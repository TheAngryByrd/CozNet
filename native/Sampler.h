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
