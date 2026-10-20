#pragma once
#include <atomic>
#include <cstdint>
#include <cor.h>
#include <corprof.h>
#include "MapCallbackBase.h"

namespace coz::map {
constexpr uint32_t magic = 0x504D5A43u;
constexpr uint16_t version = 1;
constexpr uint32_t header_size = 704;
constexpr uint32_t entry_size = 12;
constexpr uint32_t entry_capacity = 4096;
constexpr uint32_t name_capacity = 260;

enum class State : uint32_t { not_seen = 0, capturing = 1, complete = 2 };

enum class Outcome : uint32_t {
    not_seen = 0, captured = 1, info_unavailable = 2, selection_invalid = 3, event_mask_failed = 4, jit_failed = 5,
    metadata_failed = 6, mvid_mismatch = 7, starts_failed = 8, no_native_body = 9, multiple_bodies = 10, code_info_failed = 11,
    no_code_region = 12, code_info_unexpected_count = 13, map_failed = 14, map_empty = 15, map_too_large = 16, map_count_changed = 17,
    internal_error = 18
};

enum Flag : uint32_t { second_notification = 1, unload_during_capture = 2, unloaded_after_capture = 4, closed = 8 };

enum class ReadResult : int32_t { ok = 0, not_ready = 1, buffer_too_small = 2, invalid_argument = 3 };

struct Selection {
    char16_t module[name_capacity + 1]{};
    uint32_t moduleLength{};
    mdToken token{};
    GUID mvid{};
};

// Written only by the claimant before state becomes complete.
struct Storage {
    uint32_t outcome{};
    int32_t hr{};
    uint32_t detail{};
    uint64_t functionId{}, moduleId{};
    GUID mvid{};
    mdToken token{};
    uint32_t bodiesReported{}, bodiesCopied{}, regionsReported{}, regionsCopied{}, entriesReported{}, entriesCopied{};
    uint64_t starts[2]{};
    uint64_t regionStart[2]{}, regionSize[2]{};
    uint32_t nameReported{}, nameCopied{};
    char16_t name[name_capacity]{};
    uint32_t il[entry_capacity]{}, nativeStart[entry_capacity]{}, nativeEnd[entry_capacity]{};
};

class Capture {
public:
    void reset() noexcept;
    bool parseSelection(const char* module, const char* token, const char* mvid) noexcept;
    void initialize(IUnknown* info);
    void finished(FunctionID function, HRESULT status, BOOL safeToBlock) noexcept;
    void unloadStarted(ModuleID module) noexcept;
    void shutdown() noexcept;
    ReadResult read(uint8_t* buffer, uint32_t capacity, uint32_t* required) const;

private:
    struct Name {
        uint32_t length{};
        char16_t units[name_capacity]{};
    };
    void complete(Outcome outcome, HRESULT hr, uint32_t unloadsAtEntry) noexcept;
    bool claim() noexcept;
    bool selected(ICorProfilerInfo9* info, ModuleID module, Name& name);
    void capture(ICorProfilerInfo9* info, FunctionID function, HRESULT status, ModuleID module, mdToken token, uint32_t unloadsAtEntry);
    void leave() noexcept;

    // Bits 0-1 hold the State, and the other bits count ModuleUnloadStarted calls.
    std::atomic<uint32_t> word_{0};
    std::atomic<uint32_t> flags_{0};
    std::atomic<uint32_t> functionInfoFailures_{0}, moduleInfoFailures_{0}, nameTruncations_{0}, unsafeCallbacks_{0};
    std::atomic<bool> active_{false};
    std::atomic<bool> selectionReady_{false};
    std::atomic<uint32_t> inflight_{0};
    std::atomic<ICorProfilerInfo9*> info_{nullptr};
    COR_DEBUG_IL_TO_NATIVE_MAP scratch_[entry_capacity]{};
    Selection selection_{};
    Storage storage_{};
};

Capture& capture() noexcept;

class Profiler final : public MapCallbackBase {
public:
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID riid, void** object) override;
    ULONG STDMETHODCALLTYPE AddRef() override;
    ULONG STDMETHODCALLTYPE Release() override;
    HRESULT STDMETHODCALLTYPE Initialize(IUnknown* info) override;
    HRESULT STDMETHODCALLTYPE Shutdown() override;
    HRESULT STDMETHODCALLTYPE JITCompilationFinished(FunctionID function, HRESULT status, BOOL safeToBlock) override;
    HRESULT STDMETHODCALLTYPE ModuleUnloadStarted(ModuleID module) override;
    HRESULT STDMETHODCALLTYPE JITInlining(FunctionID caller, FunctionID callee, BOOL* shouldInline) override;
    HRESULT STDMETHODCALLTYPE JITCachedFunctionSearchStarted(FunctionID function, BOOL* useCached) override;

private:
    std::atomic<ULONG> references_{1};
};

class Factory final : public IClassFactory {
public:
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID riid, void** object) override;
    ULONG STDMETHODCALLTYPE AddRef() override;
    ULONG STDMETHODCALLTYPE Release() override;
    HRESULT STDMETHODCALLTYPE CreateInstance(IUnknown* outer, REFIID riid, void** object) override;
    HRESULT STDMETHODCALLTYPE LockServer(BOOL lock) override;
};

extern const CLSID clsid;
}

extern "C" HRESULT STDMETHODCALLTYPE DllGetClassObject(REFCLSID rclsid, REFIID riid, void** object);
extern "C" int32_t coz_map_read(uint8_t* buffer, uint32_t capacity, uint32_t* required);
