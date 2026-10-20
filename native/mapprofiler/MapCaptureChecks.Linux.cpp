#include <atomic>
#include <chrono>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <stdexcept>
#include <string>
#include <thread>
#include <vector>
#include "MapProfiler.h"
#include "FakeInfoBase.h"
#include "FakeImportBase.h"

using namespace coz::map;

namespace {
int passed = 0, failed = 0;

void check(bool condition, const char* name) {
    if (condition) ++passed;
    else {
        ++failed;
        std::fprintf(stderr, "FAIL: %s\n", name);
    }
}

const GUID mvid = {0x11223344, 0x5566, 0x7788, {0x99, 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF, 0x01}};
constexpr mdToken selectedToken = 0x06000012;
constexpr HRESULT insufficient = static_cast<HRESULT>(0x8007007Au);

class FakeImport final : public FakeImportBase {
public:
    std::atomic<ULONG> references{1};
    HRESULT scopeHr = S_OK;
    GUID scope = mvid;
    STDMETHODIMP QueryInterface(REFIID, void** object) override { *object = nullptr; return E_NOINTERFACE; }
    STDMETHODIMP_(ULONG) AddRef() override { return references.fetch_add(1) + 1; }
    STDMETHODIMP_(ULONG) Release() override { return references.fetch_sub(1) - 1; }
    STDMETHODIMP GetScopeProps(LPWSTR, ULONG, ULONG* length, GUID* id) override {
        if (FAILED(scopeHr)) return scopeHr;
        if (length) *length = 0;
        if (id) *id = scope;
        return S_OK;
    }
};

class FakeInfo final : public FakeInfoBase {
public:
    std::atomic<ULONG> references{1};
    bool info9 = true;
    HRESULT eventHr = S_OK;
    DWORD mask = 0;
    HRESULT functionHr = S_OK;
    mdToken token = selectedToken;
    ModuleID module = 0x5000;
    HRESULT moduleHr = S_OK;
    std::u16string name = u"/app/CozNet.TargetLocal.dll";
    bool unloadInModuleInfo = false;
    bool optionalOutputsNull = true;
    bool throwInQuery = false, throwInModuleInfo = false, throwInStarts = false;
    HRESULT metadataHr = S_OK;
    FakeImport import;
    HRESULT startsHr = S_OK;
    std::vector<UINT_PTR> starts{0x7000};
    HRESULT codeHr = S_OK;
    std::vector<COR_PRF_CODE_INFO> regions{{0x7000, 0x40}};
    ULONG32 regionsReportedOverride = 0;
    HRESULT mapHr = S_OK, secondMapHr = S_OK;
    std::vector<COR_DEBUG_IL_TO_NATIVE_MAP> map{{static_cast<ULONG32>(-2), 0, 8}, {0, 8, 20}, {3, 20, 20}, {7, 24, 40}, {static_cast<ULONG32>(-1), 40, 48}, {static_cast<ULONG32>(-3), 48, 64}};
    ULONG32 firstCountOverride = 0, secondCountOverride = 0;
    std::atomic<bool> blockMap{false}, inMap{false}, releaseMap{false};

    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID riid, void** object) override {
        *object = nullptr;
        if (throwInQuery) throw std::runtime_error("query");
        if (info9 && riid == IID_ICorProfilerInfo9) {
            *object = static_cast<ICorProfilerInfo9*>(this);
            AddRef();
            return S_OK;
        }
        return E_NOINTERFACE;
    }
    ULONG STDMETHODCALLTYPE AddRef() override { return references.fetch_add(1) + 1; }
    ULONG STDMETHODCALLTYPE Release() override { return references.fetch_sub(1) - 1; }
    HRESULT STDMETHODCALLTYPE SetEventMask(DWORD events) override { mask = events; return eventHr; }
    HRESULT STDMETHODCALLTYPE GetFunctionInfo2(FunctionID, COR_PRF_FRAME_INFO, ClassID* cls, ModuleID* moduleId, mdToken* tokenOut, ULONG32, ULONG32*, ClassID*) override {
        if (FAILED(functionHr)) return functionHr;
        *cls = 0;
        *moduleId = module;
        *tokenOut = token;
        return S_OK;
    }
    // Follows ProfToEEInterfaceImpl::GetModuleInfo2 at the pinned runtime revision.
    HRESULT STDMETHODCALLTYPE GetModuleInfo2(ModuleID, LPCBYTE* base, ULONG capacity, ULONG* units, WCHAR text[], AssemblyID* assembly, DWORD* flags) override {
        if (unloadInModuleInfo) capture().unloadStarted(0x9999);
        if (throwInModuleInfo) throw std::runtime_error("module info");
        if (base || assembly || flags) optionalOutputsNull = false;
        if (FAILED(moduleHr)) return moduleHr;
        if (text) text[0] = 0;
        const ULONG length = static_cast<ULONG>(name.size() + 1);
        HRESULT hr = S_OK;
        if (text && capacity > 0) {
            if (capacity < length) hr = insufficient;
            else {
                for (size_t i = 0; i < name.size(); ++i) text[i] = static_cast<WCHAR>(name[i]);
                text[name.size()] = 0;
            }
        }
        if (units) *units = length;
        return hr;
    }
    HRESULT STDMETHODCALLTYPE GetModuleMetaData(ModuleID, DWORD, REFIID riid, IUnknown** out) override {
        *out = nullptr;
        if (FAILED(metadataHr)) return metadataHr;
        if (!(riid == IID_IMetaDataImport)) return E_NOINTERFACE;
        import.AddRef();
        *out = &import;
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE GetNativeCodeStartAddresses(FunctionID, ReJITID, ULONG32 capacity, ULONG32* count, UINT_PTR out[]) override {
        if (throwInStarts) throw std::runtime_error("starts");
        *count = static_cast<ULONG32>(starts.size());
        if (FAILED(startsHr)) return startsHr;
        if (capacity < starts.size()) return insufficient;
        for (size_t i = 0; i < starts.size(); ++i) out[i] = starts[i];
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE GetCodeInfo4(UINT_PTR, ULONG32 capacity, ULONG32* count, COR_PRF_CODE_INFO out[]) override {
        if (FAILED(codeHr)) return codeHr;
        *count = regionsReportedOverride ? regionsReportedOverride : static_cast<ULONG32>(regions.size());
        for (size_t i = 0; i < capacity && i < regions.size(); ++i) out[i] = regions[i];
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE GetILToNativeMapping3(UINT_PTR, ULONG32 capacity, ULONG32* count, COR_DEBUG_IL_TO_NATIVE_MAP out[]) override {
        if (capacity == 0) {
            *count = firstCountOverride ? firstCountOverride : static_cast<ULONG32>(map.size());
            return mapHr;
        }
        if (blockMap.load()) {
            inMap.store(true);
            while (!releaseMap.load()) std::this_thread::sleep_for(std::chrono::milliseconds(1));
        }
        if (FAILED(secondMapHr)) return secondMapHr;
        *count = secondCountOverride ? secondCountOverride : static_cast<ULONG32>(map.size());
        for (size_t i = 0; i < capacity && i < map.size(); ++i) out[i] = map[i];
        return S_OK;
    }
};

uint32_t u32(const std::vector<uint8_t>& b, size_t o) { return b[o] | b[o + 1] << 8 | b[o + 2] << 16 | static_cast<uint32_t>(b[o + 3]) << 24; }
uint64_t u64(const std::vector<uint8_t>& b, size_t o) { return u32(b, o) | static_cast<uint64_t>(u32(b, o + 4)) << 32; }

std::vector<uint8_t> read() {
    uint32_t required = 0;
    int32_t result = coz_map_read(nullptr, 0, &required);
    if (result != static_cast<int32_t>(ReadResult::buffer_too_small)) return {};
    std::vector<uint8_t> bytes(required);
    result = coz_map_read(bytes.data(), required, &required);
    return result == 0 ? bytes : std::vector<uint8_t>{};
}

void select() {
    setenv("COZNET_MAP_MODULE", "CozNet.TargetLocal.dll", 1);
    setenv("COZNET_MAP_TOKEN", "0x06000012", 1);
    setenv("COZNET_MAP_MVID", "11223344-5566-7788-99AA-BBCCDDEEFF01", 1);
}

Profiler* start(FakeInfo& fake) {
    capture().reset();
    select();
    Profiler* profiler = nullptr;
    Factory factory;
    factory.CreateInstance(nullptr, IID_ICorProfilerCallback2, reinterpret_cast<void**>(&profiler));
    profiler->Initialize(&fake);
    return profiler;
}

std::vector<uint8_t> run(FakeInfo& fake) {
    Profiler* profiler = start(fake);
    profiler->JITCompilationFinished(0xF00, S_OK, TRUE);
    std::vector<uint8_t> bytes = read();
    profiler->Shutdown();
    profiler->Release();
    return bytes;
}

uint32_t outcomeOf(const std::vector<uint8_t>& b) { return b.size() >= header_size ? u32(b, 16) : 0xFFFFFFFFu; }
int32_t hrOf(const std::vector<uint8_t>& b) { return static_cast<int32_t>(u32(b, 20)); }

void write(const char* directory, const char* name, const std::vector<uint8_t>& bytes) {
    std::string path = std::string(directory) + "/" + name;
    FILE* file = std::fopen(path.c_str(), "wb");
    if (!file) { check(false, path.c_str()); return; }
    std::fwrite(bytes.data(), 1, bytes.size(), file);
    std::fclose(file);
}

void comChecks() {
    Factory factory;
    void* object = reinterpret_cast<void*>(1);
    FakeInfo outer;
    check(factory.CreateInstance(&outer, IID_ICorProfilerCallback2, &object) == CLASS_E_NOAGGREGATION && object == nullptr, "aggregation is rejected");
    ICorProfilerCallback2* callback = nullptr;
    check(factory.CreateInstance(nullptr, IID_ICorProfilerCallback2, reinterpret_cast<void**>(&callback)) == S_OK && callback, "the factory creates a callback");
    void* other = reinterpret_cast<void*>(1);
    check(callback->QueryInterface(IID_ICorProfilerCallback3, &other) == E_NOINTERFACE && other == nullptr, "a later callback version is refused");
    other = reinterpret_cast<void*>(1);
    check(callback->QueryInterface(IID_ICorProfilerInfo9, &other) == E_NOINTERFACE && other == nullptr, "the info interface is not a callback interface");
    IUnknown* unknown = nullptr;
    check(callback->QueryInterface(IID_IUnknown, reinterpret_cast<void**>(&unknown)) == S_OK && unknown, "IUnknown is answered");
    check(callback->AddRef() == 3 && callback->Release() == 2 && unknown->Release() == 1, "reference counts are exact");
    BOOL inline_ = FALSE, cached = FALSE;
    check(callback->JITInlining(1, 2, &inline_) == S_OK && inline_ == TRUE, "JITInlining keeps the inline decision");
    check(callback->JITCachedFunctionSearchStarted(1, &cached) == S_OK && cached == TRUE, "JITCachedFunctionSearchStarted sets its output");
    callback->Release();
    void* found = reinterpret_cast<void*>(1);
    check(DllGetClassObject(IID_IUnknown, IID_IClassFactory, &found) == CLASS_E_CLASSNOTAVAILABLE && found == nullptr, "another CLSID is not available");
    check(DllGetClassObject(clsid, IID_IClassFactory, &found) == S_OK && found, "the CLSID gives the factory");
}

void initializeChecks(const char* fixtures) {
    capture().reset();
    Profiler profiler;
    profiler.Initialize(nullptr);
    std::vector<uint8_t> b = read();
    check(u32(b, 12) == 2 && outcomeOf(b) == static_cast<uint32_t>(Outcome::info_unavailable) && hrOf(b) == E_POINTER, "a null info gives InfoUnavailable");

    FakeInfo none;
    none.info9 = false;
    b = run(none);
    check(outcomeOf(b) == static_cast<uint32_t>(Outcome::info_unavailable) && hrOf(b) == E_NOINTERFACE && none.references == 1, "no Info9 gives InfoUnavailable");
    write(fixtures, "info-unavailable.bin", b);

    FakeInfo throwing;
    throwing.throwInQuery = true;
    b = run(throwing);
    check(outcomeOf(b) == static_cast<uint32_t>(Outcome::internal_error) && hrOf(b) == E_FAIL && throwing.references == 1, "an exception in Initialize gives InternalError");
    write(fixtures, "initialize-internal-error.bin", b);

    FakeInfo bad;
    capture().reset();
    unsetenv("COZNET_MAP_MVID");
    Profiler second;
    second.Initialize(&bad);
    b = read();
    check(outcomeOf(b) == static_cast<uint32_t>(Outcome::selection_invalid) && bad.references == 1, "a missing selection releases the info");
    write(fixtures, "selection-invalid.bin", b);

    const char* malformed[][3] = {
        {"", "0x06000012", "11223344-5566-7788-99AA-BBCCDDEEFF01"},
        {"app/CozNet.TargetLocal.dll", "0x06000012", "11223344-5566-7788-99AA-BBCCDDEEFF01"},
        {"CozNet.TargetLocal.dll", "0x02000012", "11223344-5566-7788-99AA-BBCCDDEEFF01"},
        {"CozNet.TargetLocal.dll", "0x06000000", "11223344-5566-7788-99AA-BBCCDDEEFF01"},
        {"CozNet.TargetLocal.dll", "06000012", "11223344-5566-7788-99AA-BBCCDDEEFF01"},
        {"CozNet.TargetLocal.dll", "0x0600001G", "11223344-5566-7788-99AA-BBCCDDEEFF01"},
        {"CozNet.TargetLocal.dll", "0x06000012", "11223344-5566-7788-99AA-BBCCDDEEFF0"},
        {"CozNet.TargetLocal.dll", "0x06000012", "11223344+5566-7788-99AA-BBCCDDEEFF01"},
        {"CozNet.TargetLocal.dll", "0x06000012", "1122334X-5566-7788-99AA-BBCCDDEEFF01"},
    };
    for (const auto& values : malformed) {
        FakeInfo f;
        capture().reset();
        setenv("COZNET_MAP_MODULE", values[0], 1);
        setenv("COZNET_MAP_TOKEN", values[1], 1);
        setenv("COZNET_MAP_MVID", values[2], 1);
        Profiler p;
        p.Initialize(&f);
        b = read();
        check(outcomeOf(b) == static_cast<uint32_t>(Outcome::selection_invalid) && hrOf(b) == E_INVALIDARG && f.references == 1 && f.mask == 0,
            "a malformed selection gives SelectionInvalid and asks for no events");
    }
    const std::string longName(name_capacity + 1, 'a');
    FakeInfo tooLong;
    capture().reset();
    select();
    setenv("COZNET_MAP_MODULE", longName.c_str(), 1);
    Profiler longProfiler;
    longProfiler.Initialize(&tooLong);
    check(outcomeOf(read()) == static_cast<uint32_t>(Outcome::selection_invalid), "a module name above 260 units is SelectionInvalid");

    FakeInfo mask;
    mask.eventHr = E_FAIL;
    b = run(mask);
    check(outcomeOf(b) == static_cast<uint32_t>(Outcome::event_mask_failed) && hrOf(b) == E_FAIL && mask.references == 1, "a mask failure keeps its HRESULT and releases the info");
    write(fixtures, "event-mask-failed.bin", b);

    FakeInfo good;
    Profiler* p = start(good);
    check(good.mask == (COR_PRF_MONITOR_JIT_COMPILATION | COR_PRF_MONITOR_MODULE_LOADS), "the event mask has only the two flags");
    b = read();
    check(u32(b, 12) == 0 && b.size() == header_size, "before a selected method compiles, the state is NotSeen");
    write(fixtures, "not-seen.bin", b);
    p->Shutdown();
    check(good.references == 1, "shutdown releases the info");
    p->Release();
}

void captureChecks(const char* fixtures) {
    FakeInfo fake;
    std::vector<uint8_t> b = run(fake);
    check(outcomeOf(b) == static_cast<uint32_t>(Outcome::captured) && hrOf(b) == S_OK, "a clean capture is Captured");
    check(u32(b, 0) == magic && u32(b, 4) == (version | header_size << 16) && u32(b, 8) == header_size + 6 * entry_size, "the header has its magic, version, sizes");
    check(u64(b, 32) == 0xF00 && u64(b, 40) == 0x5000 && u32(b, 80) == selectedToken, "the identity is kept");
    check(std::memcmp(b.data() + 48, b.data() + 64, 16) == 0 && u32(b, 48) == 0x11223344, "the MVID is kept in GUID byte order");
    check(u32(b, 84) == 1 && u32(b, 88) == 1 && u64(b, 112) == 0x7000 && u32(b, 92) == 1 && u64(b, 128) == 0x7000 && u64(b, 136) == 0x40, "the body and region are kept");
    check(u32(b, 100) == 6 && u32(b, 104) == 6, "the map counts are kept");
    const uint32_t raw[18] = {0xFFFFFFFEu, 0, 8, 0, 8, 20, 3, 20, 20, 7, 24, 40, 0xFFFFFFFFu, 40, 48, 0xFFFFFFFDu, 48, 64};
    bool same = true;
    for (int i = 0; i < 18; ++i) same = same && u32(b, header_size + 4 * i) == raw[i];
    check(same, "special values, a zero-length entry, and a gap are kept raw");
    const std::u16string path = u"/app/CozNet.TargetLocal.dll";
    bool name = u32(b, 176) == path.size() && u32(b, 180) == path.size();
    for (size_t i = 0; i < path.size(); ++i) name = name && (b[184 + 2 * i] | b[185 + 2 * i] << 8) == path[i];
    check(name && b[184 + 2 * path.size()] == 0, "the module name is kept");
    check(fake.import.references == 1, "the metadata pointer is released");
    write(fixtures, "captured.bin", b);
    check(fake.optionalOutputsNull, "the module query asks for no base address, assembly, or flags");
    const std::vector<uint8_t> closedRead = read();
    check(closedRead.size() == b.size() && u32(closedRead, 24) == closed && u32(closedRead, 16) == static_cast<uint32_t>(Outcome::captured), "a read after shutdown keeps the outcome and sets Closed");

    struct Case { const char* name; void (*setup)(FakeInfo&); Outcome outcome; HRESULT hr; };
    const Case cases[] = {
        {"jit failed", [](FakeInfo&) {}, Outcome::jit_failed, E_FAIL},
        {"metadata failed", [](FakeInfo& f) { f.metadataHr = E_ACCESSDENIED; }, Outcome::metadata_failed, E_ACCESSDENIED},
        {"scope failed", [](FakeInfo& f) { f.import.scopeHr = E_UNEXPECTED; }, Outcome::metadata_failed, E_UNEXPECTED},
        {"mvid mismatch", [](FakeInfo& f) { f.import.scope.Data1 = 1; }, Outcome::mvid_mismatch, S_OK},
        {"starts failed", [](FakeInfo& f) { f.startsHr = E_OUTOFMEMORY; }, Outcome::starts_failed, E_OUTOFMEMORY},
        {"insufficient starts", [](FakeInfo& f) { f.starts = {1, 2, 3}; }, Outcome::starts_failed, insufficient},
        {"no body", [](FakeInfo& f) { f.starts.clear(); }, Outcome::no_native_body, S_OK},
        {"two bodies", [](FakeInfo& f) { f.starts = {0x7000, 0x8000}; }, Outcome::multiple_bodies, S_OK},
        {"code info failed", [](FakeInfo& f) { f.codeHr = E_INVALIDARG; }, Outcome::code_info_failed, E_INVALIDARG},
        {"no region", [](FakeInfo& f) { f.regions.clear(); }, Outcome::no_code_region, S_OK},
        {"three regions", [](FakeInfo& f) { f.regionsReportedOverride = 3; }, Outcome::code_info_unexpected_count, S_OK},
        {"map failed", [](FakeInfo& f) { f.mapHr = static_cast<HRESULT>(0x80131376u); }, Outcome::map_failed, static_cast<HRESULT>(0x80131376u)},
        {"second map failed", [](FakeInfo& f) { f.secondMapHr = E_FAIL; }, Outcome::map_failed, E_FAIL},
        {"map empty", [](FakeInfo& f) { f.map.clear(); }, Outcome::map_empty, S_OK},
        {"map too large", [](FakeInfo& f) { f.firstCountOverride = entry_capacity + 1; }, Outcome::map_too_large, S_OK},
        {"map count changed", [](FakeInfo& f) { f.secondCountOverride = 5; }, Outcome::map_count_changed, S_OK},
    };
    for (const Case& c : cases) {
        FakeInfo f;
        c.setup(f);
        Profiler* p = start(f);
        p->JITCompilationFinished(0xF00, std::strcmp(c.name, "jit failed") == 0 ? E_FAIL : S_OK, TRUE);
        std::vector<uint8_t> r = read();
        p->Shutdown();
        p->Release();
        check(outcomeOf(r) == static_cast<uint32_t>(c.outcome) && hrOf(r) == c.hr && f.import.references == 1 && f.references == 1, c.name);
        std::string file(c.name);
        for (char& ch : file) if (ch == ' ') ch = '-';
        if (c.outcome != Outcome::map_count_changed) write(fixtures, (file + ".bin").c_str(), r);
        if (c.outcome == Outcome::starts_failed && c.hr == insufficient) check(u32(r, 84) == 3 && u32(r, 88) == 0, "the reported body count is kept, and no start is copied");
        if (c.outcome == Outcome::multiple_bodies) check(u32(r, 88) == 2 && u64(r, 120) == 0x8000, "both bodies are kept");
        if (c.outcome == Outcome::map_too_large) check(u32(r, 100) == entry_capacity + 1 && u32(r, 104) == 0, "a large map is not copied");
        if (c.outcome == Outcome::map_count_changed) {
            check(u32(r, 28) == 6 && u32(r, 100) == 5 && u32(r, 104) == 5, "both map counts are kept");
            write(fixtures, "map-count-changed.bin", r);
        }
    }

    FakeInfo thrown;
    thrown.throwInStarts = true;
    b = run(thrown);
    check(outcomeOf(b) == static_cast<uint32_t>(Outcome::internal_error) && hrOf(b) == E_FAIL && thrown.import.references == 1 && thrown.references == 1,
        "an exception after the claim gives InternalError and releases every pointer");
    write(fixtures, "internal-error.bin", b);

    FakeInfo unclaimed;
    unclaimed.throwInModuleInfo = true;
    b = run(unclaimed);
    check(u32(b, 12) == 0 && u32(b, 16) == 0 && unclaimed.references == 1, "an exception before the claim leaves the record NotSeen");

    FakeInfo cold;
    cold.regions = {{0x7000, 0x30}, {0x9000, 0x10}};
    b = run(cold);
    check(outcomeOf(b) == static_cast<uint32_t>(Outcome::captured) && u32(b, 92) == 2 && u32(b, 96) == 2 && u64(b, 144) == 0x9000, "a cold region is kept raw");

    FakeInfo counters;
    Profiler* p = start(counters);
    p->JITCompilationFinished(0xF00, S_OK, FALSE);
    counters.functionHr = E_FAIL;
    p->JITCompilationFinished(0xF00, S_OK, TRUE);
    counters.functionHr = S_OK;
    counters.moduleHr = E_FAIL;
    p->JITCompilationFinished(0xF00, S_OK, TRUE);
    counters.moduleHr = S_OK;
    counters.name = u"/app/" + std::u16string(name_capacity, u'a') + u".dll";
    p->JITCompilationFinished(0xF00, S_OK, TRUE);
    counters.name = u"/app/CozNet.TargetLocal.dll";
    counters.token = 0x06000013;
    p->JITCompilationFinished(0xF00, S_OK, TRUE);
    counters.token = selectedToken;
    counters.name = u"/app/Other.dll";
    p->JITCompilationFinished(0xF00, S_OK, TRUE);
    b = read();
    check(u32(b, 12) == 0 && u32(b, 160) == 1 && u32(b, 164) == 1 && u32(b, 168) == 1 && u32(b, 172) == 1 && u32(b, 24) == 0,
        "unselected and failed notifications only count, and set no flag");
    p->Shutdown();
    p->Release();
}

void concurrencyChecks(const char* fixtures) {
    FakeInfo fake;
    fake.blockMap = true;
    Profiler* p = start(fake);
    std::thread claimant([&] { p->JITCompilationFinished(0xF00, S_OK, TRUE); });
    while (!fake.inMap.load()) std::this_thread::sleep_for(std::chrono::milliseconds(1));
    uint32_t required = 1;
    std::vector<uint8_t> probe(header_size, 0xAA);
    check(coz_map_read(probe.data(), header_size, &required) == static_cast<int32_t>(ReadResult::not_ready) && required == 0 && probe[0] == 0xAA, "a read during capture copies nothing");
    std::thread second([&] { p->JITCompilationFinished(0xF01, S_OK, TRUE); });
    second.join();
    p->ModuleUnloadStarted(0x1234);
    p->Shutdown();
    check(fake.references == 2, "shutdown during capture does not release the info");
    fake.releaseMap = true;
    claimant.join();
    check(fake.references == 1, "the last callback releases the info after shutdown");
    std::vector<uint8_t> b = read();
    check(outcomeOf(b) == static_cast<uint32_t>(Outcome::captured) && u32(b, 24) == (second_notification | unload_during_capture | closed),
        "flags during capture keep the primary outcome");
    write(fixtures, "flags.bin", b);
    check(read() == b, "repeated reads are identical");
    p->Release();

    FakeInfo after;
    p = start(after);
    p->JITCompilationFinished(0xF00, S_OK, TRUE);
    p->JITCompilationFinished(0xF00, S_OK, TRUE);
    p->ModuleUnloadStarted(0x4444);
    b = read();
    check(u32(b, 24) == second_notification, "an unload of another module after capture sets no unload flag");
    p->ModuleUnloadStarted(0x5000);
    b = read();
    check(outcomeOf(b) == static_cast<uint32_t>(Outcome::captured) && u32(b, 24) == (second_notification | unloaded_after_capture), "a second notification and an unload after capture are flags");
    p->Shutdown();
    p->Release();

    FakeInfo early;
    early.unloadInModuleInfo = true;
    b = run(early);
    check(u32(b, 24) == unload_during_capture, "an unload before the claim is reported as an overlap");

    FakeInfo late;
    late.blockMap = true;
    p = start(late);
    std::thread claimant2([&] { p->JITCompilationFinished(0xF00, S_OK, TRUE); });
    while (!late.inMap.load()) std::this_thread::sleep_for(std::chrono::milliseconds(1));
    p->ModuleUnloadStarted(0x4444);
    late.releaseMap = true;
    claimant2.join();
    b = read();
    check(u32(b, 24) == unload_during_capture, "an unload of any module during capture sets UnloadDuringCapture");
    p->Shutdown();
    p->Release();

    std::vector<uint8_t> small(4, 0xAA);
    uint32_t need = 0;
    check(coz_map_read(small.data(), 4, &need) == static_cast<int32_t>(ReadResult::buffer_too_small) && need == b.size() && small[0] == 0xAA, "a small buffer gets the required size and no bytes");
    check(coz_map_read(small.data(), 4, nullptr) == static_cast<int32_t>(ReadResult::invalid_argument), "a null size pointer is an invalid argument");
}
}

int seededRace() {
    static int shared = 0;
    std::thread first([] { for (int i = 0; i < 100000; ++i) ++shared; });
    std::thread second([] { for (int i = 0; i < 100000; ++i) ++shared; });
    first.join();
    second.join();
    return shared > 0 ? 0 : 1;
}

int main(int argc, char** argv) {
    if (argc > 1 && std::strcmp(argv[1], "--seeded-race") == 0) return seededRace();
    const char* fixtures = argc > 1 ? argv[1] : ".";
    comChecks();
    initializeChecks(fixtures);
    captureChecks(fixtures);
    concurrencyChecks(fixtures);
    std::printf("map capture checks: %d passed, %d failed\n", passed, failed);
    return failed == 0 && passed > 0 ? 0 : 1;
}
