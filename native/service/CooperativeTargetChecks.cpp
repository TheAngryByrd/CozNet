#include "CooperativeTarget.h"
#include <array>
#include <chrono>
#include <condition_variable>
#include <cstdlib>
#include <deque>
#include <functional>
#include <future>
#include <mutex>

using namespace coz::service;
static void require(bool value, int code) { if (!value) std::_Exit(code); }
static CooperativePollSite site(uint64_t version = 2, uint64_t point = 3) {
    return *CooperativePollSite::parse(1, version, point);
}
static bool revoke(CooperativeCodeLifetime& code) {
    const auto deadline = std::chrono::steady_clock::now() + std::chrono::seconds(5);
    do {
        if (code.tryRevoke()) return true;
        std::this_thread::yield();
    } while (std::chrono::steady_clock::now() < deadline);
    return false;
}
class Timing final : public CooperativeTiming {
public:
    std::atomic<uint64_t> clock{1000}, calls{}, zeroAt{};
    std::atomic<bool> broken{}, hold{}, entered{}, resume{};
    std::atomic<TargetWaitStatus> outcome{TargetWaitStatus::completed};
    std::function<void()> duringWait;
    uint64_t cpuCalls{}, cpuCost{};
    CooperativeCpuStatus cpuStatus{CooperativeCpuStatus::observed};
    CooperativeCpuReading cpu() noexcept override {
        ++cpuCalls;
        clock.fetch_add(cpuCost);
        if (cpuStatus == CooperativeCpuStatus::unsupported) return {};
        if (cpuStatus == CooperativeCpuStatus::failed)
            return {cpuStatus, CooperativeCpuClock::linux_thread, 0, 0, 7};
        return {cpuStatus, CooperativeCpuClock::linux_thread, cpuCalls * 100, 1, 0};
    }
    uint64_t now() noexcept override {
        if (broken.load()) return 0;
        const auto value = clock.fetch_add(100);
        return value == zeroAt.load() ? 0 : value;
    }
    TargetWaitStatus wait(uint64_t requested) noexcept override {
        calls.fetch_add(1);
        if (duringWait) duringWait();
        entered.store(true, std::memory_order_release);
        const auto deadline = std::chrono::steady_clock::now() + std::chrono::seconds(5);
        while (hold.load() && !resume.load(std::memory_order_acquire)) {
            if (std::chrono::steady_clock::now() >= deadline) return TargetWaitStatus::failed;
            std::this_thread::yield();
        }
        clock.fetch_add(requested);
        return outcome.load();
    }
};
class Worker {
public:
    Timing timing;
    std::shared_ptr<CooperativeCodeLifetime> code = std::make_shared<CooperativeCodeLifetime>(site());
    std::unique_ptr<CooperativeTarget> target;
    const std::thread::id controller = std::this_thread::get_id();
    explicit Worker(bool observeCpu = false, bool observeProgress = false) {
        std::promise<std::unique_ptr<CooperativeTarget>> registration;
        auto registered = registration.get_future();
        thread = std::thread([this, observeCpu, observeProgress, registration = std::move(registration)]() mutable {
            registration.set_value(CooperativeTarget::registerCurrentThread(site(), code, controller, 4, timing, observeCpu, observeProgress));
            for (;;) {
                std::function<void()> task;
                {
                    std::unique_lock lock(gate);
                    changed.wait(lock, [&] { return stopped || !tasks.empty(); });
                    if (stopped && tasks.empty()) return;
                    task = std::move(tasks.front());
                    tasks.pop_front();
                }
                task();
            }
        });
        require(registered.wait_for(std::chrono::seconds(5)) == std::future_status::ready, 1);
        target = registered.get();
        require(target != nullptr, 2);
    }
    ~Worker() {
        { std::lock_guard lock(gate); stopped = true; }
        changed.notify_one();
        thread.join();
    }
    template<class Action> auto enqueue(Action action) {
        using Result = std::invoke_result_t<Action>;
        auto task = std::make_shared<std::packaged_task<Result()>>(std::move(action));
        auto result = task->get_future();
        { std::lock_guard lock(gate); tasks.push_back([task] { (*task)(); }); }
        changed.notify_one();
        return result;
    }
    template<class Action> auto run(Action action) {
        auto result = enqueue(std::move(action));
        require(result.wait_for(std::chrono::seconds(5)) == std::future_status::ready, 3);
        return result.get();
    }
private:
    std::mutex gate;
    std::condition_variable changed;
    std::deque<std::function<void()>> tasks;
    bool stopped{};
    std::thread thread;
};
static TargetLeaseSnapshot read(Worker& worker, uint32_t generation, std::array<TargetLeaseChild, 4>& children) {
    std::size_t count = 99;
    const auto snapshot = worker.target->snapshot(generation, children, count);
    require(snapshot && snapshot->children == count, 4);
    return *snapshot;
}
static void registrationAndRoleChecks() {
    Worker worker;
    require(!CooperativeTarget::registerCurrentThread(site(), worker.code, worker.controller, 4, worker.timing), 5);
    require(worker.run([&] {
        auto upper = CooperativeTarget::registerCurrentThread(site(), worker.code, worker.controller, 100000, worker.timing);
        auto lower = CooperativeTarget::registerCurrentThread(site(), worker.code, worker.controller, 1, worker.timing);
        return upper && upper->retire() && lower && lower->retire()
            && !CooperativeTarget::registerCurrentThread(site(4), worker.code, worker.controller, 4, worker.timing)
            && !CooperativeTarget::registerCurrentThread(site(), nullptr, worker.controller, 4, worker.timing)
            && !CooperativeTarget::registerCurrentThread(site(), worker.code, {}, 4, worker.timing)
            && !CooperativeTarget::registerCurrentThread(site(), worker.code, worker.controller, 0, worker.timing)
            && !CooperativeTarget::registerCurrentThread(site(), worker.code, worker.controller, 100001, worker.timing);
    }), 6);
    require(!worker.target->submit(0, 1) && !worker.target->submit(1, 0) && !worker.target->submit(1, 2000001), 7);
    require(worker.run([&] { return !worker.target->submit(1, 1); }), 8);
    require(worker.target->poll(site()) == CooperativePollResult::rejected && !worker.target->retire(), 9);
    require(worker.run([&] { return worker.target->poll(site()) == CooperativePollResult::idle && worker.target->retire(); }), 10);
    require(worker.target->retired() && !worker.target->submit(1, 1), 11);
}
static void finiteChildrenAndGenerationReuse() {
    Worker worker;
    std::array<TargetLeaseChild, 4> children{};
    const auto generation = worker.target->submit(10, 60000);
    require(generation && *generation == 1 && !worker.target->submit(11, 1), 12);
    require(!worker.target->release(*generation) && !worker.run([&] { return worker.target->retire(); }), 13);
    require(worker.run([&] {
        std::size_t count = 0;
        return !worker.target->cancel(*generation) && !worker.target->snapshot(*generation, children, count);
    }), 39);
    worker.timing.duringWait = [&] { require(worker.target->poll(site()) == CooperativePollResult::rejected, 14); };
    require(worker.run([&] { return worker.target->poll(site()); }) == CooperativePollResult::serviced, 15);
    auto snapshot = read(worker, *generation, children);
    require(snapshot.visibility == TargetLeaseVisibility::active && snapshot.children == 1 && snapshot.measured == 50100
        && children[0].requested == 50000 && children[0].poll_site == site() && children[0].ip == 0
        && worker.timing.calls.load() == 1 && !worker.target->release(*generation), 16);
    require(worker.run([&] { return worker.target->poll(site()); }) == CooperativePollResult::serviced, 17);
    std::size_t insufficient = 0;
    require(!worker.target->snapshot(*generation, {}, insufficient) && insufficient == 2, 40);
    require(!worker.target->release(*generation), 18);
    snapshot = read(worker, *generation, children);
    require(snapshot.visibility == TargetLeaseVisibility::retired && snapshot.children == 2 && snapshot.measured == 60100
        && children[1].requested == 9900 && worker.timing.calls.load() == 2, 19);
    require(!worker.run([&] { return worker.target->release(*generation) || worker.target->retire(); }), 20);
    require(worker.target->release(*generation) && !worker.target->release(*generation), 21);
    const auto next = worker.target->submit(11, 1);
    require(next && *next == 2 && !worker.target->cancel(*generation), 22);
    std::size_t required = 99;
    require(!worker.target->snapshot(*generation, children, required), 23);
    require(worker.run([&] { return worker.target->poll(site(2, 4)); }) == CooperativePollResult::serviced, 24);
    snapshot = read(worker, *next, children);
    require(snapshot.stop == TargetLeaseStop::deferred && snapshot.measured == 0 && children[0].poll_site == site(2, 4)
        && worker.timing.calls.load() == 2, 25);
    require(worker.target->release(*next) && worker.run([&] { return worker.target->retire(); }), 26);
}
static void revocationCannotPassExecutingChild() {
    Worker worker;
    std::array<TargetLeaseChild, 4> children{};
    const auto generation = worker.target->submit(1, 50000);
    require(generation.has_value(), 27);
    worker.timing.hold.store(true);
    auto poll = worker.enqueue([&] { return worker.target->poll(site()); });
    const auto deadline = std::chrono::steady_clock::now() + std::chrono::seconds(5);
    while (!worker.timing.entered.load(std::memory_order_acquire)) {
        require(std::chrono::steady_clock::now() < deadline, 28);
        std::this_thread::yield();
    }
    require(!worker.code->tryRevoke() && worker.target->cancel(*generation), 29);
    const auto active = read(worker, *generation, children);
    require(active.visibility == TargetLeaseVisibility::active && active.children == 0
        && !worker.target->release(*generation), 41);
    worker.timing.resume.store(true, std::memory_order_release);
    require(poll.wait_for(std::chrono::seconds(5)) == std::future_status::ready && poll.get() == CooperativePollResult::serviced, 30);
    const auto snapshot = read(worker, *generation, children);
    require(snapshot.stop == TargetLeaseStop::cancelled && snapshot.measured == 50100 && children[0].valid, 31);
    require(worker.target->release(*generation) && revoke(*worker.code) && !worker.target->submit(2, 1), 32);
    require(worker.run([&] {
        return !CooperativeTarget::registerCurrentThread(site(), worker.code, worker.controller, 4, worker.timing)
            && worker.target->retire();
    }), 33);
}
static void pendingRevocationAndClockFailureRetainEvidence() {
    for (int mode = 0; mode != 3; ++mode) {
        const bool brokenClock = mode == 2;
        Worker worker;
        std::array<TargetLeaseChild, 4> children{};
        const auto generation = worker.target->submit(1, 50000);
        require(generation.has_value(), 34);
        if (brokenClock) worker.timing.broken.store(true);
        else require(mode == 0 ? worker.target->cancel(*generation) : revoke(*worker.code), 35);
        const auto result = worker.run([&] { return worker.target->poll(site()); });
        require(result == (brokenClock ? CooperativePollResult::failed : CooperativePollResult::serviced), 36);
        const auto snapshot = read(worker, *generation, children);
        require(snapshot.visibility == TargetLeaseVisibility::retired && snapshot.measured == 0
            && snapshot.stop == (brokenClock ? TargetLeaseStop::failed : TargetLeaseStop::cancelled)
            && children[0].poll_site == site() && worker.timing.calls.load() == 0, 37);
        require(worker.target->release(*generation) && worker.run([&] { return worker.target->retire(); }), 38);
    }
}
static void preWaitClockFailureRetainsEvidence() {
    Worker worker;
    std::array<TargetLeaseChild, 4> children{};
    worker.timing.zeroAt.store(1200);
    const auto generation = worker.target->submit(1, 50000);
    require(generation.has_value(), 46);
    require(worker.run([&] { return worker.target->poll(site()); }) == CooperativePollResult::failed, 47);
    require(worker.timing.calls.load() == 0 && !worker.target->release(*generation), 48);
    const auto snapshot = read(worker, *generation, children);
    require(snapshot.visibility == TargetLeaseVisibility::retired && snapshot.children == 1
        && snapshot.measured == 0 && snapshot.stop == TargetLeaseStop::failed
        && children[0].waited == 0 && !children[0].valid
        && children[0].wait_status == TargetWaitStatus::failed, 49);
    require(worker.target->release(*generation) && worker.run([&] { return worker.target->retire(); }), 50);
}
static void interruptedAndFailedWaitsKeepDistinctAccounting() {
    for (const auto status : {TargetWaitStatus::interrupted, TargetWaitStatus::failed}) {
        Worker worker;
        std::array<TargetLeaseChild, 4> children{};
        worker.timing.outcome.store(status);
        const auto generation = worker.target->submit(1, 100000);
        require(generation.has_value(), 42);
        const auto result = worker.run([&] { return worker.target->poll(site()); });
        const bool failed = status == TargetWaitStatus::failed;
        require(result == (failed ? CooperativePollResult::failed : CooperativePollResult::serviced), 43);
        const auto snapshot = read(worker, *generation, children);
        require(snapshot.visibility == TargetLeaseVisibility::retired && snapshot.children == 1
            && snapshot.measured == (failed ? 0ULL : 50100ULL) && children[0].waited == 50100
            && children[0].valid != failed && children[0].wait_status == status
            && snapshot.stop == (failed ? TargetLeaseStop::failed : TargetLeaseStop::interrupted), 44);
        require(worker.target->release(*generation) && worker.run([&] { return worker.target->retire(); }), 45);
    }
}
static void cpuObservationsPreserveServiceAndOwnership() {
    for (int mode = 0; mode < 7; ++mode) {
        Worker worker(mode != 0);
        if (mode == 2) worker.timing.cpuStatus = CooperativeCpuStatus::unsupported;
        if (mode == 3) worker.timing.cpuStatus = CooperativeCpuStatus::failed;
        if (mode == 4) worker.timing.outcome.store(TargetWaitStatus::failed);
        if (mode == 5) worker.timing.cpuCost = 3000000;
        if (mode == 6) worker.timing.zeroAt.store(1200);
        const auto generation = worker.target->submit(42, 50000);
        require(generation.has_value(), 70);
        std::array<CooperativeCpuChild, 4> observations{};
        require(!worker.target->cpuSnapshot(*generation, observations), 71);
        const auto outcome = worker.run([&] { return worker.target->poll(site()); });
        require(outcome == (mode >= 4 ? CooperativePollResult::failed : CooperativePollResult::serviced), 72);
        require(!worker.run([&] { return worker.target->cpuSnapshot(*generation, observations).has_value(); }), 73);
        const auto count = worker.target->cpuSnapshot(*generation, observations);
        if (mode == 0) {
            require(!count && worker.timing.cpuCalls == 0, 74);
        } else {
            require(count && *count == 1 && worker.timing.cpuCalls == 2, 75);
            const auto& child = observations[0];
            require(child.generation == *generation && child.command == 42 && child.ordinal == 1
                && child.worker == 1 && child.code_version == 2 && child.site == 3 && child.requested == 50000, 76);
            require(child.before.status == worker.timing.cpuStatus && child.after.status == worker.timing.cpuStatus, 77);
            if (mode == 3) require(child.before.error == 7 && child.after.error == 7, 78);
            if (mode != 2 && mode != 3) require(child.before.nanoseconds == 100 && child.after.nanoseconds == 200, 79);
            observations[0].command = 999;
            require(worker.target->cpuSnapshot(*generation, {}) == 1 && observations[0].command == 999, 80);
        }
        require(!worker.target->release(*generation), 81);
        std::array<TargetLeaseChild, 4> children{};
        const auto snapshot = read(worker, *generation, children);
        require(children[0].waited == (mode == 6 ? 0ULL : 50100ULL) && snapshot.measured == (mode >= 4 ? 0ULL : 50100ULL), 82);
        if (mode == 5) require(snapshot.error == TargetLeaseError::handler_bound, 83);
        require(worker.target->release(*generation) && !worker.target->cpuSnapshot(*generation, observations), 84);
        const auto next = worker.target->submit(43, 50000);
        require(next && *next != *generation && worker.target->cancel(*next), 85);
        require(worker.run([&] { return worker.target->poll(site()); }) == CooperativePollResult::serviced, 86);
        if (mode != 0) require(worker.target->cpuSnapshot(*next, observations) == 0, 87);
        read(worker, *next, children);
        require(worker.target->release(*next) && worker.run([&] { return worker.target->retire(); }), 88);
    }
}

static void cpuJournalRetainsBoundedChildren() {
    Worker worker(true);
    const auto generation = worker.target->submit(51, 2000000);
    require(generation.has_value(), 90);
    std::array<CooperativeCpuChild, 4> observations{};
    for (std::size_t index = 0; index < observations.size(); ++index) {
        require(!worker.target->cpuSnapshot(*generation, observations), 91);
        const auto outcome = worker.run([&] { return worker.target->poll(site()); });
        require(outcome == (index == 3 ? CooperativePollResult::failed : CooperativePollResult::serviced), 92);
    }
    require(worker.target->cpuSnapshot(*generation, observations) == 4, 93);
    for (std::size_t index = 0; index < observations.size(); ++index) {
        const auto& child = observations[index];
        require(child.generation == *generation && child.command == 51 && child.ordinal == index + 1
            && child.before.nanoseconds == (index * 2 + 1) * 100
            && child.after.nanoseconds == (index * 2 + 2) * 100, 94);
    }
    std::array<TargetLeaseChild, 4> children{};
    const auto snapshot = read(worker, *generation, children);
    require(snapshot.error == TargetLeaseError::journal_full && snapshot.children == 4, 95);
    require(worker.target->release(*generation), 96);
    const auto next = worker.target->submit(52, 1);
    require(next && worker.run([&] { return worker.target->poll(site()); }) == CooperativePollResult::serviced, 97);
    require(worker.target->cpuSnapshot(*next, observations) == 1 && observations[0].generation == *next
        && observations[0].command == 52 && observations[0].ordinal == 1 && observations[0].before.nanoseconds == 900, 98);
    read(worker, *next, children);
    require(worker.target->release(*next) && worker.run([&] { return worker.target->retire(); }), 99);
}

static void receiptReadinessPreservesOwnership() {
    for (int mode = 0; mode < 3; ++mode) {
        Worker worker;
        require(!worker.target->receiptReady(0) && !worker.target->receiptReady(1), 130);
        const auto generation = worker.target->submit(61, 1);
        require(generation && worker.target->receiptReady(*generation) == false, 131);
        require(worker.run([&] { return worker.target->receiptReady(*generation); }) == false, 132);
        if (mode == 1) worker.timing.outcome.store(TargetWaitStatus::failed);
        if (mode == 2) require(worker.target->cancel(*generation), 133);
        const auto result = worker.run([&] { return worker.target->poll(site()); });
        require(result == (mode == 1 ? CooperativePollResult::failed : CooperativePollResult::serviced), 134);
        require(worker.target->receiptReady(*generation) == true
            && worker.run([&] { return worker.target->receiptReady(*generation); }) == true, 135);
        require(!worker.target->release(*generation), 136);
        std::array<TargetLeaseChild, 4> children{};
        read(worker, *generation, children);
        require(worker.target->release(*generation) && !worker.target->receiptReady(*generation), 137);
        const auto next = worker.target->submit(62, 1);
        require(next && *next != *generation && !worker.target->receiptReady(*generation)
            && worker.target->receiptReady(*next) == false && worker.target->cancel(*next), 138);
        require(worker.run([&] { return worker.target->poll(site()); }) == CooperativePollResult::serviced, 139);
        read(worker, *next, children);
        require(worker.target->release(*next) && worker.run([&] { return worker.target->retire(); }), 140);
    }
}

static void executingChildIsNotReceiptReady() {
    Worker worker;
    worker.timing.hold.store(true);
    const auto generation = worker.target->submit(63, 1);
    require(generation.has_value(), 141);
    auto polling = worker.enqueue([&] { return worker.target->poll(site()); });
    const auto deadline = std::chrono::steady_clock::now() + std::chrono::seconds(5);
    while (!worker.timing.entered.load(std::memory_order_acquire) && std::chrono::steady_clock::now() < deadline)
        std::this_thread::yield();
    require(worker.timing.entered.load() && worker.target->receiptReady(*generation) == false, 142);
    worker.timing.resume.store(true, std::memory_order_release);
    require(polling.wait_for(std::chrono::seconds(5)) == std::future_status::ready
        && polling.get() == CooperativePollResult::serviced && worker.target->receiptReady(*generation) == true, 143);
    std::array<TargetLeaseChild, 4> children{};
    read(worker, *generation, children);
    require(worker.target->release(*generation) && worker.run([&] { return worker.target->retire(); }), 144);
}

static void progressPreservesGenerationAndTerminalPhase() {
    CooperativeProgress marker;
    require(!marker.read(0) && !marker.read(1), 150);
    marker.begin(1);
    marker.record(1, CooperativeProgressPhase::child_admitted, 100000);
    marker.record(1, CooperativeProgressPhase::poll_entered);
    auto value = marker.read(1);
    require(value && value->ordinal == 100000 && value->phase == CooperativeProgressPhase::poll_entered, 151);
    marker.record(1, CooperativeProgressPhase::child_admitted, 100001);
    require(marker.read(1)->ordinal == 100000, 152);
    marker.record(1, CooperativeProgressPhase::failed);
    marker.record(1, CooperativeProgressPhase::returning);
    require(marker.read(1)->phase == CooperativeProgressPhase::failed, 153);
    marker.record(1, CooperativeProgressPhase::quiesced);
    marker.record(1, CooperativeProgressPhase::failed);
    require(marker.read(1)->phase == CooperativeProgressPhase::quiesced, 154);
    marker.begin(2);
    marker.record(1, CooperativeProgressPhase::quiesced);
    value = marker.read(2);
    require(!marker.read(1) && value && value->ordinal == 0 && value->phase == CooperativeProgressPhase::pending, 155);
}
static void progressConcurrentGenerationReplacement() {
    CooperativeProgress marker;
    marker.begin(1);
    std::atomic<uint32_t> generation{1};
    std::atomic<bool> started{}, stop{};
    std::thread oldWriter([&] {
        started.store(true, std::memory_order_release);
        while (!stop.load(std::memory_order_acquire))
            marker.record(generation.load(std::memory_order_acquire), CooperativeProgressPhase::child_admitted, 1);
    });
    while (!started.load(std::memory_order_acquire)) std::this_thread::yield();
    bool preserved = true;
    for (uint32_t next = 2; next <= 20000; ++next) {
        marker.begin(next);
        generation.store(next, std::memory_order_release);
        preserved = preserved && marker.read(next).has_value();
    }
    stop.store(true, std::memory_order_release);
    oldWriter.join();
    require(preserved && marker.read(20000).has_value(), 173);
}
static void progressObservesExecutingChildWithoutReceiptAuthority() {
    Worker worker(false, true);
    require(worker.target->progressEnabled() && !worker.target->progress(0) && !worker.target->progress(1), 156);
    const auto generation = worker.target->submit(90, 60000);
    require(generation.has_value(), 157);
    auto value = worker.target->progress(*generation);
    require(value && value->generation == *generation && value->ordinal == 0 && value->phase == CooperativeProgressPhase::pending, 158);
    require(worker.target->poll(site()) == CooperativePollResult::rejected &&
        worker.target->progress(*generation)->phase == CooperativeProgressPhase::pending, 159);
    worker.timing.hold.store(true);
    auto polling = worker.enqueue([&] { return worker.target->poll(site()); });
    const auto deadline = std::chrono::steady_clock::now() + std::chrono::seconds(5);
    while (!worker.timing.entered.load(std::memory_order_acquire) && std::chrono::steady_clock::now() < deadline)
        std::this_thread::yield();
    value = worker.target->progress(*generation);
    require(worker.timing.entered.load() && value && value->ordinal == 1 &&
        value->phase == CooperativeProgressPhase::wait_entered && !worker.target->release(*generation), 160);
    worker.timing.resume.store(true, std::memory_order_release);
    require(polling.wait_for(std::chrono::seconds(5)) == std::future_status::ready &&
        polling.get() == CooperativePollResult::serviced, 161);
    value = worker.target->progress(*generation);
    require(value && value->ordinal == 1 && value->phase == CooperativeProgressPhase::returning, 162);
    require(worker.run([&] { return worker.target->poll(site()); }) == CooperativePollResult::serviced, 163);
    value = worker.target->progress(*generation);
    require(value && value->ordinal == 2 && value->phase == CooperativeProgressPhase::quiesced &&
        !worker.target->release(*generation), 164);
    std::array<TargetLeaseChild, 4> children{};
    read(worker, *generation, children);
    require(worker.target->release(*generation) && !worker.target->progress(*generation), 165);
    const auto next = worker.target->submit(91, 1);
    require(next && *next != *generation && !worker.target->progress(*generation) &&
        worker.target->progress(*next)->ordinal == 0, 166);
    require(worker.target->cancel(*next) && worker.run([&] { return worker.target->poll(site()); }) == CooperativePollResult::serviced, 167);
    require(worker.target->progress(*next)->phase == CooperativeProgressPhase::quiesced, 168);
    read(worker, *next, children);
    require(worker.target->release(*next) && worker.run([&] { return worker.target->retire(); }), 169);
}
static void disabledProgressRemainsUnavailable() {
    Worker worker;
    const auto generation = worker.target->submit(92, 1);
    require(generation && !worker.target->progressEnabled() && !worker.target->progress(*generation), 170);
    require(worker.run([&] { return worker.target->poll(site()); }) == CooperativePollResult::serviced &&
        !worker.target->progress(*generation), 171);
    std::array<TargetLeaseChild, 4> children{};
    read(worker, *generation, children);
    require(worker.target->release(*generation) && worker.run([&] { return worker.target->retire(); }), 172);
}
int main() {
    progressPreservesGenerationAndTerminalPhase();
    progressConcurrentGenerationReplacement();
    progressObservesExecutingChildWithoutReceiptAuthority();
    disabledProgressRemainsUnavailable();
    receiptReadinessPreservesOwnership();
    executingChildIsNotReceiptReady();
    cpuJournalRetainsBoundedChildren();
    cpuObservationsPreserveServiceAndOwnership();
    registrationAndRoleChecks();
    finiteChildrenAndGenerationReuse();
    revocationCannotPassExecutingChild();
    pendingRevocationAndClockFailureRetainEvidence();
    preWaitClockFailureRetainsEvidence();
    interruptedAndFailedWaitsKeepDistinctAccounting();
}
