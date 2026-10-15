#include "CooperativeApi.h"
#include "CooperativeSpinTiming.h"
#include <cstddef>
#include <limits>
#include <mutex>

using namespace coz::service;
static_assert(sizeof(CozCooperativeProgress) == 24 && offsetof(CozCooperativeProgress, phase) == 16);
static_assert(sizeof(CozCooperativeCpuReading) == 40);
static_assert(sizeof(CozCooperativeCpuChild) == 136 && offsetof(CozCooperativeCpuChild, after) == 96);
static_assert(sizeof(CozCooperativeChild) == 88 && offsetof(CozCooperativeChild, valid) == 80);
static_assert(sizeof(CozCooperativeSnapshot) == 88 && offsetof(CozCooperativeSnapshot, captured_ns) == 80);
static_assert(static_cast<uint32_t>(CooperativePollResult::idle) == COZ_COOPERATIVE_IDLE
    && static_cast<uint32_t>(CooperativePollResult::serviced) == COZ_COOPERATIVE_SERVICED
    && static_cast<uint32_t>(CooperativePollResult::rejected) == COZ_COOPERATIVE_REJECTED
    && static_cast<uint32_t>(CooperativePollResult::failed) == COZ_COOPERATIVE_FAILED);
namespace {
struct Slot {
    std::unique_ptr<CooperativeTarget> target;
    std::vector<TargetLeaseChild> copy;
    std::vector<CooperativeCpuChild> cpu_copy;
    std::thread::id owner;
    uint32_t generation{};
    bool copied_retired{};
    explicit Slot(uint32_t capacity) : copy(capacity) {}
};
}
struct CozCooperativeSession {
    const std::thread::id controller = std::this_thread::get_id();
    const uint64_t version;
    CooperativeSpinTiming timing;
    std::shared_ptr<CooperativeCodeLifetime> code;
    std::vector<std::unique_ptr<Slot>> slots;
    CozCooperativeSession(CooperativePollSite identity, uint32_t count, uint32_t capacity)
        : version(identity.codeVersion()), code(std::make_shared<CooperativeCodeLifetime>(identity)) {
        for (uint32_t index = 0; index < count; ++index) slots.push_back(std::make_unique<Slot>(capacity));
    }
};
namespace {
std::shared_mutex api_gate;
std::unique_ptr<CozCooperativeSession> active;
bool opened{};
template<class Action> int32_t translated(Action action) noexcept {
    try { return action(); }
    catch (const std::bad_alloc&) { return COZ_COOPERATIVE_MEMORY; }
    catch (...) { return COZ_COOPERATIVE_FAILURE; }
}
template<class Action> int32_t access(CozCooperativeSession* session, bool controller, Action action) noexcept {
    return translated([&]() -> int32_t {
        std::shared_lock lock(api_gate);
        if (!session || session != active.get()) return COZ_COOPERATIVE_INVALID;
        if (controller && session->controller != std::this_thread::get_id()) return COZ_COOPERATIVE_ROLE;
        return action(*session);
    });
}
Slot* get(CozCooperativeSession& session, uint32_t slot) noexcept {
    return slot < session.slots.size() ? session.slots[slot].get() : nullptr;
}
Slot* request(CozCooperativeSession& session, uint32_t slot, uint64_t generation) noexcept {
    auto* value = get(session, slot);
    return value && value->target && generation != 0 && generation <= std::numeric_limits<uint32_t>::max()
        && value->generation == generation ? value : nullptr;
}
}
int32_t COZ_COOPERATIVE_CALL coz_cooperative_open(uint64_t version, uint32_t targets, uint32_t capacity, CozCooperativeSession** session) {
    if (!session) return COZ_COOPERATIVE_INVALID;
    *session = nullptr;
    const auto identity = CooperativePollSite::parse(1, version, 1);
    if (!identity || targets == 0 || targets > 64 || capacity == 0 || capacity > 100000) return COZ_COOPERATIVE_INVALID;
    return translated([&]() -> int32_t {
        std::unique_lock lock(api_gate);
        if (opened) return COZ_COOPERATIVE_BUSY;
        auto candidate = std::make_unique<CozCooperativeSession>(*identity, targets, capacity);
        if (candidate->timing.now() == 0) return COZ_COOPERATIVE_CLOCK;
        active = std::move(candidate);
        opened = true;
        *session = active.get();
        return COZ_COOPERATIVE_OK;
    });
}
static int32_t registerTarget(CozCooperativeSession* session, uint32_t slot, uint64_t worker, uint64_t site, bool observeCpu, bool observeProgress = false) {
    return translated([&]() -> int32_t {
        std::unique_lock lock(api_gate);
        if (!session || session != active.get()) return COZ_COOPERATIVE_INVALID;
        if (session->controller == std::this_thread::get_id()) return COZ_COOPERATIVE_ROLE;
        const auto identity = CooperativePollSite::parse(worker, session->version, site);
        auto* value = get(*session, slot);
        if (!value || !identity) return COZ_COOPERATIVE_INVALID;
        if (value->target) return COZ_COOPERATIVE_BUSY;
        auto target = CooperativeTarget::registerCurrentThread(*identity, session->code, session->controller, value->copy.size(), session->timing, observeCpu, observeProgress);
        if (!target) return COZ_COOPERATIVE_BUSY;
        if (observeCpu) value->cpu_copy.resize(value->copy.size());
        value->owner = std::this_thread::get_id();
        value->target = std::move(target);
        return COZ_COOPERATIVE_OK;
    });
}
int32_t COZ_COOPERATIVE_CALL coz_cooperative_register(CozCooperativeSession* session, uint32_t slot, uint64_t worker, uint64_t site) {
    return registerTarget(session, slot, worker, site, false);
}

int32_t COZ_COOPERATIVE_CALL coz_cooperative_register_cpu(CozCooperativeSession* session, uint32_t slot, uint64_t worker, uint64_t site) {
    return registerTarget(session, slot, worker, site, true);
}

int32_t COZ_COOPERATIVE_CALL coz_cooperative_register_progress(CozCooperativeSession* session, uint32_t slot,
    uint64_t worker, uint64_t site, uint32_t observe_cpu) {
    if (observe_cpu > 1) return COZ_COOPERATIVE_INVALID;
    return registerTarget(session, slot, worker, site, observe_cpu != 0, true);
}
int32_t COZ_COOPERATIVE_CALL coz_cooperative_progress(CozCooperativeSession* session, uint32_t slot,
    uint64_t generation, CozCooperativeProgress* progress) {
    if (!progress) return COZ_COOPERATIVE_INVALID;
    *progress = {};
    if (generation == 0 || generation > std::numeric_limits<uint32_t>::max()) return COZ_COOPERATIVE_INVALID;
    return access(session, false, [&](CozCooperativeSession& value) -> int32_t {
        auto* target = get(value, slot);
        if (!target || !target->target) return COZ_COOPERATIVE_INVALID;
        if (!target->target->progressEnabled()) return COZ_COOPERATIVE_DISABLED;
        const auto result = target->target->progress(static_cast<uint32_t>(generation));
        if (!result) return COZ_COOPERATIVE_INVALID;
        *progress = {result->generation, result->ordinal, static_cast<uint64_t>(result->phase)};
        return COZ_COOPERATIVE_OK;
    });
}
int32_t COZ_COOPERATIVE_CALL coz_cooperative_cpu_snapshot(CozCooperativeSession* session,
    uint32_t slot, uint64_t generation, CozCooperativeCpuChild* children, uint32_t capacity, uint32_t* required) {
    if (!required || (capacity != 0 && !children) || capacity > 100000) return COZ_COOPERATIVE_INVALID;
    *required = 0;
    return access(session, true, [&](CozCooperativeSession& value) -> int32_t {
        auto* target = request(value, slot, generation);
        if (!target || target->cpu_copy.empty()) return COZ_COOPERATIVE_INVALID;
        const auto count = target->target->cpuSnapshot(target->generation, target->cpu_copy);
        if (!count) return COZ_COOPERATIVE_BUSY;
        *required = static_cast<uint32_t>(*count);
        if (*count > capacity) return COZ_COOPERATIVE_BUFFER;
        const auto reading = [](const CooperativeCpuReading& input) -> CozCooperativeCpuReading {
            return {static_cast<uint64_t>(input.status), static_cast<uint64_t>(input.clock),
                input.nanoseconds, input.resolution, input.error};
        };
        for (std::size_t index = 0; index < *count; ++index) {
            const auto& child = target->cpu_copy[index];
            children[index] = {child.generation, child.command, child.ordinal, child.worker,
                child.code_version, child.site, child.requested, reading(child.before), reading(child.after)};
        }
        return COZ_COOPERATIVE_OK;
    });
}

int32_t COZ_COOPERATIVE_CALL coz_cooperative_poll(CozCooperativeSession* session, uint32_t slot, uint64_t worker, uint64_t version, uint64_t site, uint32_t* result) {
    if (!result) return COZ_COOPERATIVE_INVALID;
    *result = static_cast<uint32_t>(CooperativePollResult::rejected);
    const auto identity = CooperativePollSite::parse(worker, version, site);
    if (!identity) return COZ_COOPERATIVE_INVALID;
    return access(session, false, [&](CozCooperativeSession& value) -> int32_t {
        auto* target = get(value, slot);
        if (!target || !target->target) return COZ_COOPERATIVE_INVALID;
        if (target->owner != std::this_thread::get_id()) return COZ_COOPERATIVE_ROLE;
        *result = static_cast<uint32_t>(target->target->poll(*identity));
        return COZ_COOPERATIVE_OK;
    });
}
int32_t COZ_COOPERATIVE_CALL coz_cooperative_submit(CozCooperativeSession* session, uint32_t slot, uint64_t command, uint64_t budget, uint64_t* generation) {
    if (!generation) return COZ_COOPERATIVE_INVALID;
    *generation = 0;
    return access(session, true, [&](CozCooperativeSession& value) -> int32_t {
        auto* target = get(value, slot);
        if (!target || !target->target || command == 0 || budget == 0 || budget > 2000000) return COZ_COOPERATIVE_INVALID;
        const auto submitted = target->target->submit(command, budget);
        if (!submitted) return COZ_COOPERATIVE_BUSY;
        target->generation = *submitted;
        target->copied_retired = false;
        *generation = *submitted;
        return COZ_COOPERATIVE_OK;
    });
}
int32_t COZ_COOPERATIVE_CALL coz_cooperative_cancel(CozCooperativeSession* session, uint32_t slot, uint64_t generation) {
    return access(session, true, [&](CozCooperativeSession& value) -> int32_t {
        auto* target = request(value, slot, generation);
        if (!target) return COZ_COOPERATIVE_INVALID;
        return target->target->cancel(target->generation) ? COZ_COOPERATIVE_OK : COZ_COOPERATIVE_BUSY;
    });
}
int32_t COZ_COOPERATIVE_CALL coz_cooperative_receipt_ready(CozCooperativeSession* session, uint32_t slot, uint64_t generation, uint32_t* ready) {
    if (!ready) return COZ_COOPERATIVE_INVALID;
    *ready = 0;
    if (generation == 0 || generation > std::numeric_limits<uint32_t>::max()) return COZ_COOPERATIVE_INVALID;
    return access(session, false, [&](CozCooperativeSession& value) -> int32_t {
        auto* target = get(value, slot);
        if (!target || !target->target) return COZ_COOPERATIVE_INVALID;
        const auto result = target->target->receiptReady(static_cast<uint32_t>(generation));
        if (!result) return COZ_COOPERATIVE_INVALID;
        *ready = *result ? 1 : 0;
        return COZ_COOPERATIVE_OK;
    });
}
int32_t COZ_COOPERATIVE_CALL coz_cooperative_snapshot(CozCooperativeSession* session, uint32_t slot, uint64_t generation, CozCooperativeSnapshot* result, CozCooperativeChild* children, uint32_t capacity) {
    if (!result || (capacity != 0 && !children) || capacity > 100000) return COZ_COOPERATIVE_INVALID;
    *result = {};
    return access(session, true, [&](CozCooperativeSession& value) -> int32_t {
        auto* target = request(value, slot, generation);
        if (!target) return COZ_COOPERATIVE_INVALID;
        target->copied_retired = false;
        std::size_t count{};
        const auto snapshot = target->target->snapshot(target->generation, target->copy, count);
        if (!snapshot) return COZ_COOPERATIVE_BUSY;
        const auto& header = *snapshot;
        *result = {header.generation, header.command, header.budget, header.children, header.measured,
            static_cast<uint64_t>(header.visibility), static_cast<uint64_t>(header.stop), static_cast<uint64_t>(header.error),
            header.transport_error, header.may_have_delivery, 0};
        if (count <= capacity) {
            for (std::size_t index = 0; index < count; ++index) {
                const auto& child = target->copy[index];
                if (!child.poll_site) return COZ_COOPERATIVE_FAILURE;
                const auto& site = *child.poll_site;
                children[index] = {child.ordinal, child.requested, child.armed, child.started, child.finished, child.waited,
                    site.worker(), site.codeVersion(), site.site(), static_cast<uint64_t>(child.wait_status), child.valid};
            }
        }
        result->captured_ns = value.timing.now();
        if (result->captured_ns == 0) return COZ_COOPERATIVE_CLOCK;
        if (count > capacity) return COZ_COOPERATIVE_BUFFER;
        target->copied_retired = header.visibility == TargetLeaseVisibility::retired && !header.may_have_delivery;
        return COZ_COOPERATIVE_OK;
    });
}
int32_t COZ_COOPERATIVE_CALL coz_cooperative_release(CozCooperativeSession* session, uint32_t slot, uint64_t generation) {
    return access(session, true, [&](CozCooperativeSession& value) -> int32_t {
        auto* target = request(value, slot, generation);
        if (!target) return COZ_COOPERATIVE_INVALID;
        if (!target->copied_retired || !target->target->release(target->generation)) return COZ_COOPERATIVE_BUSY;
        target->copied_retired = false;
        target->generation = 0;
        return COZ_COOPERATIVE_OK;
    });
}
int32_t COZ_COOPERATIVE_CALL coz_cooperative_retire(CozCooperativeSession* session, uint32_t slot) {
    return access(session, false, [&](CozCooperativeSession& value) -> int32_t {
        auto* target = get(value, slot);
        if (!target || !target->target) return COZ_COOPERATIVE_INVALID;
        if (target->owner != std::this_thread::get_id()) return COZ_COOPERATIVE_ROLE;
        return target->target->retire() ? COZ_COOPERATIVE_OK : COZ_COOPERATIVE_BUSY;
    });
}
int32_t COZ_COOPERATIVE_CALL coz_cooperative_revoke(CozCooperativeSession* session) {
    return access(session, true, [&](CozCooperativeSession& value) -> int32_t {
        return value.code->tryRevoke() ? COZ_COOPERATIVE_OK : COZ_COOPERATIVE_BUSY;
    });
}
int32_t COZ_COOPERATIVE_CALL coz_cooperative_clock(CozCooperativeSession* session, uint64_t* nanoseconds) {
    if (!nanoseconds) return COZ_COOPERATIVE_INVALID;
    *nanoseconds = 0;
    return access(session, true, [&](CozCooperativeSession& value) -> int32_t {
        *nanoseconds = value.timing.now();
        return *nanoseconds == 0 ? COZ_COOPERATIVE_CLOCK : COZ_COOPERATIVE_OK;
    });
}
int32_t COZ_COOPERATIVE_CALL coz_cooperative_close(CozCooperativeSession* session) {
    return translated([&]() -> int32_t {
        std::unique_lock lock(api_gate);
        if (!session || session != active.get()) return COZ_COOPERATIVE_INVALID;
        if (session->controller != std::this_thread::get_id()) return COZ_COOPERATIVE_ROLE;
        for (const auto& slot : session->slots) {
            if (slot->target && !slot->target->retired()) return COZ_COOPERATIVE_BUSY;
        }
        active.reset();
        return COZ_COOPERATIVE_OK;
    });
}
