#include <atomic>
#include <chrono>
#include <condition_variable>
#include <cstdlib>
#include <map>
#include <memory>
#include <mutex>
#include <new>
#include <string_view>
#include <thread>
#include <vector>
#include "ProbeLifetime.h"

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
static std::vector<BYTE> rewrittenIL;
enum class IlMapMode : LONG { Registered = 0, RawDiagnostic = 1 };
static IlMapMode ilMapMode = IlMapMode::Registered;
enum class ProbePlacement : LONG { Branch = 0, EmptyStackDiagnostic = 1 };
static ProbePlacement probePlacement = ProbePlacement::Branch;
static Sampler sampler;
static ICorProfilerInfo2* samplingInfo = nullptr;
static std::atomic<FunctionID> selectedFunction{0};
static bool boundProbes = false;
static coz::ProbeLifetime probeLifetime;

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
    enum class ProbeInterval : INT32 { Dense = 1, Small = 64, Medium = 256, Large = 1024 };
    ProbeInterval probeInterval = ProbeInterval::Dense;
    const WCHAR* targetModule = COZ_TEXT("CozNet.Instrumentation.dll");
    const WCHAR* targetType = COZ_TEXT("CozNet.Instrumentation.Workload");
    const WCHAR* targetMethod = COZ_TEXT("Work");

    HRESULT DiagnosticStackDepths(ILInstr* sentinel, std::map<ILInstr*, unsigned>& depths) {
        std::vector<ILInstr*> pending{sentinel->m_pNext};
        depths.emplace(sentinel->m_pNext, 0);
        auto visit = [&](ILInstr* target, unsigned depth) {
            if (target == sentinel) return false;
            auto entry = depths.emplace(target, depth);
            if (entry.second) pending.push_back(target);
            return entry.first->second == depth;
        };
        while (!pending.empty()) {
            auto* instruction = pending.back();
            pending.pop_back();
            unsigned pop = 0, push = 0;
            bool branch = false, fallthrough = true;
            switch (instruction->m_opcode) {
                case CEE_LDC_I4_0: case CEE_LDC_I4_1: case CEE_LDC_I4:
                case CEE_LDLOC_0: case CEE_LDLOC_1: case CEE_LDARG_0: push = 1; break;
                case CEE_STLOC_0: case CEE_STLOC_1: pop = 1; break;
                case CEE_CONV_I8: pop = 1; push = 1; break;
                case CEE_ADD: pop = 2; push = 1; break;
                case CEE_BR: case CEE_BR_S: branch = true; fallthrough = false; break;
                case CEE_BLT: case CEE_BLT_S: pop = 2; branch = true; break;
                case CEE_RET: pop = 1; fallthrough = false; break;
                default: return E_NOTIMPL;
            }
            unsigned depth = depths.at(instruction);
            if (depth < pop) return E_INVALIDARG;
            depth = depth - pop + push;
            if (instruction->m_opcode == CEE_RET && depth != 0) return E_INVALIDARG;
            if (branch && !visit(instruction->m_pTarget, depth)) return E_INVALIDARG;
            if (fallthrough && !visit(instruction->m_pNext, depth)) return E_INVALIDARG;
        }
        return S_OK;
    }

    HRESULT Rewrite(FunctionID function, ModuleID module, mdMethodDef method, mdMethodDef probe) {
        LPCBYTE body = nullptr;
        HRESULT hr = info->GetILFunctionBody(module, method, &body, nullptr);
        if (FAILED(hr)) return hr;
        COR_ILMETHOD_DECODER decoder(reinterpret_cast<COR_ILMETHOD*>(const_cast<BYTE*>(body)));
        if (decoder.EHCount() != 0) return E_NOTIMPL;

        ILRewriter rewriter(info.get(), nullptr, module, method);
        hr = rewriter.Import();
        if (FAILED(hr)) return hr;

        UINT countdown = 0;
        if (probeInterval != ProbeInterval::Dense) {
            hr = rewriter.Initialize();
            if (FAILED(hr)) return hr;
            countdown = rewriter.AddNewInt32Local();
            // The pinned helper also returns zero on failure, so local-free methods are unsupported.
            if (countdown == 0 || countdown >= 65535) return E_NOTIMPL;
        }
        auto* sentinel = rewriter.GetILList();
        std::map<ILInstr*, ULONG> originalOffsets;
        for (ULONG offset = 0; offset < decoder.GetCodeSize(); ++offset) {
            auto* instruction = rewriter.GetInstrFromOffset(offset);
            if (instruction) originalOffsets.emplace(instruction, offset);
        }

        std::map<ILInstr*, unsigned> stackDepths;
        if (probePlacement == ProbePlacement::EmptyStackDiagnostic) {
            hr = DiagnosticStackDepths(sentinel, stackDepths);
            if (FAILED(hr)) return hr;
        }

        auto insert = [&](ILInstr* before, unsigned opcode, INT32 argument = 0) {
            auto* instruction = rewriter.NewILInstr();
            if (!instruction) throw std::bad_alloc();
            instruction->m_opcode = opcode;
            instruction->m_Arg32 = argument;
            rewriter.InsertBefore(before, instruction);
            return instruction;
        };
        uint32_t probeSites = 0;
        auto emitProbe = [&](ILInstr* before) {
            if (!boundProbes) return insert(before, CEE_CALL, probe);
            auto* first = insert(before, CEE_LDC_I8);
            first->m_Arg64 = coz::ProbeLifetime::generation;
            insert(before, CEE_LDC_I4, static_cast<INT32>(++probeSites));
            insert(before, CEE_CALL, probe);
            return first;
        };
        auto* entry = sentinel->m_pNext;
        if (probeInterval != ProbeInterval::Dense) {
            insert(entry, CEE_LDC_I4, static_cast<INT32>(probeInterval));
            insert(entry, CEE_STLOC, countdown);
            emitProbe(entry);
        }
        std::map<ILInstr*, ILInstr*> probes;
        for (auto* instruction = entry; instruction != sentinel; instruction = instruction->m_pNext) {
            if (instruction->m_opcode == CEE_SWITCH || instruction->m_opcode == CEE_SWITCH_ARG) return E_NOTIMPL;
            bool backward = (s_OpCodeFlags[instruction->m_opcode] & OPCODEFLAGS_BranchTarget) != 0
                && originalOffsets.at(instruction->m_pTarget) <= originalOffsets.at(instruction);
            if (probeInterval == ProbeInterval::Dense && (instruction == entry || backward)) {
                probes.emplace(instruction, emitProbe(instruction));
            } else if (backward) {
                auto* anchor = instruction;
                if (probePlacement == ProbePlacement::EmptyStackDiagnostic) {
                    while (stackDepths.at(anchor) != 0) {
                        anchor = anchor->m_pPrev;
                        if (anchor == sentinel || stackDepths.find(anchor) == stackDepths.end()) return E_NOTIMPL;
                        if ((s_OpCodeFlags[anchor->m_opcode] & OPCODEFLAGS_BranchTarget) != 0) return E_NOTIMPL;
                    }
                    for (auto* middle = anchor->m_pNext; middle != instruction->m_pNext; middle = middle->m_pNext)
                        for (const auto& item : originalOffsets)
                            if ((s_OpCodeFlags[item.first->m_opcode] & OPCODEFLAGS_BranchTarget) != 0
                                && item.first->m_pTarget == middle) return E_NOTIMPL;
                }
                if (probes.find(anchor) != probes.end()) return E_NOTIMPL;
                auto* gate = insert(anchor, CEE_LDLOC, countdown);
                insert(anchor, CEE_LDC_I4_1);
                insert(anchor, CEE_SUB);
                insert(anchor, CEE_DUP);
                insert(anchor, CEE_STLOC, countdown);
                auto* skip = insert(anchor, CEE_BRTRUE);
                skip->m_pTarget = anchor;
                emitProbe(anchor);
                insert(anchor, CEE_LDC_I4, static_cast<INT32>(probeInterval));
                insert(anchor, CEE_STLOC, countdown);
                probes.emplace(anchor, gate);
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
        LPCBYTE rewrittenBody = nullptr;
        hr = info->GetILFunctionBody(module, method, &rewrittenBody, nullptr);
        if (FAILED(hr)) return hr;
        COR_ILMETHOD_DECODER rewrittenDecoder(reinterpret_cast<COR_ILMETHOD*>(const_cast<BYTE*>(rewrittenBody)));
        rewrittenIL.assign(rewrittenDecoder.Code, rewrittenDecoder.Code + rewrittenDecoder.GetCodeSize());
        std::map<ULONG, ULONG> sorted;
        for (const auto& item : originalOffsets) sorted.emplace(item.second, item.first->m_offset);
        codeMap.clear();
        for (const auto& item : sorted) codeMap.push_back({item.first, item.second, TRUE});
        if (ilMapMode == IlMapMode::Registered) {
            hr = info->SetILInstrumentedCodeMap(function, TRUE, static_cast<ULONG>(codeMap.size()), codeMap.data());
            if (FAILED(hr)) return hr;
        }
        if (boundProbes && !probeLifetime.rewritten(probeSites)) return E_FAIL;
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
        const char* identity = std::getenv("COZNET_PROBE_IDENTITY");
        if (!identity || std::string_view(identity) == "legacy") boundProbes = false;
        else if (std::string_view(identity) == "bound") boundProbes = true;
        else { status.store(E_INVALIDARG); return E_INVALIDARG; }
        const char* mapMode = std::getenv("COZNET_IL_MAP_MODE");
        if (!mapMode || std::string_view(mapMode) == "registered") ilMapMode = IlMapMode::Registered;
        else if (std::string_view(mapMode) == "raw-diagnostic") ilMapMode = IlMapMode::RawDiagnostic;
        else { status.store(E_INVALIDARG); return E_INVALIDARG; }
        const char* interval = std::getenv("COZNET_PROBE_INTERVAL");
        if (!interval || std::string_view(interval) == "1") probeInterval = ProbeInterval::Dense;
        else if (std::string_view(interval) == "64") probeInterval = ProbeInterval::Small;
        else if (std::string_view(interval) == "256") probeInterval = ProbeInterval::Medium;
        else if (std::string_view(interval) == "1024") probeInterval = ProbeInterval::Large;
        else { status.store(E_INVALIDARG); return E_INVALIDARG; }
        const char* placement = std::getenv("COZNET_PROBE_PLACEMENT");
        if (!placement || std::string_view(placement) == "branch") probePlacement = ProbePlacement::Branch;
        else if (std::string_view(placement) == "empty-stack-diagnostic") probePlacement = ProbePlacement::EmptyStackDiagnostic;
        else { status.store(E_INVALIDARG); return E_INVALIDARG; }
        if (probePlacement == ProbePlacement::EmptyStackDiagnostic && probeInterval == ProbeInterval::Dense) {
            status.store(E_INVALIDARG);
            return E_INVALIDARG;
        }
        const char* target = std::getenv("COZNET_PROBE_TARGET");
        if (target && std::string_view(target) == "linux-perf") {
            targetModule = COZ_TEXT("CozNet.LinuxPerf.dll");
            targetType = COZ_TEXT("CpuWorkload");
            targetMethod = COZ_TEXT("Run");
        } else if (target && std::string_view(target) != "instrumentation") {
            status.store(E_INVALIDARG);
            return E_INVALIDARG;
        }
        if (boundProbes && (!target || std::string_view(target) != "linux-perf" || ilMapMode != IlMapMode::Registered)) {
            status.store(E_INVALIDARG); return E_INVALIDARG;
        }
        HRESULT hr = runtime->QueryInterface(__uuidof(ICorProfilerInfo), reinterpret_cast<void**>(info.address()));
        if (SUCCEEDED(hr)) hr = runtime->QueryInterface(__uuidof(ICorProfilerInfo2), reinterpret_cast<void**>(&samplingInfo));
        const DWORD events = COR_PRF_MONITOR_JIT_COMPILATION | COR_PRF_ENABLE_STACK_SNAPSHOT | COR_PRF_MONITOR_THREADS;
        if (SUCCEEDED(hr) && boundProbes) {
            ComOwner<ICorProfilerInfo5> policy;
            hr = runtime->QueryInterface(__uuidof(ICorProfilerInfo5), reinterpret_cast<void**>(policy.address()));
            if (SUCCEEDED(hr)) hr = policy->SetEventMask2(events | COR_PRF_MONITOR_MODULE_LOADS |
                COR_PRF_DISABLE_ALL_NGEN_IMAGES | COR_PRF_DISABLE_INLINING, COR_PRF_HIGH_DISABLE_TIERED_COMPILATION);
        } else if (SUCCEEDED(hr)) hr = info->SetEventMask(events);
        status.store(FAILED(hr) ? hr : 1);
        return hr;
    }
    HRESULT STDMETHODCALLTYPE ThreadDestroyed(ThreadID thread) override {
        sampler.ThreadDestroyed(thread);
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE JITCompilationFinished(FunctionID function, HRESULT result, BOOL) override {
        if (boundProbes) {
            probeLifetime.finish(function, SUCCEEDED(result));
            return S_OK;
        }
        if (function != selectedFunction) return S_OK;
        try {
            if (SUCCEEDED(result)) result = sampler.Prepare(samplingInfo, function);
            if (FAILED(result)) status.store(result);
        } catch (...) { status.store(E_FAIL); }
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE Shutdown() override {
        if (boundProbes) probeLifetime.invalidate(coz::ProbeLifetimeState::shutdown);
        sampler.Stop();
        if (samplingInfo) { samplingInfo->Release(); samplingInfo = nullptr; }
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE JITInlining(FunctionID, FunctionID, BOOL* shouldInline) override {
        *shouldInline = boundProbes ? FALSE : TRUE;
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE JITCachedFunctionSearchStarted(FunctionID, BOOL* useCached) override {
        *useCached = boundProbes ? FALSE : TRUE;
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE ModuleUnloadStarted(ModuleID module) override {
        if (boundProbes) probeLifetime.moduleUnloading(module);
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
            if (std::basic_string_view<WCHAR>(file) != targetModule) return S_OK;
            ComOwner<IMetaDataImport> metadata;
            hr = info->GetModuleMetaData(module, ofRead, IID_IMetaDataImport, reinterpret_cast<IUnknown**>(metadata.address()));
            if (FAILED(hr)) { status.store(hr); return S_OK; }
            WCHAR name[256];
            mdTypeDef type;
            hr = metadata->GetMethodProps(token, &type, name, 256, nullptr, nullptr, nullptr, nullptr, nullptr, nullptr);
            if (FAILED(hr) || std::basic_string_view<WCHAR>(name) != targetMethod) return S_OK;
            hr = metadata->GetTypeDefProps(type, name, 256, nullptr, nullptr, nullptr);
            if (FAILED(hr) || std::basic_string_view<WCHAR>(name) != targetType) return S_OK;
            if (boundProbes && !probeLifetime.begin(module, function, token)) { status.store(E_FAIL); return S_OK; }
            LONG expected = 1;
            if (!status.compare_exchange_strong(expected, 3)) return S_OK;
            if (!safeToBlock) {
                if (boundProbes) probeLifetime.invalidate(coz::ProbeLifetimeState::compilationFailed);
                status.store(E_FAIL); return S_OK;
            }
            BYTE signature[] = {IMAGE_CEE_CS_CALLCONV_DEFAULT, 0, ELEMENT_TYPE_VOID};
            BYTE boundSignature[] = {IMAGE_CEE_CS_CALLCONV_DEFAULT, 2, ELEMENT_TYPE_VOID, ELEMENT_TYPE_U8, ELEMENT_TYPE_U4};
            mdMethodDef probe;
            hr = boundProbes ? metadata->FindMethod(type, COZ_TEXT("ProbeBound"), boundSignature, sizeof(boundSignature), &probe)
                : metadata->FindMethod(type, COZ_TEXT("Probe"), signature, sizeof(signature), &probe);
            if (SUCCEEDED(hr)) hr = Rewrite(function, module, token, probe);
            if (FAILED(hr) && boundProbes) probeLifetime.invalidate(coz::ProbeLifetimeState::compilationFailed);
            if (SUCCEEDED(hr)) selectedFunction = function;
            status.store(FAILED(hr) ? hr : 2);
        } catch (...) {
            if (boundProbes) probeLifetime.invalidate(coz::ProbeLifetimeState::compilationFailed);
            status.store(E_FAIL);
        }
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
COZ_EXPORT LONG __stdcall CozGetProbeLifetimeState() {
    return boundProbes ? static_cast<LONG>(probeLifetime.state()) : -1;
}
COZ_EXPORT BOOL __stdcall CozGetProbeBinding(uint64_t* generation, UINT_PTR* module, UINT_PTR* function, ULONG* method, ULONG* sites) {
    if (!generation || !module || !function || !method || !sites) return FALSE;
    *generation = 0; *module = 0; *function = 0; *method = 0; *sites = 0;
    coz::ProbeBinding binding{};
    if (!boundProbes || !probeLifetime.read(binding)) return FALSE;
    *generation = binding.generation; *module = binding.module; *function = binding.function;
    *method = binding.method; *sites = binding.sites;
    return TRUE;
}
COZ_EXPORT BOOL __stdcall CozIsProbeBindingCurrent(uint64_t generation, ULONG site) {
    return boundProbes && probeLifetime.current(generation, site);
}
COZ_EXPORT HRESULT __stdcall CozStartSampling() {
    if (boundProbes) return E_NOTIMPL;
    if (status.load() != 2 || !samplingInfo) return E_UNEXPECTED;
    if (ilMapMode == IlMapMode::RawDiagnostic) return E_NOTIMPL;
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
COZ_EXPORT LONG __stdcall CozGetILMapMode() { return status.load() == 2 ? static_cast<LONG>(ilMapMode) : -1; }
COZ_EXPORT LONG __stdcall CozGetProbePlacement() { return status.load() == 2 ? static_cast<LONG>(probePlacement) : -1; }
COZ_EXPORT ULONG __stdcall CozGetRewrittenILSize() { return status.load() == 2 ? static_cast<ULONG>(rewrittenIL.size()) : 0; }
COZ_EXPORT LONG __stdcall CozGetRewrittenILByte(ULONG index) {
    return status.load() == 2 && index < rewrittenIL.size() ? rewrittenIL[index] : -1;
}
COZ_EXPORT ULONG __stdcall CozGetNativeMapSize() { return status.load() == 2 ? sampler.NativeMapSize() : 0; }
COZ_EXPORT BOOL __stdcall CozGetNativeMapEntry(ULONG index, ULONG* il, ULONG* start, ULONG* end) {
    return status.load() == 2 && sampler.NativeMapEntry(index, il, start, end);
}
COZ_EXPORT BOOL __stdcall CozGetNativeCodeInfo(UINT_PTR* address, SIZE_T* size) {
    return status.load() == 2 && sampler.NativeCodeInfo(address, size);
}
COZ_EXPORT ULONG __stdcall CozGetMapSize() {
    return status.load() == 2 ? static_cast<ULONG>(codeMap.size()) : 0;
}
COZ_EXPORT BOOL __stdcall CozGetMapEntry(ULONG index, ULONG* original, ULONG* rewritten) {
    if (status.load() != 2 || index >= codeMap.size() || !original || !rewritten) return FALSE;
    *original = codeMap[index].oldOffset;
    *rewritten = codeMap[index].newOffset;
    return TRUE;
}
