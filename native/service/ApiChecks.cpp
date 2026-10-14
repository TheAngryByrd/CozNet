#include "ServiceApi.h"
#include <atomic>
#include <cerrno>
#include <chrono>
#include <cstddef>
#include <cstdlib>
#include <string_view>
#include <thread>
#include <sys/syscall.h>
#include <unistd.h>

static void require(bool value, int code) { if (!value) std::_Exit(code); }
static_assert(sizeof(CozServiceSubmissionTiming) == 48);
static_assert(offsetof(CozServiceSubmissionTiming, returned) == 40);
static bool rejectedBeforeOwner(const CozServiceSubmissionTiming& timing) {
    return timing.entered > 0 && timing.api_locked >= timing.entered
        && timing.returned >= timing.api_locked && timing.owner_entered == 0
        && timing.owner_locked == 0 && timing.owner_finished == 0;
}
template<class Predicate> static void await(Predicate predicate) {
    const auto deadline = std::chrono::steady_clock::now() + std::chrono::seconds(2);
    while (!predicate()) {
        require(std::chrono::steady_clock::now() < deadline, 90);
        std::this_thread::yield();
    }
}

int main(int argc, char** argv) {
    require(argc == 1 || (argc == 2 && std::string_view(argv[1]) == "bounded"), 35);
    CozServiceSession* session{};
    require(coz_service_open(0, 1, 1, 8, &session) == EINVAL && session == nullptr, 1);
    require(coz_service_open(1, 1, 2, 8, &session) == EINVAL && session == nullptr, 2);
    require(coz_service_open(1, 1, 1, 0, &session) == EINVAL && session == nullptr, 3);
    require(coz_service_open_with_polling(1, 1, 1, 8, -1, &session) == EINVAL && session == nullptr, 36);
    require(coz_service_open_with_polling(1, 1, 1, 8, 2, &session) == EINVAL && session == nullptr, 37);
    require(coz_service_open_with_polling(1, 1, 1, 8, 1, nullptr) == EINVAL, 38);
    const int opened = argc == 1 ? coz_service_open(1, 1, 1, 8, &session)
        : coz_service_open_with_polling(1, 1, 1, 8, 1, &session);
    require(opened == 0 && session != nullptr, 4);
    struct GuardedThread { int32_t before{123}, id{}, after{456}; } owner;
    require(coz_service_owner_thread(session, nullptr) == EINVAL, 30);
    require(coz_service_owner_thread(session, &owner.id) == 0 && owner.id > 0
        && owner.id != ::syscall(SYS_gettid) && owner.before == 123 && owner.after == 456, 31);
    CozServiceSession* second{};
    require(coz_service_open(1, 1, 1, 8, &second) == EALREADY && second == nullptr, 5);
    std::atomic<bool> ready{false}, stop{false};
    std::thread target([&] {
        require(coz_service_register(session, 1) == 0, 6);
        uint64_t now{};
        require(coz_service_clock(session, &now) == EPERM && coz_service_close(session) == EPERM, 7);
        int32_t wrong_thread = 123;
        require(coz_service_owner_thread(session, &wrong_thread) == EPERM && wrong_thread == 0, 32);
        CozServiceSubmissionTiming timing{1, 2, 3, 4, 5, 6};
        require(coz_service_submit_timed(session, 1, 1, 2, 1, 110000, &timing) == EPERM
            && rejectedBeforeOwner(timing), 24);
        ready = true;
        await([&] { return stop.load(); });
        require(coz_service_retire(session, 1) == 0, 8);
    });
    await([&] { return ready.load(); });
    require(coz_service_submit(session, -1, 1, 2, 1, 110000) == EINVAL, 9);
    require(coz_service_submit(session, 1, 0, 0, 1, 110000) == EINVAL, 10);
    require(coz_service_submit_timed(session, 1, 1, 2, 1, 110000, nullptr) == EINVAL, 25);
    struct GuardedTiming { uint64_t before{123}; CozServiceSubmissionTiming value{}; uint64_t after{456}; } timed;
    require(coz_service_submit_timed(session, -1, 1, 2, 1, 110000, &timed.value) == EINVAL, 26);
    require(timed.value.entered > 0 && timed.value.api_locked >= timed.value.entered
        && timed.value.owner_entered >= timed.value.api_locked && timed.value.owner_locked >= timed.value.owner_entered
        && timed.value.owner_finished == 0 && timed.value.returned >= timed.value.owner_locked, 27);
    require(coz_service_submit_timed(session, 1, 1, 2, 1, 110000, &timed.value) == 0, 11);
    require(timed.before == 123 && timed.after == 456 && timed.value.entered > 0
        && timed.value.api_locked >= timed.value.entered && timed.value.owner_entered >= timed.value.api_locked
        && timed.value.owner_locked >= timed.value.owner_entered && timed.value.owner_finished >= timed.value.owner_locked
        && timed.value.returned >= timed.value.owner_finished, 28);
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
    int32_t same_owner{};
    require(coz_service_owner_thread(session, &same_owner) == 0 && same_owner == owner.id, 33);
    require(coz_service_close(session) == 0, 21);
    require(coz_service_owner_thread(session, &same_owner) == EINVAL && same_owner == 0, 34);
    require(coz_service_clock(session, &receipt) == EINVAL && coz_service_close(session) == EINVAL, 22);
    timed.value = {1, 2, 3, 4, 5, 6};
    require(coz_service_submit_timed(session, 1, 1, 2, 2, 110000, &timed.value) == EINVAL
        && rejectedBeforeOwner(timed.value) && timed.before == 123 && timed.after == 456, 29);
    require(coz_service_open(1, 1, 1, 8, &second) == EALREADY, 23);
    return 0;
}
