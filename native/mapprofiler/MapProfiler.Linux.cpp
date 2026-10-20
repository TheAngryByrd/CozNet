#include <cstdlib>
#include <cstring>
#include <new>
#include <utility>
#include "MapProfiler.h"

#define COZ_MAP_EXPORT __attribute__((visibility("default")))

namespace coz::map {
// {4B1F2C70-6D3A-4E0B-9C51-7A2E8D4F6B13}
const CLSID clsid = {0x4B1F2C70, 0x6D3A, 0x4E0B, {0x9C, 0x51, 0x7A, 0x2E, 0x8D, 0x4F, 0x6B, 0x13}};

namespace {
constexpr HRESULT insufficient_buffer = static_cast<HRESULT>(0x8007007Au);

bool hexDigit(char c, uint32_t& value) {
    if (c >= '0' && c <= '9') value = static_cast<uint32_t>(c - '0');
    else if (c >= 'a' && c <= 'f') value = static_cast<uint32_t>(c - 'a' + 10);
    else if (c >= 'A' && c <= 'F') value = static_cast<uint32_t>(c - 'A' + 10);
    else return false;
    return true;
}

bool parseHex(const char* text, size_t length, uint64_t& value) {
    value = 0;
    for (size_t i = 0; i < length; ++i) {
        uint32_t digit;
        if (!hexDigit(text[i], digit)) return false;
        value = (value << 4) | digit;
    }
    return true;
}

bool parseGuid(const char* text, GUID& guid) {
    if (std::strlen(text) != 36 || text[8] != '-' || text[13] != '-' || text[18] != '-' || text[23] != '-') return false;
    uint64_t a, b, c, d, e;
    if (!parseHex(text, 8, a) || !parseHex(text + 9, 4, b) || !parseHex(text + 14, 4, c) || !parseHex(text + 19, 4, d) || !parseHex(text + 24, 12, e)) return false;
    guid.Data1 = static_cast<uint32_t>(a);
    guid.Data2 = static_cast<uint16_t>(b);
    guid.Data3 = static_cast<uint16_t>(c);
    guid.Data4[0] = static_cast<uint8_t>(d >> 8);
    guid.Data4[1] = static_cast<uint8_t>(d);
    for (int i = 0; i < 6; ++i) guid.Data4[2 + i] = static_cast<uint8_t>(e >> (40 - 8 * i));
    return true;
}

void put16(uint8_t* p, uint16_t v) { p[0] = static_cast<uint8_t>(v); p[1] = static_cast<uint8_t>(v >> 8); }
void put32(uint8_t* p, uint32_t v) { for (int i = 0; i < 4; ++i) p[i] = static_cast<uint8_t>(v >> (8 * i)); }
void put64(uint8_t* p, uint64_t v) { for (int i = 0; i < 8; ++i) p[i] = static_cast<uint8_t>(v >> (8 * i)); }
void putGuid(uint8_t* p, const GUID& g) {
    put32(p, g.Data1);
    put16(p + 4, g.Data2);
    put16(p + 6, g.Data3);
    std::memcpy(p + 8, g.Data4, 8);
}

class MetadataHolder {
public:
    IUnknown* value{};
    ~MetadataHolder() { if (value) value->Release(); }
};
}

Capture& capture() noexcept {
    static Capture instance;
    return instance;
}

void Capture::reset() noexcept {
    word_.store(0);
    flags_.store(0);
    functionInfoFailures_.store(0);
    moduleInfoFailures_.store(0);
    nameTruncations_.store(0);
    unsafeCallbacks_.store(0);
    active_.store(false);
    selectionReady_.store(false);
    inflight_.store(0);
    info_.store(nullptr);
    selection_ = Selection{};
    std::memset(static_cast<void*>(&storage_), 0, sizeof(storage_));
}

bool Capture::parseSelection(const char* module, const char* token, const char* mvid) noexcept {
    if (!module || !token || !mvid) return false;
    const size_t length = std::strlen(module);
    if (length == 0 || length > name_capacity || std::strchr(module, '/')) return false;
    for (size_t i = 0; i < length; ++i) {
        if (static_cast<unsigned char>(module[i]) > 0x7F) return false;
        selection_.module[i] = static_cast<char16_t>(module[i]);
    }
    selection_.moduleLength = static_cast<uint32_t>(length);
    uint64_t value;
    if (std::strncmp(token, "0x", 2) != 0 || std::strlen(token) != 10 || !parseHex(token + 2, 8, value) || (value >> 24) != 0x06 || (value & 0xFFFFFF) == 0) return false;
    selection_.token = static_cast<mdToken>(value);
    return parseGuid(mvid, selection_.mvid);
}

void Capture::complete(Outcome outcome, HRESULT hr, uint32_t unloadsAtEntry) noexcept {
    storage_.outcome = static_cast<uint32_t>(outcome);
    storage_.hr = hr;
    uint32_t word = word_.load();
    for (;;) {
        if ((word >> 2) != unloadsAtEntry) flags_.fetch_or(unload_during_capture);
        if (word_.compare_exchange_weak(word, (word & ~3u) | static_cast<uint32_t>(State::complete))) return;
    }
}

bool Capture::claim() noexcept {
    uint32_t word = word_.load();
    while ((word & 3u) == static_cast<uint32_t>(State::not_seen))
        if (word_.compare_exchange_weak(word, word | static_cast<uint32_t>(State::capturing))) return true;
    return false;
}

void Capture::initialize(IUnknown* unknown) {
    const uint32_t unloads = word_.load() >> 2;
    ICorProfilerInfo9* info = nullptr;
    const auto fail = [&](Outcome outcome, HRESULT hr) {
        if (ICorProfilerInfo9* held = std::exchange(info, nullptr)) held->Release();
        if (claim()) complete(outcome, hr, unloads);
    };
    try {
        if (!unknown) return fail(Outcome::info_unavailable, E_POINTER);
        const HRESULT query = unknown->QueryInterface(IID_ICorProfilerInfo9, reinterpret_cast<void**>(&info));
        if (FAILED(query) || !info) {
            info = nullptr;
            return fail(Outcome::info_unavailable, FAILED(query) ? query : E_NOINTERFACE);
        }
        if (!parseSelection(std::getenv("COZNET_MAP_MODULE"), std::getenv("COZNET_MAP_TOKEN"), std::getenv("COZNET_MAP_MVID")))
            return fail(Outcome::selection_invalid, E_INVALIDARG);
        selectionReady_.store(true, std::memory_order_release);
        const HRESULT hr = info->SetEventMask(COR_PRF_MONITOR_JIT_COMPILATION | COR_PRF_MONITOR_MODULE_LOADS);
        if (FAILED(hr)) return fail(Outcome::event_mask_failed, hr);
        info_.store(info);
        active_.store(true);
    } catch (...) {
        fail(Outcome::internal_error, E_FAIL);
    }
}

void Capture::leave() noexcept {
    if (inflight_.fetch_sub(1) == 1 && !active_.load())
        if (ICorProfilerInfo9* info = info_.exchange(nullptr)) info->Release();
}

bool Capture::selected(ICorProfilerInfo9* info, ModuleID module, Name& found) {
    WCHAR name[name_capacity + 1]{};
    ULONG units = 0;
    // A base address or assembly output gives CORPROF_E_DATAINCOMPLETE before the module load finishes.
    const HRESULT hr = info->GetModuleInfo2(module, nullptr, name_capacity + 1, &units, name, nullptr, nullptr);
    if (hr == insufficient_buffer) {
        nameTruncations_.fetch_add(1);
        return false;
    }
    if (FAILED(hr) || units == 0 || units > name_capacity + 1) {
        moduleInfoFailures_.fetch_add(1);
        return false;
    }
    const uint32_t length = units - 1;
    uint32_t start = 0;
    for (uint32_t i = 0; i < length; ++i) if (name[i] == u'/') start = i + 1;
    if (length - start != selection_.moduleLength) return false;
    for (uint32_t i = 0; i < selection_.moduleLength; ++i) if (static_cast<char16_t>(name[start + i]) != selection_.module[i]) return false;
    found.length = length;
    for (uint32_t i = 0; i < length; ++i) found.units[i] = static_cast<char16_t>(name[i]);
    return true;
}

void Capture::finished(FunctionID function, HRESULT status, BOOL safeToBlock) noexcept {
    const uint32_t unloads = word_.load() >> 2;
    inflight_.fetch_add(1);
    ICorProfilerInfo9* info = active_.load() ? info_.load() : nullptr;
    if (!info) return leave();
    bool claimed = false;
    try {
        if (!safeToBlock) {
            unsafeCallbacks_.fetch_add(1);
            return leave();
        }
        ClassID cls{};
        ModuleID module{};
        mdToken token{};
        if (FAILED(info->GetFunctionInfo2(function, 0, &cls, &module, &token, 0, nullptr, nullptr))) {
            functionInfoFailures_.fetch_add(1);
            return leave();
        }
        Name name;
        if (token != selection_.token || !selected(info, module, name)) return leave();
        if (!claim()) {
            flags_.fetch_or(second_notification);
            return leave();
        }
        claimed = true;
        storage_.nameReported = name.length;
        storage_.nameCopied = name.length;
        for (uint32_t i = 0; i < name.length; ++i) storage_.name[i] = name.units[i];
        capture(info, function, status, module, token, unloads);
    } catch (...) {
        if (claimed) complete(Outcome::internal_error, E_FAIL, unloads);
    }
    leave();
}

void Capture::capture(ICorProfilerInfo9* info, FunctionID function, HRESULT status, ModuleID module, mdToken token, uint32_t unloads) {
    storage_.functionId = function;
    storage_.moduleId = module;
    storage_.token = token;
    if (FAILED(status)) return complete(Outcome::jit_failed, status, unloads);

    MetadataHolder metadata;
    HRESULT hr = info->GetModuleMetaData(module, ofRead, IID_IMetaDataImport, &metadata.value);
    if (FAILED(hr) || !metadata.value) return complete(Outcome::metadata_failed, FAILED(hr) ? hr : E_POINTER, unloads);
    ULONG nameLength = 0;
    hr = static_cast<IMetaDataImport*>(metadata.value)->GetScopeProps(nullptr, 0, &nameLength, &storage_.mvid);
    if (FAILED(hr)) return complete(Outcome::metadata_failed, hr, unloads);
    if (std::memcmp(&storage_.mvid, &selection_.mvid, sizeof(GUID)) != 0) return complete(Outcome::mvid_mismatch, S_OK, unloads);

    UINT_PTR starts[2]{};
    ULONG32 bodies = 0;
    hr = info->GetNativeCodeStartAddresses(function, 0, 2, &bodies, starts);
    storage_.bodiesReported = bodies;
    if (FAILED(hr)) return complete(Outcome::starts_failed, hr, unloads);
    storage_.bodiesCopied = bodies < 2 ? bodies : 2;
    for (uint32_t i = 0; i < storage_.bodiesCopied; ++i) storage_.starts[i] = starts[i];
    if (bodies == 0) return complete(Outcome::no_native_body, S_OK, unloads);
    if (bodies > 1) return complete(Outcome::multiple_bodies, S_OK, unloads);

    COR_PRF_CODE_INFO regions[2]{};
    ULONG32 regionCount = 0;
    hr = info->GetCodeInfo4(starts[0], 2, &regionCount, regions);
    storage_.regionsReported = regionCount;
    if (FAILED(hr)) return complete(Outcome::code_info_failed, hr, unloads);
    storage_.regionsCopied = regionCount < 2 ? regionCount : 2;
    for (uint32_t i = 0; i < storage_.regionsCopied; ++i) {
        storage_.regionStart[i] = regions[i].startAddress;
        storage_.regionSize[i] = regions[i].size;
    }
    if (regionCount == 0) return complete(Outcome::no_code_region, S_OK, unloads);
    if (regionCount > 2) return complete(Outcome::code_info_unexpected_count, S_OK, unloads);

    ULONG32 count = 0;
    hr = info->GetILToNativeMapping3(starts[0], 0, &count, nullptr);
    storage_.entriesReported = count;
    if (FAILED(hr)) return complete(Outcome::map_failed, hr, unloads);
    if (count == 0) return complete(Outcome::map_empty, S_OK, unloads);
    if (count > entry_capacity) return complete(Outcome::map_too_large, S_OK, unloads);
    ULONG32 second = 0;
    hr = info->GetILToNativeMapping3(starts[0], count, &second, scratch_);
    if (FAILED(hr)) return complete(Outcome::map_failed, hr, unloads);
    storage_.entriesReported = second;
    storage_.entriesCopied = second < count ? second : count;
    for (uint32_t i = 0; i < storage_.entriesCopied; ++i) {
        storage_.il[i] = scratch_[i].ilOffset;
        storage_.nativeStart[i] = scratch_[i].nativeStartOffset;
        storage_.nativeEnd[i] = scratch_[i].nativeEndOffset;
    }
    if (second != count) {
        storage_.detail = count;
        return complete(Outcome::map_count_changed, S_OK, unloads);
    }
    complete(Outcome::captured, S_OK, unloads);
}

void Capture::unloadStarted(ModuleID module) noexcept {
    const uint32_t state = word_.fetch_add(4u) & 3u;
    if (state == static_cast<uint32_t>(State::capturing)) flags_.fetch_or(unload_during_capture);
    else if (state == static_cast<uint32_t>(State::complete) && module != 0 && storage_.moduleId == module) flags_.fetch_or(unloaded_after_capture);
}

void Capture::shutdown() noexcept {
    flags_.fetch_or(closed);
    active_.store(false);
    if (inflight_.load() == 0)
        if (ICorProfilerInfo9* info = info_.exchange(nullptr)) info->Release();
}

ReadResult Capture::read(uint8_t* buffer, uint32_t capacity, uint32_t* required) const {
    if (!required) return ReadResult::invalid_argument;
    uint32_t state, flags;
    for (;;) {
        const uint32_t first = word_.load(std::memory_order_acquire) & 3u;
        flags = flags_.load();
        state = word_.load(std::memory_order_acquire) & 3u;
        if (state == first) break;
    }
    if (state == static_cast<uint32_t>(State::capturing)) {
        *required = 0;
        return ReadResult::not_ready;
    }
    const bool done = state == static_cast<uint32_t>(State::complete);
    const uint32_t entries = done ? storage_.entriesCopied : 0;
    const uint32_t total = header_size + entries * entry_size;
    *required = total;
    if (!buffer || capacity < total) return ReadResult::buffer_too_small;
    std::memset(buffer, 0, total);
    put32(buffer + 0, magic);
    put16(buffer + 4, version);
    put16(buffer + 6, static_cast<uint16_t>(header_size));
    put32(buffer + 8, total);
    put32(buffer + 12, state);
    put32(buffer + 24, flags);
    if (selectionReady_.load(std::memory_order_acquire)) putGuid(buffer + 64, selection_.mvid);
    put32(buffer + 108, entry_capacity);
    put32(buffer + 160, functionInfoFailures_.load());
    put32(buffer + 164, moduleInfoFailures_.load());
    put32(buffer + 168, nameTruncations_.load());
    put32(buffer + 172, unsafeCallbacks_.load());
    if (!done) return ReadResult::ok;
    const Storage& s = storage_;
    put32(buffer + 16, s.outcome);
    put32(buffer + 20, static_cast<uint32_t>(s.hr));
    put32(buffer + 28, s.detail);
    put64(buffer + 32, s.functionId);
    put64(buffer + 40, s.moduleId);
    putGuid(buffer + 48, s.mvid);
    put32(buffer + 80, s.token);
    put32(buffer + 84, s.bodiesReported);
    put32(buffer + 88, s.bodiesCopied);
    put32(buffer + 92, s.regionsReported);
    put32(buffer + 96, s.regionsCopied);
    put32(buffer + 100, s.entriesReported);
    put32(buffer + 104, s.entriesCopied);
    for (int i = 0; i < 2; ++i) {
        put64(buffer + 112 + 8 * i, s.starts[i]);
        put64(buffer + 128 + 16 * i, s.regionStart[i]);
        put64(buffer + 136 + 16 * i, s.regionSize[i]);
    }
    put32(buffer + 176, s.nameReported);
    put32(buffer + 180, s.nameCopied);
    for (uint32_t i = 0; i < s.nameCopied; ++i) put16(buffer + 184 + 2 * i, static_cast<uint16_t>(s.name[i]));
    for (uint32_t i = 0; i < entries; ++i) {
        put32(buffer + header_size + entry_size * i, s.il[i]);
        put32(buffer + header_size + entry_size * i + 4, s.nativeStart[i]);
        put32(buffer + header_size + entry_size * i + 8, s.nativeEnd[i]);
    }
    return ReadResult::ok;
}

HRESULT STDMETHODCALLTYPE Profiler::QueryInterface(REFIID riid, void** object) {
    if (!object) return E_POINTER;
    *object = nullptr;
    if (riid == IID_IUnknown || riid == IID_ICorProfilerCallback || riid == IID_ICorProfilerCallback2) {
        *object = static_cast<ICorProfilerCallback2*>(this);
        AddRef();
        return S_OK;
    }
    return E_NOINTERFACE;
}

ULONG STDMETHODCALLTYPE Profiler::AddRef() { return references_.fetch_add(1) + 1; }

ULONG STDMETHODCALLTYPE Profiler::Release() {
    const ULONG count = references_.fetch_sub(1) - 1;
    if (count == 0) delete this;
    return count;
}

HRESULT STDMETHODCALLTYPE Profiler::Initialize(IUnknown* info) {
    capture().initialize(info);
    return S_OK;
}

HRESULT STDMETHODCALLTYPE Profiler::Shutdown() {
    capture().shutdown();
    return S_OK;
}

HRESULT STDMETHODCALLTYPE Profiler::JITCompilationFinished(FunctionID function, HRESULT status, BOOL safeToBlock) {
    capture().finished(function, status, safeToBlock);
    return S_OK;
}

HRESULT STDMETHODCALLTYPE Profiler::ModuleUnloadStarted(ModuleID module) {
    capture().unloadStarted(module);
    return S_OK;
}

HRESULT STDMETHODCALLTYPE Profiler::JITInlining(FunctionID, FunctionID, BOOL* shouldInline) {
    if (shouldInline) *shouldInline = TRUE;
    return S_OK;
}

HRESULT STDMETHODCALLTYPE Profiler::JITCachedFunctionSearchStarted(FunctionID, BOOL* useCached) {
    if (useCached) *useCached = TRUE;
    return S_OK;
}

HRESULT STDMETHODCALLTYPE Factory::QueryInterface(REFIID riid, void** object) {
    if (!object) return E_POINTER;
    *object = nullptr;
    if (riid == IID_IUnknown || riid == IID_IClassFactory) {
        *object = static_cast<IClassFactory*>(this);
        return S_OK;
    }
    return E_NOINTERFACE;
}

ULONG STDMETHODCALLTYPE Factory::AddRef() { return 2; }
ULONG STDMETHODCALLTYPE Factory::Release() { return 1; }

HRESULT STDMETHODCALLTYPE Factory::CreateInstance(IUnknown* outer, REFIID riid, void** object) {
    if (!object) return E_POINTER;
    *object = nullptr;
    if (outer) return CLASS_E_NOAGGREGATION;
    Profiler* profiler = new (std::nothrow) Profiler();
    if (!profiler) return E_OUTOFMEMORY;
    const HRESULT hr = profiler->QueryInterface(riid, object);
    profiler->Release();
    return hr;
}

HRESULT STDMETHODCALLTYPE Factory::LockServer(BOOL) { return S_OK; }
}

extern "C" COZ_MAP_EXPORT HRESULT STDMETHODCALLTYPE DllGetClassObject(REFCLSID rclsid, REFIID riid, void** object) {
    static coz::map::Factory factory;
    if (!object) return E_POINTER;
    *object = nullptr;
    if (rclsid != coz::map::clsid) return CLASS_E_CLASSNOTAVAILABLE;
    return factory.QueryInterface(riid, object);
}

extern "C" COZ_MAP_EXPORT int32_t coz_map_read(uint8_t* buffer, uint32_t capacity, uint32_t* required) {
    try { return static_cast<int32_t>(coz::map::capture().read(buffer, capacity, required)); } catch (...) { return static_cast<int32_t>(coz::map::ReadResult::invalid_argument); }
}
