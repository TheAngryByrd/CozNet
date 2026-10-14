#include "ServiceApi.h"
#include "SignalTransport.Linux.h"
#include <algorithm>
#include <cerrno>
#include <memory>
#include <new>
#include <stdexcept>

using namespace coz::service;
static_assert(sizeof(CozServiceAcknowledgment) == 88);
static_assert(sizeof(CozServiceChild) == 104);
static_assert(sizeof(CozServiceSnapshot) == 104);

struct CozServiceSession {
    std::thread::id controller{std::this_thread::get_id()};
    LinuxSignalTransport transport;
    Owner owner;
    std::array<Snapshot, 2> snapshots;
    std::array<bool, 2> delivered_terminal{};
    CozServiceSession(InstructionRange range, SignalWaitMode mode, std::size_t capacity)
        : transport(range, mode), owner(transport, capacity) {}
};

namespace {
std::mutex api_mutex;
CozServiceSession* active{};
bool closed{};

template<class Action> int32_t guarded(CozServiceSession* session, bool controller, Action action) noexcept {
    try {
        std::lock_guard lock(api_mutex);
        if (session == nullptr || session != active) return EINVAL;
        if (controller && session->controller != std::this_thread::get_id()) return EPERM;
        return action(*session);
    } catch (const std::bad_alloc&) { return ENOMEM; }
    catch (...) { return EIO; }
}

CozServiceAcknowledgment copy(const Acknowledgment& value) {
    return {value.sequence, value.outcome, value.ip, value.handler_ns, value.wait_ns, value.wait_result,
        value.started, value.queued, value.handler_started, value.handler_finished, value.observed};
}
}

int32_t coz_service_open(uint64_t begin, uint64_t size, int32_t mode, uint64_t capacity, CozServiceSession** result) {
    if (result == nullptr) return EINVAL;
    *result = nullptr;
    try {
        std::lock_guard lock(api_mutex);
        if (active != nullptr || closed) return EALREADY;
        if (mode < 0 || mode > 1 || capacity == 0 || capacity > 100000) return EINVAL;
        auto session = std::make_unique<CozServiceSession>(InstructionRange(begin, size),
            mode == 0 ? SignalWaitMode::sleep : SignalWaitMode::spin, static_cast<std::size_t>(capacity));
        const int error = session->owner.start();
        if (error != 0) return error;
        active = session.release();
        *result = active;
        return 0;
    } catch (const std::invalid_argument&) { return EINVAL; }
    catch (const std::bad_alloc&) { return ENOMEM; }
    catch (...) { return EIO; }
}

int32_t coz_service_register(CozServiceSession* session, int32_t slot) {
    return guarded(session, false, [&](auto& value) { return value.transport.registerTarget(slot); });
}
int32_t coz_service_retire(CozServiceSession* session, int32_t slot) {
    return guarded(session, false, [&](auto& value) { return value.transport.retireTarget(slot); });
}
int32_t coz_service_submit(CozServiceSession* session, int32_t slot, uint64_t first, uint64_t second, uint64_t command, uint64_t budget) {
    return guarded(session, true, [&](auto& value) {
        if (!value.owner.submit(slot, {first, second}, command, budget)) return EINVAL;
        value.delivered_terminal[slot] = false;
        return 0;
    });
}
int32_t coz_service_cancel(CozServiceSession* session, int32_t slot, uint64_t first, uint64_t second) {
    return guarded(session, true, [&](auto& value) { return value.owner.cancel(slot, {first, second}) ? 0 : EINVAL; });
}

int32_t coz_service_snapshot(CozServiceSession* session, int32_t slot, uint64_t first, uint64_t second,
    CozServiceSnapshot* result, CozServiceChild* children, uint64_t child_capacity,
    CozServiceAcknowledgment* rejected, uint64_t rejected_capacity) {
    if (result == nullptr || slot < 0 || slot >= 2 || child_capacity > 100000 || rejected_capacity > 100000
        || (child_capacity != 0 && children == nullptr) || (rejected_capacity != 0 && rejected == nullptr)) return EINVAL;
    return guarded(session, true, [&](auto& value) {
        value.delivered_terminal[slot] = false;
        auto& snapshot = value.snapshots[slot];
        if (!value.owner.snapshot(slot, {first, second}, snapshot)) return EINVAL;
        *result = {snapshot.identity.first, snapshot.identity.second, snapshot.command, snapshot.budget, snapshot.measured,
            static_cast<uint64_t>(snapshot.phase), static_cast<uint64_t>(snapshot.error), snapshot.transport_error,
            snapshot.cancellation, snapshot.outstanding, snapshot.children.size(), snapshot.rejected.size(), 0};
        if (snapshot.children.size() > child_capacity || snapshot.rejected.size() > rejected_capacity) return ENOBUFS;
        for (std::size_t i = 0; i < snapshot.children.size(); ++i)
            children[i] = {snapshot.children[i].ordinal, snapshot.children[i].requested, copy(snapshot.children[i].acknowledgment)};
        std::transform(snapshot.rejected.begin(), snapshot.rejected.end(), rejected, copy);
        result->captured_ns = value.transport.now();
        if (result->captured_ns == 0) return EIO;
        value.delivered_terminal[slot] = snapshot.phase != Phase::active && !snapshot.outstanding;
        return 0;
    });
}

int32_t coz_service_release(CozServiceSession* session, int32_t slot, uint64_t first, uint64_t second) {
    if (slot < 0 || slot >= 2) return EINVAL;
    return guarded(session, true, [&](auto& value) {
        if (!value.delivered_terminal[slot] || !value.owner.release(slot, {first, second})) return EBUSY;
        value.delivered_terminal[slot] = false;
        return 0;
    });
}
int32_t coz_service_clock(CozServiceSession* session, uint64_t* nanoseconds) {
    if (nanoseconds == nullptr) return EINVAL;
    return guarded(session, true, [&](auto& value) {
        *nanoseconds = value.transport.now();
        return *nanoseconds == 0 ? EIO : 0;
    });
}
int32_t coz_service_close(CozServiceSession* session) {
    return guarded(session, true, [&](auto& value) {
        const int error = value.owner.shutdown();
        if (error != 0) return error;
        active = nullptr;
        closed = true;
        delete &value;
        return 0;
    });
}
