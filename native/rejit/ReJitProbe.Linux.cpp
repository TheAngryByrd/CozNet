// Test-only CLR profiler that requests a ReJIT of one method. Vtable slots follow corprof.idl in dotnet/runtime v10.0.0.
#include <atomic>
#include <cstdint>
#include <cstring>
#include <thread>

namespace {
using HRESULT = int32_t;
constexpr HRESULT ok = 0;
constexpr HRESULT no_interface = int32_t(0x80004002u);
constexpr HRESULT not_ready = int32_t(0x80004005u);
constexpr uint32_t enable_rejit = 0x00040000u;
// ReJITCompilationFinished needs the JIT compilation monitor flag.
constexpr uint32_t monitor_jit = 0x00000020u;

struct Guid {
    uint32_t data1;
    uint16_t data2, data3;
    uint8_t data4[8];
};

constexpr Guid iid_unknown{0x00000000, 0x0000, 0x0000, {0xC0, 0, 0, 0, 0, 0, 0, 0x46}};
constexpr Guid iid_class_factory{0x00000001, 0x0000, 0x0000, {0xC0, 0, 0, 0, 0, 0, 0, 0x46}};
constexpr Guid iid_callbacks[] = {
    {0x176FBED1, 0xA55C, 0x4796, {0x98, 0xCA, 0xA9, 0xDA, 0x0E, 0xF8, 0x83, 0xE7}},
    {0x8A8CC829, 0xCCF2, 0x49fe, {0xBB, 0xAE, 0x0F, 0x02, 0x22, 0x28, 0x07, 0x1A}},
    {0x4FD2ED52, 0x7731, 0x4b8d, {0x94, 0x69, 0x03, 0xD2, 0xCC, 0x30, 0x86, 0xC5}},
    {0x7B63B2E3, 0x107D, 0x4d48, {0xB2, 0xF6, 0xF6, 0x1E, 0x22, 0x94, 0x70, 0xD2}},
};
constexpr Guid iid_info4{0x0d8fdcaa, 0x6257, 0x47bf, {0xb1, 0xbf, 0x94, 0xda, 0xc8, 0x84, 0x66, 0xee}};

constexpr int callback_slots = 89;
constexpr int slot_initialize = 3;
constexpr int slot_cached_search = 25;
constexpr int slot_inlining = 28;
constexpr int slot_rejit_finished = 85;
constexpr int slot_rejit_error = 86;
constexpr int slot_get_function_info = 15;
constexpr int slot_set_event_mask = 16;
constexpr int slot_request_rejit = 73;

struct Object {
    void** table;
};

bool same(const Guid* left, const Guid& right) { return std::memcmp(left, &right, sizeof(Guid)) == 0; }

Object callback{};
Object factory{};
void* callback_table[callback_slots]{};
void* factory_table[5]{};
std::atomic<Object*> info{nullptr};
std::atomic<int64_t> finished{0};
std::atomic<int64_t> errors{0};
std::atomic<int32_t> last_status{0};

HRESULT stub(void*) { return ok; }
uint32_t add_ref(void*) { return 1; }
uint32_t release(void*) { return 1; }

HRESULT callback_query(Object*, const Guid* iid, void** result) {
    bool known = same(iid, iid_unknown);
    for (const auto& candidate : iid_callbacks) known = known || same(iid, candidate);
    *result = known ? &callback : nullptr;
    return known ? ok : no_interface;
}

HRESULT initialize(Object*, Object* unknown) {
    Object* value = nullptr;
    using Query = HRESULT (*)(Object*, const Guid*, void**);
    if (const auto hr = reinterpret_cast<Query>(unknown->table[0])(unknown, &iid_info4, reinterpret_cast<void**>(&value)); hr != ok) return hr;
    using SetMask = HRESULT (*)(Object*, uint32_t);
    if (const auto hr = reinterpret_cast<SetMask>(value->table[slot_set_event_mask])(value, enable_rejit | monitor_jit); hr != ok) return hr;
    info.store(value, std::memory_order_release);
    return ok;
}

HRESULT cached_search(Object*, uintptr_t, int32_t* use_cached) {
    *use_cached = 1;
    return ok;
}

HRESULT inlining(Object*, uintptr_t, uintptr_t, int32_t* should_inline) {
    *should_inline = 1;
    return ok;
}

HRESULT rejit_finished(Object*, uintptr_t, uintptr_t, HRESULT status, int32_t) {
    if (status == ok) finished.fetch_add(1);
    else errors.fetch_add(1);
    last_status.store(status);
    return ok;
}

HRESULT rejit_error(Object*, uintptr_t, uint32_t, uintptr_t, HRESULT status) {
    errors.fetch_add(1);
    last_status.store(status);
    return ok;
}

HRESULT factory_query(Object*, const Guid* iid, void** result) {
    const bool known = same(iid, iid_unknown) || same(iid, iid_class_factory);
    *result = known ? &factory : nullptr;
    return known ? ok : no_interface;
}

HRESULT create_instance(Object*, void* outer, const Guid* iid, void** result) {
    if (outer) return no_interface;
    return callback_query(&callback, iid, result);
}

bool prepare() {
    for (auto& slot : callback_table) slot = reinterpret_cast<void*>(&stub);
    callback_table[0] = reinterpret_cast<void*>(&callback_query);
    callback_table[1] = reinterpret_cast<void*>(&add_ref);
    callback_table[2] = reinterpret_cast<void*>(&release);
    callback_table[slot_initialize] = reinterpret_cast<void*>(&initialize);
    callback_table[slot_cached_search] = reinterpret_cast<void*>(&cached_search);
    callback_table[slot_inlining] = reinterpret_cast<void*>(&inlining);
    callback_table[slot_rejit_finished] = reinterpret_cast<void*>(&rejit_finished);
    callback_table[slot_rejit_error] = reinterpret_cast<void*>(&rejit_error);
    callback.table = callback_table;
    factory_table[0] = reinterpret_cast<void*>(&factory_query);
    factory_table[1] = reinterpret_cast<void*>(&add_ref);
    factory_table[2] = reinterpret_cast<void*>(&release);
    factory_table[3] = reinterpret_cast<void*>(&create_instance);
    factory_table[4] = reinterpret_cast<void*>(&stub);
    factory.table = factory_table;
    return true;
}

const bool prepared = prepare();
}

extern "C" __attribute__((visibility("default"))) HRESULT DllGetClassObject(const Guid*, const Guid* iid, void** result) {
    if (!prepared) return not_ready;
    return factory_query(&factory, iid, result);
}

extern "C" __attribute__((visibility("default"))) HRESULT coz_rejit_request(uintptr_t function) {
    Object* value = info.load(std::memory_order_acquire);
    if (!value) return not_ready;
    uintptr_t type = 0, module = 0;
    uint32_t token = 0;
    using FunctionInfo = HRESULT (*)(Object*, uintptr_t, uintptr_t*, uintptr_t*, uint32_t*);
    if (const auto hr = reinterpret_cast<FunctionInfo>(value->table[slot_get_function_info])(value, function, &type, &module, &token); hr != ok) return hr;
    using Request = HRESULT (*)(Object*, uint32_t, uintptr_t*, uint32_t*);
    HRESULT result = not_ready;
    // The CLR rejects RequestReJIT from a managed thread outside a callback, so a native thread makes the call.
    std::thread caller([&] { result = reinterpret_cast<Request>(value->table[slot_request_rejit])(value, 1, &module, &token); });
    caller.join();
    return result;
}

extern "C" __attribute__((visibility("default"))) void coz_rejit_status(int64_t* done, int64_t* failed, int32_t* status) {
    *done = finished.load();
    *failed = errors.load();
    *status = last_status.load();
}
