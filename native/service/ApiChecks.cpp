#include "ServiceApi.h"
#include <atomic>
#include <cerrno>
#include <chrono>
#include <cstdlib>
#include <thread>

static void require(bool value, int code) { if (!value) std::_Exit(code); }
template<class Predicate> static void await(Predicate predicate) {
    const auto deadline = std::chrono::steady_clock::now() + std::chrono::seconds(2);
    while (!predicate()) {
        require(std::chrono::steady_clock::now() < deadline, 90);
        std::this_thread::yield();
    }
}

int main() {
    CozServiceSession* session{};
    require(coz_service_open(0, 1, 1, 8, &session) == EINVAL && session == nullptr, 1);
    require(coz_service_open(1, 1, 2, 8, &session) == EINVAL && session == nullptr, 2);
    require(coz_service_open(1, 1, 1, 0, &session) == EINVAL && session == nullptr, 3);
    require(coz_service_open(1, 1, 1, 8, &session) == 0 && session != nullptr, 4);
    CozServiceSession* second{};
    require(coz_service_open(1, 1, 1, 8, &second) == EALREADY && second == nullptr, 5);
    std::atomic<bool> ready{false}, stop{false};
    std::thread target([&] {
        require(coz_service_register(session, 1) == 0, 6);
        uint64_t now{};
        require(coz_service_clock(session, &now) == EPERM && coz_service_close(session) == EPERM, 7);
        ready = true;
        await([&] { return stop.load(); });
        require(coz_service_retire(session, 1) == 0, 8);
    });
    await([&] { return ready.load(); });
    require(coz_service_submit(session, -1, 1, 2, 1, 110000) == EINVAL, 9);
    require(coz_service_submit(session, 1, 0, 0, 1, 110000) == EINVAL, 10);
    require(coz_service_submit(session, 1, 1, 2, 1, 110000) == 0, 11);
    CozServiceSnapshot snapshot{};
    int result{};
    await([&] {
        result = coz_service_snapshot(session, 1, 1, 2, &snapshot, nullptr, 0, nullptr, 0);
        require(result == 0 || result == ENOBUFS, 12);
        return result == ENOBUFS;
    });
    require(snapshot.children == 1 && snapshot.captured_ns == 0, 13);
    require(coz_service_release(session, 1, 1, 2) == EBUSY && coz_service_close(session) == EBUSY, 14);
    struct GuardedChild { uint64_t before{123}; CozServiceChild child{}; uint64_t after{456}; } guarded;
    require(coz_service_snapshot(session, 1, 3, 4, &snapshot, &guarded.child, 1, nullptr, 0) == EINVAL, 15);
    require(coz_service_snapshot(session, 1, 1, 2, &snapshot, &guarded.child, 1, nullptr, 0) == 0, 16);
    require(guarded.before == 123 && guarded.after == 456 && guarded.child.ordinal == 1 && guarded.child.requested == 50000, 17);
    require(snapshot.phase == 2 && snapshot.error == 0 && snapshot.outstanding == 0 && snapshot.measured == 0
        && guarded.child.acknowledgment.outcome == 1 && snapshot.captured_ns >= guarded.child.acknowledgment.observed, 18);
    uint64_t receipt{};
    require(coz_service_clock(session, &receipt) == 0 && receipt >= snapshot.captured_ns, 19);
    require(coz_service_release(session, 1, 1, 2) == 0 && coz_service_close(session) == EBUSY, 20);
    stop = true;
    target.join();
    require(coz_service_close(session) == 0, 21);
    require(coz_service_clock(session, &receipt) == EINVAL && coz_service_close(session) == EINVAL, 22);
    require(coz_service_open(1, 1, 1, 8, &second) == EALREADY, 23);
    return 0;
}
