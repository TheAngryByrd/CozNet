#include <atomic>
#include <chrono>
#include <condition_variable>
#include <map>
#include <memory>
#include <mutex>
#include <new>
#include <string_view>
#include <thread>
#include <vector>

// PAL annotation macros conflict with libstdc++ declarations.
#ifdef _WIN32
#include <initguid.h>
#define COZ_TEXT(value) L##value
#else
#define INITGUID
#include <guiddef.h>
#define COZ_TEXT(value) u##value
#endif
#include "CallbackBase.h"
#include "ilrewriter.h"
#include "Sampler.h"
#include "Exports.h"

static const GUID ProfilerId = {0x6e51c528, 0x23f1, 0x49ea, {0x93, 0x46, 0x2a, 0x7a, 0x8d, 0x91, 0x1b, 0x61}};
static std::atomic<LONG> status{0};
static std::atomic<LONG> instrumented{0};
static std::vector<COR_IL_MAP> codeMap;
static Sampler sampler;
static ICorProfilerInfo2* samplingInfo = nullptr;
static FunctionID selectedFunction = 0;

template <typename T> class ComOwner {
    T* value = nullptr;
public:
    ~ComOwner() { if (value) value->Release(); }
    T* operator->() const { return value; }
    T** address() { return &value; }
    T* get() const { return value; }
};

class Profiler final : public CallbackBase {
    std::atomic<ULONG> references{1};
    ComOwner<ICorProfilerInfo> info;

    HRESULT Rewrite(FunctionID function, ModuleID module, mdMethodDef method, mdMethodDef probe) {
        LPCBYTE body = nullptr;
        HRESULT hr = info->GetILFunctionBody(module, method, &body, nullptr);
        if (FAILED(hr)) return hr;
        COR_ILMETHOD_DECODER decoder(reinterpret_cast<COR_ILMETHOD*>(const_cast<BYTE*>(body)));
        if (decoder.EHCount() != 0) return E_NOTIMPL;

        ILRewriter rewriter(info.get(), nullptr, module, method);
        hr = rewriter.Import();
        if (FAILED(hr)) return hr;
        auto* sentinel = rewriter.GetILList();
        std::map<ILInstr*, ULONG> originalOffsets;
        for (ULONG offset = 0; offset < decoder.GetCodeSize(); ++offset) {
            auto* instruction = rewriter.GetInstrFromOffset(offset);
            if (instruction) originalOffsets.emplace(instruction, offset);
        }

        std::map<ILInstr*, ILInstr*> probes;
        for (auto* instruction = sentinel->m_pNext; instruction != sentinel; instruction = instruction->m_pNext) {
            if (instruction->m_opcode == CEE_SWITCH || instruction->m_opcode == CEE_SWITCH_ARG) return E_NOTIMPL;
            bool backward = (s_OpCodeFlags[instruction->m_opcode] & OPCODEFLAGS_BranchTarget) != 0
                && originalOffsets.at(instruction->m_pTarget) <= originalOffsets.at(instruction);
            if (instruction == sentinel->m_pNext || backward) {
                auto* call = rewriter.NewILInstr();
                if (!call) return E_OUTOFMEMORY;
                call->m_opcode = CEE_CALL;
                call->m_Arg32 = probe;
                rewriter.InsertBefore(instruction, call);
                probes.emplace(instruction, call);
            }
        }
        for (const auto& item : originalOffsets) {
            auto* instruction = item.first;
            if ((s_OpCodeFlags[instruction->m_opcode] & OPCODEFLAGS_BranchTarget) != 0) {
                const auto redirected = probes.find(instruction->m_pTarget);
                if (redirected != probes.end()) instruction->m_pTarget = redirected->second;
            }
        }
        hr = rewriter.Export();
        if (FAILED(hr)) return hr;
        std::map<ULONG, ULONG> sorted;
        for (const auto& item : originalOffsets) sorted.emplace(item.second, item.first->m_offset);
        codeMap.clear();
        for (const auto& item : sorted) codeMap.push_back({item.first, item.second, TRUE});
        hr = info->SetILInstrumentedCodeMap(function, TRUE, static_cast<ULONG>(codeMap.size()), codeMap.data());
        if (FAILED(hr)) return hr;
        ++instrumented;
        return S_OK;
    }

public:
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID id, void** result) override {
        if (!result) return E_POINTER;
        *result = nullptr;
        if (id == __uuidof(IUnknown) || id == __uuidof(ICorProfilerCallback) || id == __uuidof(ICorProfilerCallback2)) {
            *result = static_cast<ICorProfilerCallback2*>(this);
            AddRef();
            return S_OK;
        }
        return E_NOINTERFACE;
    }
    ULONG STDMETHODCALLTYPE AddRef() override { return ++references; }
    ULONG STDMETHODCALLTYPE Release() override {
        auto remaining = --references;
        if (!remaining) delete this;
        return remaining;
    }
    HRESULT STDMETHODCALLTYPE Initialize(IUnknown* runtime) override {
        HRESULT hr = runtime->QueryInterface(__uuidof(ICorProfilerInfo), reinterpret_cast<void**>(info.address()));
        if (SUCCEEDED(hr)) hr = runtime->QueryInterface(__uuidof(ICorProfilerInfo2), reinterpret_cast<void**>(&samplingInfo));
        if (SUCCEEDED(hr)) hr = info->SetEventMask(COR_PRF_MONITOR_JIT_COMPILATION | COR_PRF_ENABLE_STACK_SNAPSHOT | COR_PRF_MONITOR_THREADS);
        status.store(FAILED(hr) ? hr : 1);
        return hr;
    }
    HRESULT STDMETHODCALLTYPE ThreadDestroyed(ThreadID thread) override {
        sampler.ThreadDestroyed(thread);
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE JITCompilationFinished(FunctionID function, HRESULT result, BOOL) override {
        if (function != selectedFunction) return S_OK;
        try {
            if (SUCCEEDED(result)) result = sampler.Prepare(samplingInfo, function);
            if (FAILED(result)) status.store(result);
        } catch (...) { status.store(E_FAIL); }
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE Shutdown() override {
        sampler.Stop();
        if (samplingInfo) { samplingInfo->Release(); samplingInfo = nullptr; }
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE JITInlining(FunctionID, FunctionID, BOOL* shouldInline) override {
        *shouldInline = TRUE;
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE JITCachedFunctionSearchStarted(FunctionID, BOOL* useCached) override {
        *useCached = TRUE;
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE JITCompilationStarted(FunctionID function, BOOL safeToBlock) override {
        try {
            ModuleID module;
            mdToken token;
            HRESULT hr = info->GetFunctionInfo(function, nullptr, &module, &token);
            if (FAILED(hr)) return S_OK;
            WCHAR path[4096];
            hr = info->GetModuleInfo(module, nullptr, 4096, nullptr, path, nullptr);
            if (FAILED(hr)) return S_OK;
            const WCHAR* file = path;
            for (const WCHAR* cursor = path; *cursor; ++cursor)
                if (*cursor == L'\\' || *cursor == L'/') file = cursor + 1;
            if (std::basic_string_view<WCHAR>(file) != COZ_TEXT("CozNet.Instrumentation.dll")) return S_OK;
            ComOwner<IMetaDataImport> metadata;
            hr = info->GetModuleMetaData(module, ofRead, IID_IMetaDataImport, reinterpret_cast<IUnknown**>(metadata.address()));
            if (FAILED(hr)) { status.store(hr); return S_OK; }
            WCHAR name[256];
            mdTypeDef type;
            hr = metadata->GetMethodProps(token, &type, name, 256, nullptr, nullptr, nullptr, nullptr, nullptr, nullptr);
            if (FAILED(hr) || std::basic_string_view<WCHAR>(name) != COZ_TEXT("Work")) return S_OK;
            hr = metadata->GetTypeDefProps(type, name, 256, nullptr, nullptr, nullptr);
            if (FAILED(hr) || std::basic_string_view<WCHAR>(name) != COZ_TEXT("CozNet.Instrumentation.Workload")) return S_OK;
            LONG expected = 1;
            if (!status.compare_exchange_strong(expected, 3)) return S_OK;
            if (!safeToBlock) { status.store(E_FAIL); return S_OK; }
            BYTE signature[] = {IMAGE_CEE_CS_CALLCONV_DEFAULT, 0, ELEMENT_TYPE_VOID};
            mdMethodDef probe;
            hr = metadata->FindMethod(type, COZ_TEXT("Probe"), signature, sizeof(signature), &probe);
            if (SUCCEEDED(hr)) hr = Rewrite(function, module, token, probe);
            if (SUCCEEDED(hr)) selectedFunction = function;
            status.store(FAILED(hr) ? hr : 2);
        } catch (...) { status.store(E_FAIL); }
        return S_OK;
    }
};

class Factory final : public IClassFactory {
    std::atomic<ULONG> references{1};
public:
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID id, void** result) override {
        if (!result) return E_POINTER;
        *result = nullptr;
        if (id == __uuidof(IUnknown) || id == __uuidof(IClassFactory)) {
            *result = static_cast<IClassFactory*>(this);
            AddRef();
            return S_OK;
        }
        return E_NOINTERFACE;
    }
    ULONG STDMETHODCALLTYPE AddRef() override { return ++references; }
    ULONG STDMETHODCALLTYPE Release() override {
        auto remaining = --references;
        if (!remaining) delete this;
        return remaining;
    }
    HRESULT STDMETHODCALLTYPE CreateInstance(IUnknown* outer, REFIID id, void** result) override {
        if (!result) return E_POINTER;
        *result = nullptr;
        if (outer) return CLASS_E_NOAGGREGATION;
        auto* profiler = new (std::nothrow) Profiler();
        if (!profiler) return E_OUTOFMEMORY;
        auto hr = profiler->QueryInterface(id, result);
        profiler->Release();
        return hr;
    }
    HRESULT STDMETHODCALLTYPE LockServer(BOOL) override { return S_OK; }
};

COZ_CLASS_EXPORT HRESULT __stdcall DllGetClassObject(REFCLSID clsid, REFIID id, void** result) {
    if (!result) return E_POINTER;
    *result = nullptr;
    if (clsid != ProfilerId) return CLASS_E_CLASSNOTAVAILABLE;
    auto* factory = new (std::nothrow) Factory();
    if (!factory) return E_OUTOFMEMORY;
    auto hr = factory->QueryInterface(id, result);
    factory->Release();
    return hr;
}

COZ_EXPORT LONG __stdcall CozGetStatus() { return status.load(); }
COZ_EXPORT HRESULT __stdcall CozStartSampling() {
    if (status.load() != 2 || !samplingInfo) return E_UNEXPECTED;
    return sampler.Start(samplingInfo, selectedFunction);
}
COZ_EXPORT void __stdcall CozStopSampling() { sampler.Stop(); }
COZ_EXPORT ULONG __stdcall CozGetSampleAttempts() { return sampler.Attempts(); }
COZ_EXPORT ULONG __stdcall CozGetSampleFailures() { return sampler.Failures(); }
COZ_EXPORT ULONG __stdcall CozGetSelectedStacks() { return sampler.SelectedStacks(); }
COZ_EXPORT LONG __stdcall CozGetLastSampleFailure() { return sampler.LastFailure(); }
COZ_EXPORT ULONG __stdcall CozGetMappedStacks() { return sampler.MappedStacks(); }
COZ_EXPORT ULONG __stdcall CozGetLastSampleIL() { return sampler.LastIL(); }
COZ_EXPORT std::uint64_t __stdcall CozGetObservedCpuNanoseconds() { return sampler.ObservedCpu(); }
COZ_EXPORT std::uint64_t __stdcall CozGetMaxCpuDeltaNanoseconds() { return sampler.MaxCpuDelta(); }
COZ_EXPORT ULONG __stdcall CozGetClockReads() { return sampler.ClockReads(); }
COZ_EXPORT ULONG __stdcall CozGetZeroCpuDeltas() { return sampler.ZeroCpuDeltas(); }
COZ_EXPORT LONG __stdcall CozGetClockFailure() { return sampler.ClockFailure(); }
COZ_EXPORT ULONG __stdcall CozGetSampleLocationCount() { return sampler.LocationCount(); }
COZ_EXPORT BOOL __stdcall CozGetSampleLocation(ULONG index, ULONG* il, ULONG* count) {
    return sampler.Location(index, il, count);
}
COZ_EXPORT LONG __stdcall CozGetInstrumentedMethods() { return instrumented.load(); }
COZ_EXPORT ULONG __stdcall CozGetMapSize() {
    return status.load() == 2 ? static_cast<ULONG>(codeMap.size()) : 0;
}
COZ_EXPORT BOOL __stdcall CozGetMapEntry(ULONG index, ULONG* original, ULONG* rewritten) {
    if (status.load() != 2 || index >= codeMap.size() || !original || !rewritten) return FALSE;
    *original = codeMap[index].oldOffset;
    *rewritten = codeMap[index].newOffset;
    return TRUE;
}
