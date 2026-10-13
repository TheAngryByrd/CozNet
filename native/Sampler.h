#pragma once
#include "SnapshotBackend.h"
#include <atomic>
#include <chrono>
#include <condition_variable>
#include <mutex>
#include <thread>
#include <vector>

class Sampler final {
    std::unique_ptr<SnapshotBackend> backend;
    std::mutex control;
    std::mutex wakeMutex;
    std::condition_variable wake;
    std::thread worker;
    bool stopping = false;
    ThreadID target = 0;
    FunctionID selected = 0;
    std::atomic<ULONG> attempts{0};
    std::atomic<ULONG> failures{0};
    std::atomic<ULONG> selectedStacks{0};
    std::atomic<LONG> lastFailure{S_OK};
    bool found = false;
    UINT_PTR sampledAddress = 0;
    COR_PRF_CODE_INFO code{};
    std::vector<COR_DEBUG_IL_TO_NATIVE_MAP> locations;
    std::vector<ULONG> locationCounts;
    std::atomic<ULONG> mappedStacks{0};
    std::atomic<ULONG> lastIL{static_cast<ULONG>(-1)};
    std::uint64_t initialCpu = 0;
    std::uint64_t previousCpu = 0;
    std::atomic<std::uint64_t> observedCpu{0};
    std::atomic<std::uint64_t> maxCpuDelta{0};
    std::atomic<ULONG> clockReads{0};
    std::atomic<ULONG> zeroCpuDeltas{0};
    std::atomic<LONG> clockFailure{S_OK};

    static HRESULT __stdcall Frame(FunctionID function, UINT_PTR address, COR_PRF_FRAME_INFO,
        ULONG32, BYTE[], void* context) {
        auto* self = static_cast<Sampler*>(context);
        if (function == self->selected && !self->found) {
            self->found = true;
            self->sampledAddress = address;
        }
        return S_OK;
    }

    void Run() {
        std::unique_lock<std::mutex> lock(wakeMutex);
        while (!wake.wait_for(lock, std::chrono::milliseconds(1), [this] { return stopping; })) {
            lock.unlock();
            std::uint64_t cpu;
            auto clockResult = backend->ReadCpuNanoseconds(&cpu);
            if (SUCCEEDED(clockResult) && cpu < previousCpu) clockResult = E_FAIL;
            if (FAILED(clockResult)) {
                clockFailure.store(clockResult);
            } else {
                ++clockReads;
                auto delta = cpu - previousCpu;
                if (delta == 0) ++zeroCpuDeltas;
                if (delta > maxCpuDelta.load()) maxCpuDelta.store(delta);
                previousCpu = cpu;
                observedCpu.store(cpu - initialCpu);
            }
            found = false;
            auto hr = backend->Capture(target, Frame, this);
            ++attempts;
            if (FAILED(hr)) {
                ++failures;
                lastFailure.store(hr);
            } else if (found) {
                ++selectedStacks;
                if (sampledAddress >= code.startAddress && sampledAddress - code.startAddress < code.size) {
                    auto offset = sampledAddress - code.startAddress;
                    for (size_t i = 0; i < locations.size(); ++i) {
                        const auto& location = locations[i];
                        if (static_cast<LONG>(location.ilOffset) >= 0 &&
                            offset >= location.nativeStartOffset && offset < location.nativeEndOffset) {
                            ++mappedStacks;
                            ++locationCounts[i];
                            lastIL.store(location.ilOffset);
                            break;
                        }
                    }
                }
            }
            lock.lock();
        }
    }

    void StopLocked() {
        if (!worker.joinable()) return;
        {
            std::lock_guard<std::mutex> lock(wakeMutex);
            stopping = true;
        }
        wake.notify_one();
        worker.join();
        backend.reset();
        target = 0;
    }

public:
    ~Sampler() { Stop(); }

    HRESULT Prepare(ICorProfilerInfo2* runtime, FunctionID function) {
        std::lock_guard<std::mutex> lock(control);
        if (worker.joinable()) return E_NOTIMPL;
        HRESULT hr;
        ULONG32 count = 0;
        hr = runtime->GetCodeInfo2(function, 1, &count, &code);
        if (FAILED(hr)) return hr;
        if (count != 1) return E_NOTIMPL;
        hr = runtime->GetILToNativeMapping(function, 0, &count, nullptr);
        if (FAILED(hr)) return hr;
        locations.resize(count);
        hr = runtime->GetILToNativeMapping(function, count, &count, locations.data());
        if (FAILED(hr)) return hr;
        if (count > locations.size()) return E_FAIL;
        locations.resize(count);
        return S_OK;
    }

    HRESULT Start(ICorProfilerInfo2* runtime, FunctionID function) {
        std::lock_guard<std::mutex> lock(control);
        if (worker.joinable() || !function) return E_UNEXPECTED;
        auto hr = runtime->GetCurrentThreadID(&target);
        if (FAILED(hr)) return hr;
        try {
            backend = CreateSnapshotBackend(runtime);
            if (!backend) return E_NOTIMPL;
            hr = backend->ReadCpuNanoseconds(&initialCpu);
            if (FAILED(hr)) { backend.reset(); return hr; }
            previousCpu = initialCpu;
            observedCpu.store(0);
            maxCpuDelta.store(0);
            clockReads.store(0);
            zeroCpuDeltas.store(0);
            clockFailure.store(S_OK);
            selected = function;
            locationCounts.assign(locations.size(), 0);
            stopping = false;
            attempts.store(0);
            failures.store(0);
            selectedStacks.store(0);
            lastFailure.store(S_OK);
            mappedStacks.store(0);
            lastIL.store(static_cast<ULONG>(-1));
            worker = std::thread([this] { Run(); });
            return S_OK;
        } catch (...) {
            backend.reset();
            target = 0;
            return E_FAIL;
        }
    }

    ULONG NativeMapSize() {
        std::lock_guard<std::mutex> lock(control);
        return static_cast<ULONG>(locations.size());
    }
    BOOL NativeMapEntry(ULONG index, ULONG* il, ULONG* start, ULONG* end) {
        std::lock_guard<std::mutex> lock(control);
        if (index >= locations.size() || !il || !start || !end) return FALSE;
        const auto& location = locations[index];
        *il = location.ilOffset;
        *start = location.nativeStartOffset;
        *end = location.nativeEndOffset;
        return TRUE;
    }
    BOOL NativeCodeInfo(UINT_PTR* address, SIZE_T* size) {
        std::lock_guard<std::mutex> lock(control);
        if (!address || !size || code.size == 0) return FALSE;
        *address = code.startAddress;
        *size = code.size;
        return TRUE;
    }

    void Stop() {
        std::lock_guard<std::mutex> lock(control);
        StopLocked();
    }

    void ThreadDestroyed(ThreadID thread) {
        std::lock_guard<std::mutex> lock(control);
        if (thread == target) StopLocked();
    }
    ULONG Attempts() const { return attempts.load(); }
    ULONG Failures() const { return failures.load(); }
    ULONG SelectedStacks() const { return selectedStacks.load(); }
    LONG LastFailure() const { return lastFailure.load(); }
    ULONG MappedStacks() const { return mappedStacks.load(); }
    ULONG LastIL() const { return lastIL.load(); }
    std::uint64_t ObservedCpu() const { return observedCpu.load(); }
    std::uint64_t MaxCpuDelta() const { return maxCpuDelta.load(); }
    ULONG ClockReads() const { return clockReads.load(); }
    ULONG ZeroCpuDeltas() const { return zeroCpuDeltas.load(); }
    LONG ClockFailure() const { return clockFailure.load(); }
    ULONG LocationCount() {
        std::lock_guard<std::mutex> lock(control);
        return worker.joinable() ? 0 : static_cast<ULONG>(locationCounts.size());
    }
    BOOL Location(ULONG index, ULONG* il, ULONG* count) {
        std::lock_guard<std::mutex> lock(control);
        if (worker.joinable() || index >= locationCounts.size() || !il || !count) return FALSE;
        *il = locations[index].ilOffset;
        *count = locationCounts[index];
        return TRUE;
    }
};
