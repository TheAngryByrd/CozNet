#include "CooperativeApi.h"
#include "CooperativeSpinTiming.h"
#include <chrono>
#include <condition_variable>
#include <cstddef>
#include <cstdlib>
#include <deque>
#include <functional>
#include <future>
#include <mutex>
#include <string_view>

using namespace coz::service;
static void require(bool value, int code) { if (!value) std::_Exit(code); }
static_assert(sizeof(CozCooperativeChild) == 88 && offsetof(CozCooperativeChild, valid) == 80);
static_assert(sizeof(CozCooperativeSnapshot) == 88 && offsetof(CozCooperativeSnapshot, captured_ns) == 80);

class Worker {
public:
    Worker() : thread([this] {
        for (;;) {
            std::function<void()> action;
            {
                std::unique_lock lock(gate);
                changed.wait(lock, [&] { return stopped || !tasks.empty(); });
                if (stopped && tasks.empty()) return;
                action = std::move(tasks.front());
                tasks.pop_front();
            }
            action();
        }
    }) {}
    ~Worker() {
        {
            std::lock_guard lock(gate);
            stopped = true;
        }
        changed.notify_one();
        thread.join();
    }
    template<class Action> auto run(Action action) {
        using Result = std::invoke_result_t<Action>;
        auto task = std::make_shared<std::packaged_task<Result()>>(std::move(action));
        auto result = task->get_future();
        {
            std::lock_guard lock(gate);
            tasks.push_back([task] { (*task)(); });
        }
        changed.notify_one();
        require(result.wait_for(std::chrono::seconds(5)) == std::future_status::ready, 1);
        return result.get();
    }
private:
    std::mutex gate;
    std::condition_variable changed;
    std::deque<std::function<void()>> tasks;
    bool stopped{};
    std::thread thread;
};
static void nativeClockAndBoundedWait() {
    CooperativeSpinTiming timing;
    require(timing.wait(0) == TargetWaitStatus::failed && timing.wait(50001) == TargetWaitStatus::failed, 2);
    const auto before = timing.now();
    require(before != 0 && timing.wait(50000) == TargetWaitStatus::completed, 3);
    const auto after = timing.now();
    require(after >= before && after - before >= 50000, 4);
    Worker worker;
    const auto remote = worker.run([&] { return timing.now(); });
    require(remote >= after && timing.now() >= remote, 5);
}
static void registrationAndRoleBoundaries(CozCooperativeSession* session, Worker& worker) {
    require(coz_cooperative_register(session, 0, 1, 3) == COZ_COOPERATIVE_ROLE, 6);
    require(worker.run([&] { return coz_cooperative_register(session, 0, 0, 3); }) == COZ_COOPERATIVE_INVALID, 7);
    require(worker.run([&] { return coz_cooperative_register(session, 0, 1, 0); }) == COZ_COOPERATIVE_INVALID, 8);
    require(worker.run([&] { return coz_cooperative_register(session, 3, 1, 3); }) == COZ_COOPERATIVE_INVALID, 9);
    require(worker.run([&] { return coz_cooperative_register(session, 0, 1, 3); }) == COZ_COOPERATIVE_OK, 10);
    require(worker.run([&] { return coz_cooperative_register(session, 0, 1, 3); }) == COZ_COOPERATIVE_BUSY, 11);
    uint32_t outcome = 99;
    require(coz_cooperative_poll(session, 0, 1, 2, 3, &outcome) == COZ_COOPERATIVE_ROLE
        && outcome == static_cast<uint32_t>(CooperativePollResult::rejected), 12);
    require(coz_cooperative_retire(session, 0) == COZ_COOPERATIVE_ROLE, 13);
    require(worker.run([&] {
        uint64_t time = 99, generation = 99;
        CozCooperativeSnapshot snapshot{};
        return coz_cooperative_clock(session, &time) == COZ_COOPERATIVE_ROLE && time == 0
            && coz_cooperative_submit(session, 0, 1, 1, &generation) == COZ_COOPERATIVE_ROLE && generation == 0
            && coz_cooperative_cancel(session, 0, 1) == COZ_COOPERATIVE_ROLE
            && coz_cooperative_snapshot(session, 0, 1, &snapshot, nullptr, 0) == COZ_COOPERATIVE_ROLE
            && coz_cooperative_release(session, 0, 1) == COZ_COOPERATIVE_ROLE
            && coz_cooperative_revoke(session) == COZ_COOPERATIVE_ROLE
            && coz_cooperative_close(session) == COZ_COOPERATIVE_ROLE;
    }), 14);
    require(worker.run([&] {
        uint32_t result{};
        return coz_cooperative_poll(session, 0, 1, 2, 3, &result) == COZ_COOPERATIVE_OK
            && result == static_cast<uint32_t>(CooperativePollResult::idle);
    }), 15);
    require(coz_cooperative_close(session) == COZ_COOPERATIVE_BUSY, 16);
}
static void invalidApiInputs(CozCooperativeSession* session, Worker& worker) {
    uint64_t value = 99;
    uint32_t result = 99;
    CozCooperativeSnapshot snapshot{};
    require(coz_cooperative_open(2, 1, 4, nullptr) == COZ_COOPERATIVE_INVALID
        && coz_cooperative_clock(session, nullptr) == COZ_COOPERATIVE_INVALID
        && coz_cooperative_submit(session, 0, 1, 1, nullptr) == COZ_COOPERATIVE_INVALID, 54);
    require(coz_cooperative_clock(nullptr, &value) == COZ_COOPERATIVE_INVALID && value == 0
        && coz_cooperative_poll(nullptr, 0, 1, 2, 3, &result) == COZ_COOPERATIVE_INVALID
        && result == COZ_COOPERATIVE_REJECTED, 55);
    require(coz_cooperative_poll(session, 0, 1, 2, 3, nullptr) == COZ_COOPERATIVE_INVALID
        && coz_cooperative_poll(session, 0, 0, 2, 3, &result) == COZ_COOPERATIVE_INVALID
        && coz_cooperative_poll(session, 0, 1, 0, 3, &result) == COZ_COOPERATIVE_INVALID
        && coz_cooperative_poll(session, 0, 1, 2, 0, &result) == COZ_COOPERATIVE_INVALID, 56);
    require(coz_cooperative_snapshot(session, 0, 1, nullptr, nullptr, 0) == COZ_COOPERATIVE_INVALID
        && coz_cooperative_snapshot(session, 0, 1, &snapshot, nullptr, 1) == COZ_COOPERATIVE_INVALID
        && coz_cooperative_submit(session, 3, 1, 1, &value) == COZ_COOPERATIVE_INVALID, 57);
    require(worker.run([&] {
        uint32_t outcome{};
        return coz_cooperative_poll(session, 3, 1, 2, 3, &outcome) == COZ_COOPERATIVE_INVALID
            && coz_cooperative_retire(session, 3) == COZ_COOPERATIVE_INVALID;
    }), 58);
}
static void copiedJournalAndGenerationOwnership(CozCooperativeSession* session, Worker& worker) {
    uint64_t generation{}, rejected = 99;
    require(coz_cooperative_submit(session, 0, 0, 1, &rejected) == COZ_COOPERATIVE_INVALID && rejected == 0, 17);
    require(coz_cooperative_submit(session, 0, 1, 0, &rejected) == COZ_COOPERATIVE_INVALID
        && coz_cooperative_submit(session, 0, 1, 2000001, &rejected) == COZ_COOPERATIVE_INVALID, 18);
    require(coz_cooperative_submit(session, 0, 10, 1, &generation) == COZ_COOPERATIVE_OK && generation == 1, 19);
    require(coz_cooperative_submit(session, 0, 11, 1, &rejected) == COZ_COOPERATIVE_BUSY && rejected == 0, 20);
    require(coz_cooperative_release(session, 0, generation) == COZ_COOPERATIVE_BUSY, 21);
    require(coz_cooperative_cancel(session, 0, generation + (uint64_t{1} << 32)) == COZ_COOPERATIVE_INVALID, 22);
    require(worker.run([&] { return coz_cooperative_retire(session, 0); }) == COZ_COOPERATIVE_BUSY, 23);
    require(worker.run([&] {
        uint32_t result{};
        return coz_cooperative_poll(session, 0, 1, 2, 3, &result) == COZ_COOPERATIVE_OK
            && result == static_cast<uint32_t>(CooperativePollResult::serviced);
    }), 24);
    require(coz_cooperative_release(session, 0, generation) == COZ_COOPERATIVE_BUSY, 25);
    CozCooperativeSnapshot header{};
    struct { uint64_t before{123}; CozCooperativeChild child{}; uint64_t after{456}; } guarded;
    guarded.child.ordinal = 999;
    require(coz_cooperative_snapshot(session, 0, generation, &header, &guarded.child, 0) == COZ_COOPERATIVE_BUFFER
        && header.children == 1 && header.captured_ns != 0 && guarded.child.ordinal == 999, 26);
    require(coz_cooperative_release(session, 0, generation) == COZ_COOPERATIVE_BUSY, 27);
    require(coz_cooperative_snapshot(session, 0, generation, &header, &guarded.child, 1) == COZ_COOPERATIVE_OK, 28);
    uint64_t receipt{};
    require(coz_cooperative_clock(session, &receipt) == COZ_COOPERATIVE_OK && receipt >= header.captured_ns, 29);
    const auto& child = guarded.child;
    require(guarded.before == 123 && guarded.after == 456 && child.ordinal == 1 && child.worker == 1
        && child.code_version == 2 && child.site == 3 && child.requested == 1 && child.valid == 1
        && child.armed > 0 && child.started >= child.armed && child.finished >= child.started
        && header.captured_ns >= child.finished && header.measured == child.waited && child.waited >= 1
        && header.visibility == static_cast<uint64_t>(TargetLeaseVisibility::retired)
        && header.stop == static_cast<uint64_t>(TargetLeaseStop::completed)
        && header.error == 0 && header.may_have_delivery == 0 && header.transport_error == 0
        && header.generation == generation && header.command == 10 && header.budget == 1
        && child.wait_status == static_cast<uint64_t>(TargetWaitStatus::completed), 30);
    require(coz_cooperative_release(session, 0, generation) == COZ_COOPERATIVE_OK, 31);
    require(coz_cooperative_release(session, 0, generation) == COZ_COOPERATIVE_INVALID
        && coz_cooperative_snapshot(session, 0, generation, &header, nullptr, 0) == COZ_COOPERATIVE_INVALID, 32);
}
static void mismatchedSitesAndCancellation(CozCooperativeSession* session, Worker& worker) {
    for (int mode = 0; mode < 4; ++mode) {
        uint64_t generation{};
        require(coz_cooperative_submit(session, 0, 20 + mode, 50000, &generation) == COZ_COOPERATIVE_OK
            && generation == static_cast<uint64_t>(mode + 2), 33);
        if (mode == 3) require(coz_cooperative_cancel(session, 0, generation) == COZ_COOPERATIVE_OK, 34);
        const uint64_t owner = mode == 0 ? 9 : 1, version = mode == 1 ? 9 : 2, site = mode == 2 ? 9 : 3;
        require(worker.run([&] {
            uint32_t result{};
            return coz_cooperative_poll(session, 0, owner, version, site, &result) == COZ_COOPERATIVE_OK
                && result == static_cast<uint32_t>(CooperativePollResult::serviced);
        }), 35);
        CozCooperativeSnapshot header{};
        CozCooperativeChild child{};
        require(coz_cooperative_snapshot(session, 0, generation, &header, &child, 1) == COZ_COOPERATIVE_OK
            && header.children == 1 && header.measured == 0 && child.waited == 0 && child.valid == 1
            && child.worker == owner && child.code_version == version && child.site == site
            && header.stop == static_cast<uint64_t>(mode == 3 ? TargetLeaseStop::cancelled : TargetLeaseStop::deferred), 36);
        require(coz_cooperative_release(session, 0, generation) == COZ_COOPERATIVE_OK, 37);
    }
}
static void independentTargetOwnership(CozCooperativeSession* session, Worker& worker, Worker& other) {
    require(other.run([&] { return coz_cooperative_register(session, 1, 2, 4); }) == COZ_COOPERATIVE_OK, 59);
    uint64_t first{}, second{};
    require(coz_cooperative_submit(session, 0, 40, 1, &first) == COZ_COOPERATIVE_OK
        && coz_cooperative_submit(session, 1, 41, 1, &second) == COZ_COOPERATIVE_OK && first != second, 60);
    require(worker.run([&] {
        uint32_t outcome{};
        return coz_cooperative_poll(session, 1, 2, 2, 4, &outcome) == COZ_COOPERATIVE_ROLE;
    }), 61);
    require(coz_cooperative_cancel(session, 0, first) == COZ_COOPERATIVE_OK
        && coz_cooperative_cancel(session, 1, second) == COZ_COOPERATIVE_OK, 62);
    require(worker.run([&] {
        uint32_t outcome{};
        return coz_cooperative_poll(session, 0, 1, 2, 3, &outcome) == COZ_COOPERATIVE_OK && outcome == COZ_COOPERATIVE_SERVICED;
    }) && other.run([&] {
        uint32_t outcome{};
        return coz_cooperative_poll(session, 1, 2, 2, 4, &outcome) == COZ_COOPERATIVE_OK && outcome == COZ_COOPERATIVE_SERVICED;
    }), 63);
    CozCooperativeSnapshot header{};
    CozCooperativeChild child{};
    require(coz_cooperative_snapshot(session, 1, first, &header, &child, 1) == COZ_COOPERATIVE_INVALID
        && coz_cooperative_snapshot(session, 1, second, &header, &child, 1) == COZ_COOPERATIVE_OK
        && header.command == 41 && child.worker == 2 && child.site == 4, 64);
    require(coz_cooperative_release(session, 0, first) == COZ_COOPERATIVE_BUSY
        && coz_cooperative_release(session, 1, second) == COZ_COOPERATIVE_OK, 65);
    require(coz_cooperative_snapshot(session, 0, first, &header, &child, 1) == COZ_COOPERATIVE_OK
        && header.command == 40 && child.worker == 1 && child.site == 3
        && coz_cooperative_release(session, 0, first) == COZ_COOPERATIVE_OK, 66);
    require(other.run([&] { return coz_cooperative_retire(session, 1); }) == COZ_COOPERATIVE_OK, 67);
}
static void revocationAndClose(CozCooperativeSession* session, Worker& worker, Worker& other) {
    uint64_t generation{};
    require(coz_cooperative_submit(session, 0, 30, 50000, &generation) == COZ_COOPERATIVE_OK, 38);
    const auto deadline = std::chrono::steady_clock::now() + std::chrono::seconds(5);
    int32_t revoked;
    do { revoked = coz_cooperative_revoke(session); }
    while (revoked == COZ_COOPERATIVE_BUSY && std::chrono::steady_clock::now() < deadline);
    require(revoked == COZ_COOPERATIVE_OK, 39);
    require(other.run([&] { return coz_cooperative_register(session, 2, 2, 3); }) == COZ_COOPERATIVE_BUSY, 40);
    require(worker.run([&] {
        uint32_t result{};
        return coz_cooperative_poll(session, 0, 1, 2, 3, &result) == COZ_COOPERATIVE_OK
            && result == static_cast<uint32_t>(CooperativePollResult::serviced);
    }), 41);
    CozCooperativeSnapshot header{};
    CozCooperativeChild child{};
    require(coz_cooperative_snapshot(session, 0, generation, &header, &child, 1) == COZ_COOPERATIVE_OK
        && header.measured == 0 && header.stop == static_cast<uint64_t>(TargetLeaseStop::cancelled), 42);
    require(coz_cooperative_release(session, 0, generation) == COZ_COOPERATIVE_OK, 43);
    require(coz_cooperative_submit(session, 0, 31, 1, &generation) == COZ_COOPERATIVE_BUSY && generation == 0, 44);
    require(worker.run([&] { return coz_cooperative_retire(session, 0); }) == COZ_COOPERATIVE_OK, 45);
    require(worker.run([&] {
        uint32_t result{};
        return coz_cooperative_poll(session, 0, 1, 2, 3, &result) == COZ_COOPERATIVE_OK
            && result == static_cast<uint32_t>(CooperativePollResult::rejected);
    }), 46);
}
static void cpuSnapshotContract() {
    CozCooperativeSession* session{};
    require(coz_cooperative_open(2, 1, 4, &session) == COZ_COOPERATIVE_OK, 70);
    {
        Worker worker;
        require(coz_cooperative_register_cpu(session, 0, 1, 3) == COZ_COOPERATIVE_ROLE, 71);
        require(worker.run([&] { return coz_cooperative_register_cpu(session, 0, 1, 3); }) == COZ_COOPERATIVE_OK, 72);
        uint64_t generation{};
        require(coz_cooperative_submit(session, 0, 42, 1, &generation) == COZ_COOPERATIVE_OK, 73);
        uint32_t required = 99;
        CozCooperativeCpuChild cpu{};
        require(coz_cooperative_cpu_snapshot(session, 0, generation, &cpu, 1, &required) == COZ_COOPERATIVE_BUSY
            && required == 0, 74);
        require(worker.run([&] {
            uint32_t outcome{};
            return coz_cooperative_poll(session, 0, 1, 2, 3, &outcome) == COZ_COOPERATIVE_OK
                && outcome == COZ_COOPERATIVE_SERVICED;
        }), 75);
        require(worker.run([&] { return coz_cooperative_cpu_snapshot(session, 0, generation, &cpu, 1, &required); })
            == COZ_COOPERATIVE_ROLE, 76);
        cpu.command = 999;
        require(coz_cooperative_cpu_snapshot(session, 0, generation, &cpu, 0, &required) == COZ_COOPERATIVE_BUFFER
            && required == 1 && cpu.command == 999, 77);
        require(coz_cooperative_cpu_snapshot(session, 0, generation, &cpu, 1, &required) == COZ_COOPERATIVE_OK
            && required == 1 && cpu.command == 42 && cpu.generation == generation && cpu.ordinal == 1
            && cpu.worker == 1 && cpu.code_version == 2 && cpu.site == 3 && cpu.requested == 1, 78);
#if defined(__linux__)
        require(cpu.before.status == 1 && cpu.after.status == 1 && cpu.before.clock == 1 && cpu.after.clock == 1
            && cpu.before.resolution > 0 && cpu.after.resolution > 0 && cpu.before.error == 0 && cpu.after.error == 0
            && cpu.after.nanoseconds >= cpu.before.nanoseconds, 79);
#else
        require(cpu.before.status == 0 && cpu.after.status == 0 && cpu.before.clock == 0 && cpu.after.clock == 0, 79);
#endif
        require(coz_cooperative_release(session, 0, generation) == COZ_COOPERATIVE_BUSY, 80);
        CozCooperativeSnapshot snapshot{};
        CozCooperativeChild child{};
        require(coz_cooperative_snapshot(session, 0, generation, &snapshot, &child, 1) == COZ_COOPERATIVE_OK, 81);
        require(coz_cooperative_release(session, 0, generation) == COZ_COOPERATIVE_OK, 82);
        require(coz_cooperative_cpu_snapshot(session, 0, generation, &cpu, 1, &required) == COZ_COOPERATIVE_INVALID, 83);
        require(worker.run([&] { return coz_cooperative_retire(session, 0); }) == COZ_COOPERATIVE_OK, 84);
    }
    require(coz_cooperative_close(session) == COZ_COOPERATIVE_OK, 85);
}

static void receiptReadinessIsObservationOnly(CozCooperativeSession* session, Worker& worker, Worker& other) {
    uint32_t ready = 99;
    require(coz_cooperative_receipt_ready(session, 0, 1, nullptr) == COZ_COOPERATIVE_INVALID, 100);
    require(coz_cooperative_receipt_ready(nullptr, 0, 1, &ready) == COZ_COOPERATIVE_INVALID && ready == 0, 101);
    for (uint64_t invalid : {uint64_t{0}, uint64_t{1} << 32}) {
        ready = 99;
        require(coz_cooperative_receipt_ready(session, 0, invalid, &ready) == COZ_COOPERATIVE_INVALID && ready == 0, 102);
    }
    require(coz_cooperative_receipt_ready(session, 0, 1, &ready) == COZ_COOPERATIVE_INVALID
        && coz_cooperative_receipt_ready(session, 2, 1, &ready) == COZ_COOPERATIVE_INVALID
        && coz_cooperative_receipt_ready(session, 99, 1, &ready) == COZ_COOPERATIVE_INVALID, 103);
    uint64_t generation{};
    require(coz_cooperative_submit(session, 0, 60, 1, &generation) == COZ_COOPERATIVE_OK, 104);
    require(other.run([&] { return coz_cooperative_receipt_ready(session, 0, generation, &ready); }) == COZ_COOPERATIVE_OK
        && ready == 0, 105);
    require(worker.run([&] {
        uint32_t outcome{};
        return coz_cooperative_poll(session, 0, 1, 2, 3, &outcome) == COZ_COOPERATIVE_OK
            && outcome == COZ_COOPERATIVE_SERVICED;
    }), 106);
    require(worker.run([&] { return coz_cooperative_receipt_ready(session, 0, generation, &ready); }) == COZ_COOPERATIVE_OK
        && ready == 1, 107);
    require(coz_cooperative_receipt_ready(session, 0, generation, &ready) == COZ_COOPERATIVE_OK && ready == 1
        && coz_cooperative_release(session, 0, generation) == COZ_COOPERATIVE_BUSY, 108);
    CozCooperativeSnapshot snapshot{};
    CozCooperativeChild child{};
    require(coz_cooperative_snapshot(session, 0, generation, &snapshot, &child, 1) == COZ_COOPERATIVE_OK
        && coz_cooperative_release(session, 0, generation) == COZ_COOPERATIVE_OK, 109);
    ready = 99;
    require(coz_cooperative_receipt_ready(session, 0, generation, &ready) == COZ_COOPERATIVE_INVALID && ready == 0, 110);
}

int main(int argc, char** argv) {
    if (argc == 2 && std::string_view(argv[1]) == "cpu") { cpuSnapshotContract(); return 0; }
    if (argc == 2 && std::string_view(argv[1]) == "timing") { nativeClockAndBoundedWait(); return 0; }
    CozCooperativeSession* session{};
    require(coz_cooperative_open(0, 1, 4, &session) == COZ_COOPERATIVE_INVALID && !session, 47);
    require(coz_cooperative_open(2, 0, 4, &session) == COZ_COOPERATIVE_INVALID
        && coz_cooperative_open(2, 65, 4, &session) == COZ_COOPERATIVE_INVALID
        && coz_cooperative_open(2, 2, 0, &session) == COZ_COOPERATIVE_INVALID
        && coz_cooperative_open(2, 2, 100001, &session) == COZ_COOPERATIVE_INVALID, 48);
    require(coz_cooperative_open(2, 3, 4, &session) == COZ_COOPERATIVE_OK && session, 49);
    CozCooperativeSession* extra{};
    require(coz_cooperative_open(2, 2, 4, &extra) == COZ_COOPERATIVE_BUSY && !extra, 50);
    {
        Worker worker, other;
        registrationAndRoleBoundaries(session, worker);
        invalidApiInputs(session, worker);
        copiedJournalAndGenerationOwnership(session, worker);
        mismatchedSitesAndCancellation(session, worker);
        independentTargetOwnership(session, worker, other);
        receiptReadinessIsObservationOnly(session, worker, other);
        revocationAndClose(session, worker, other);
    }
    require(coz_cooperative_close(session) == COZ_COOPERATIVE_OK, 51);
    uint32_t ready = 99;
    require(coz_cooperative_receipt_ready(session, 0, 1, &ready) == COZ_COOPERATIVE_INVALID && ready == 0, 111);
    uint64_t time = 99;
    require(coz_cooperative_clock(session, &time) == COZ_COOPERATIVE_INVALID && time == 0
        && coz_cooperative_close(session) == COZ_COOPERATIVE_INVALID, 52);
    require(coz_cooperative_open(2, 2, 4, &extra) == COZ_COOPERATIVE_BUSY && !extra, 53);
}
