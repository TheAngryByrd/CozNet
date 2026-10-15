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
    Worker() {
        std::promise<std::unique_ptr<CooperativeTarget>> registration;
        auto registered = registration.get_future();
        thread = std::thread([this, registration = std::move(registration)]() mutable {
            registration.set_value(CooperativeTarget::registerCurrentThread(site(), code, controller, 4, timing));
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
int main() {
    registrationAndRoleChecks();
    finiteChildrenAndGenerationReuse();
    revocationCannotPassExecutingChild();
    pendingRevocationAndClockFailureRetainEvidence();
    preWaitClockFailureRetainsEvidence();
    interruptedAndFailedWaitsKeepDistinctAccounting();
}
