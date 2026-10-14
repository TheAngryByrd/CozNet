#include "TargetLease.h"
#include <array>
#include <cstdlib>
#include <limits>
#include <memory>
#include <utility>
#include <thread>
#include <chrono>

using namespace coz::service;
static void require(bool value, int code) { if (!value) std::_Exit(code); }
static TargetLeasePlan plan(uint64_t budget = 110000, uint64_t generation = 1, uint64_t command = 1) {
    const auto parsed = TargetLeasePlan::parse(generation, command, budget, 100, 20);
    require(parsed.has_value(), 1);
    return *parsed;
}
static void boundaries() {
    require(!TargetLeasePlan::parse(0, 1, 1, 100, 20), 2);
    require(!TargetLeasePlan::parse(1, 0, 1, 100, 20), 3);
    require(!TargetLeasePlan::parse(1, 1, 0, 100, 20), 4);
    require(!TargetLeasePlan::parse(1, 1, 2000001, 100, 20), 5);
    require(!TargetLeasePlan::parse(1, 1, 1, 0, 20), 6);
    require(!TargetLeasePlan::parse(1, 1, 1, 100, 0), 7);
    require(!TargetLeasePlan::parse(1, 1, 1, std::numeric_limits<uint64_t>::max(), 1), 8);
    require(TargetLeasePlan::parse(1, 1, 2000000, 100, 20).has_value(), 9);
    require(plan().eligible(100) && plan().eligible(119) && !plan().eligible(99) && !plan().eligible(120), 10);
}
static void settlement() {
    std::array<TargetLeaseChild, 3> journal;
    TargetLease lease(plan(), journal);
    require(lease.start(TimerArmResult::accepted(1000)), 11);
    require(!lease.start(TimerArmResult::accepted(1001)), 12);
    auto first = lease.arrive(1, 100, 2000, false);
    require(first && first->shouldWait() && first->requested() == 50000, 13);
    auto completed = lease.prepare(*first, 50003, TargetWaitStatus::completed, false);
    require(completed && completed->shouldRearm(), 14);
    require(lease.publish(*completed, 53000, TimerArmResult::accepted(52500), false), 15);
    require(lease.measured() == 50003 && lease.journal().size() == 1 && lease.deliveryPending(), 16);
    auto second = lease.arrive(1, 119, 54000, false);
    require(second && second->requested() == 50000, 17);
    require(!lease.prepare(*first, 1, TargetWaitStatus::completed, false), 18);
    auto next = lease.prepare(*second, 50002, TargetWaitStatus::completed, false);
    require(next && next->shouldRearm(), 19);
    require(!lease.publish(*completed, 105000, TimerArmResult::accepted(104500), false), 20);
    require(lease.publish(*next, 105000, TimerArmResult::accepted(104500), false), 21);
    auto third = lease.arrive(1, 100, 106000, false);
    require(third && third->requested() == 9995, 22);
    auto last = lease.prepare(*third, 10002, TargetWaitStatus::completed, false);
    require(last && !last->shouldRearm(), 23);
    require(lease.publish(*last, 117000, TimerArmResult::notRequested(), false), 24);
    require(lease.measured() == 110007 && lease.stop() == TargetLeaseStop::completed && lease.journal().size() == 3, 25);
    require(!lease.retire() && lease.quiesced() && lease.retire(), 26);
    require(!lease.start(TimerArmResult::accepted(118000)) && !lease.arrive(1, 100, 119000, false), 27);
}
static void cancellationAndRearm() {
    {
        std::array<TargetLeaseChild, 2> journal;
        TargetLease lease(plan(), journal);
        require(lease.start(TimerArmResult::accepted(1000)) && lease.cancelPending(), 28);
        require(lease.deliveryPending() && lease.stop() == TargetLeaseStop::cancelled && !lease.retire()
            && !lease.arrive(1, 100, 2000, false) && lease.journal().empty()
            && lease.quiesced() && lease.retire(), 29);
    }
    for (int stage = 0; stage != 3; ++stage) {
        std::array<TargetLeaseChild, 4> journal;
        TargetLease lease(plan(), journal);
        require(lease.start(TimerArmResult::accepted(1000)), 30);
        auto child = lease.arrive(1, 100, 2000, stage == 0);
        require(child && child->shouldWait() == (stage != 0), 31);
        const uint64_t waited = stage == 0 ? 0 : 50000;
        auto done = lease.prepare(*child, waited, TargetWaitStatus::completed, stage == 1);
        require(done && done->shouldRearm() == (stage == 2), 32);
        require(lease.publish(*done, 53000, stage == 2 ? TimerArmResult::accepted(52500) : TimerArmResult::notRequested(), stage == 2), 33);
        require(lease.stop() == TargetLeaseStop::cancelled && lease.measured() == waited, 34);
        require(lease.deliveryPending() == (stage == 2) && !lease.retire(), 35);
        require(!lease.arrive(1, 100, 54000, false), 36);
        require(lease.quiesced() && !lease.deliveryPending() && lease.retire(), 37);
    }
    std::array<TargetLeaseChild, 4> journal;
    TargetLease lease(plan(), journal);
    require(lease.start(TimerArmResult::accepted(1000)), 38);
    auto child = lease.arrive(1, 100, 2000, false);
    auto done = lease.prepare(*child, 50000, TargetWaitStatus::completed, false);
    require(lease.publish(*done, 53000, TimerArmResult::rejected(17), false), 39);
    require(lease.stop() == TargetLeaseStop::failed && lease.error() == TargetLeaseError::arm_failed
        && lease.transportError() == 17 && lease.measured() == 50000 && lease.journal()[0].valid, 40);
}
static void eligibilityAndFailures() {
    for (const auto ip : {99ULL, 120ULL}) {
        std::array<TargetLeaseChild, 2> journal;
        TargetLease lease(plan(), journal);
        lease.start(TimerArmResult::accepted(1000));
        auto child = lease.arrive(1, ip, 2000, false);
        require(child && !child->shouldWait(), 41);
        auto done = lease.prepare(*child, 0, TargetWaitStatus::completed, false);
        require(done && !done->shouldRearm() && lease.publish(*done, 2100, TimerArmResult::notRequested(), false), 42);
        require(lease.stop() == TargetLeaseStop::deferred && lease.measured() == 0 && lease.journal()[0].valid, 43);
    }
    for (int scenario = 0; scenario != 5; ++scenario) {
        std::array<TargetLeaseChild, 2> journal;
        TargetLease lease(plan(), journal);
        lease.start(TimerArmResult::accepted(1000));
        auto child = lease.arrive(1, 100, 2000, false);
        const uint64_t waited = scenario == 1 ? 5000001 : 50000;
        auto done = lease.prepare(*child, waited, TargetWaitStatus::completed, false);
        const uint64_t end = scenario == 0 ? 5002001 : scenario == 2 ? 1999 : scenario == 3 ? 51999 : 53000;
        const auto arm = done->shouldRearm() ? TimerArmResult::accepted(scenario == 4 ? 53001 : 52000) : TimerArmResult::notRequested();
        require(lease.publish(*done, end, arm, false), 44);
        require(lease.stop() == TargetLeaseStop::failed && lease.journal().size() == 1, 45);
        require(lease.measured() == (scenario == 4 ? 50000ULL : 0ULL), 46);
        require(!lease.retire() && lease.quiesced() && lease.retire(), 47);
    }
    std::array<TargetLeaseChild, 1> journal;
    TargetLease lease(plan(), journal);
    lease.start(TimerArmResult::accepted(1000));
    auto child = lease.arrive(1, 100, 2000, false);
    auto done = lease.prepare(*child, 50000, TargetWaitStatus::completed, false);
    require(done && !done->shouldRearm(), 48);
    require(lease.publish(*done, 53000, TimerArmResult::notRequested(), false), 49);
    require(lease.error() == TargetLeaseError::journal_full && lease.measured() == 50000, 50);
}
static void staleGeneration() {
    for (const auto identity : {std::pair<uint64_t, uint64_t>{2, 1}, {1, 2}}) {
        std::array<TargetLeaseChild, 2> journal;
        alignas(TargetLease) std::byte storage[sizeof(TargetLease)];
        auto* lease = std::construct_at(reinterpret_cast<TargetLease*>(storage), plan(), std::span(journal));
        lease->start(TimerArmResult::accepted(1000));
        require(!lease->arrive(2, 100, 2000, false) && lease->deliveryPending(), 51);
        auto stale = lease->arrive(1, 100, 2000, false);
        auto stale_completion = lease->prepare(*stale, 50000, TargetWaitStatus::completed, false);
        require(lease->publish(*stale_completion, 53000, TimerArmResult::rejected(1), false), 52);
        require(lease->quiesced() && lease->retire(), 53);
        std::destroy_at(lease);
        lease = std::construct_at(reinterpret_cast<TargetLease*>(storage), plan(110000, identity.first, identity.second), std::span(journal));
        lease->start(TimerArmResult::accepted(60000));
        require(!lease->arrive(identity.first + 1, 100, 61000, false) && lease->deliveryPending(), 54);
        auto current = lease->arrive(identity.first, 100, 61000, false);
        require(current && !lease->prepare(*stale, 50000, TargetWaitStatus::completed, false), 55);
        auto done = lease->prepare(*current, 50000, TargetWaitStatus::completed, false);
        require(!lease->publish(*stale_completion, 112000, TimerArmResult::accepted(111500), false), 56);
        require(lease->publish(*done, 112000, TimerArmResult::rejected(2), false), 57);
        require(lease->measured() == 50000 && lease->quiesced() && lease->retire(), 58);
        std::destroy_at(lease);
    }
}
static void terminalEvidence() {
    for (const auto status : {TargetWaitStatus::interrupted, TargetWaitStatus::failed}) {
        std::array<TargetLeaseChild, 2> journal;
        TargetLease lease(plan(), journal);
        require(lease.start(TimerArmResult::accepted(1000)) && !lease.quiesced() && !lease.retire(), 59);
        auto child = lease.arrive(1, 100, 2000, false);
        auto done = lease.prepare(*child, 12345, status, false);
        require(done && !done->shouldRearm() && !lease.cancelPending(), 60);
        require(lease.publish(*done, 15000, TimerArmResult::notRequested(), false), 61);
        const bool interrupted = status == TargetWaitStatus::interrupted;
        require(lease.measured() == (interrupted ? 12345ULL : 0ULL)
            && lease.journal()[0].waited == 12345 && lease.journal()[0].valid == interrupted, 62);
        require(lease.stop() == (interrupted ? TargetLeaseStop::interrupted : TargetLeaseStop::failed), 63);
    }
    for (const auto arm : {TimerArmResult::rejected(23), TimerArmResult::accepted(0)}) {
        std::array<TargetLeaseChild, 2> journal;
        TargetLease lease(plan(), journal);
        require(lease.start(arm) && lease.stop() == TargetLeaseStop::failed, 64);
        require(lease.deliveryPending() == arm.pending() && !lease.retire(), 65);
        require(lease.quiesced() && lease.retire(), 66);
    }
    std::array<TargetLeaseChild, 2> journal;
    TargetLease lease(plan(1), journal);
    lease.start(TimerArmResult::accepted(1000));
    auto child = lease.arrive(1, 100, 2000, false);
    auto done = lease.prepare(*child, 5000000, TargetWaitStatus::completed, false);
    require(done && !done->shouldRearm() && lease.publish(*done, 5002000, TimerArmResult::notRequested(), false), 67);
    require(lease.stop() == TargetLeaseStop::completed && lease.measured() == 5000000 && lease.journal()[0].valid, 68);
    TargetLease empty(plan(), {});
    require(empty.error() == TargetLeaseError::journal_full && !empty.start(TimerArmResult::accepted(1000)), 69);
}
template<class Predicate> static void await(Predicate predicate) {
    const auto deadline = std::chrono::steady_clock::now() + std::chrono::seconds(2);
    while (!predicate()) {
        require(std::chrono::steady_clock::now() < deadline, 70);
        std::this_thread::yield();
    }
}
static void snapshotBoundaries() {
    std::array<TargetLeaseChild, 4> journal;
    TargetLease lease(plan(), journal);
    std::array<TargetLeaseChild, 4> copy{};
    std::size_t required = 99;
    lease.start(TimerArmResult::accepted(1000));
    auto child = lease.arrive(1, 100, 2000, false);
    auto done = lease.prepare(*child, 50000, TargetWaitStatus::completed, false);
    auto before = lease.snapshot(copy, required);
    require(before && required == 0 && before->children == 0 && before->measured == 0, 71);
    lease.publish(*done, 53000, TimerArmResult::accepted(52500), false);
    copy[0].ordinal = 999;
    require(!lease.snapshot(std::span(copy).first(0), required) && required == 1 && copy[0].ordinal == 999, 72);
    require(!lease.snapshot(journal, required) && required == 1, 73);
    require(!lease.snapshot(std::span(journal).subspan(1), required), 74);
    auto prefix = lease.snapshot(copy, required);
    require(prefix && prefix->visibility == TargetLeaseVisibility::active && prefix->children == 1
        && prefix->generation == 1 && prefix->command == 1 && prefix->budget == 110000 && prefix->measured == 50000, 75);
    const auto first = copy[0];
    auto second = lease.arrive(1, 100, 54000, false);
    auto next = lease.prepare(*second, 50000, TargetWaitStatus::completed, false);
    lease.publish(*next, 105000, TimerArmResult::accepted(104500), true);
    auto terminal = lease.snapshot(copy, required);
    require(terminal && terminal->visibility == TargetLeaseVisibility::terminal && terminal->children == 2
        && terminal->measured == 100000 && terminal->stop == TargetLeaseStop::cancelled && terminal->may_have_delivery
        && copy[0] == first, 76);
    lease.quiesced();
    auto quiet = lease.snapshot(copy, required);
    require(quiet && quiet->visibility == TargetLeaseVisibility::quiesced && !quiet->may_have_delivery
        && quiet->children == 2 && quiet->measured == 100000, 77);
    lease.retire();
    auto retired = lease.snapshot(copy, required);
    require(retired && retired->visibility == TargetLeaseVisibility::retired && !retired->may_have_delivery
        && retired->stop == TargetLeaseStop::cancelled && copy[0] == first, 78);
}
static void concurrentSnapshots() {
    std::array<TargetLeaseChild, 256> journal;
    TargetLease lease(plan(2000000), journal);
    std::atomic<unsigned> stage{}, permit{};
    std::thread writer([&] {
        require(lease.start(TimerArmResult::accepted(1000)), 79);
        for (uint64_t ordinal = 1; ordinal <= 200; ++ordinal) {
            const uint64_t started = ordinal * 12000;
            auto child = lease.arrive(1, 100, started, false);
            require(child.has_value(), 80);
            auto done = lease.prepare(*child, 10000, TargetWaitStatus::completed, false);
            require(done && done->shouldRearm() == (ordinal != 200), 81);
            if (ordinal == 1) { stage.store(1, std::memory_order_release); await([&] { return permit.load(std::memory_order_acquire) >= 1; }); }
            require(lease.publish(*done, started + 10020,
                ordinal == 200 ? TimerArmResult::notRequested() : TimerArmResult::accepted(started + 10010), false), 82);
            if (ordinal == 1) { stage.store(2, std::memory_order_release); await([&] { return permit.load(std::memory_order_acquire) >= 2; }); }
        }
        await([&] { return permit.load(std::memory_order_acquire) >= 3; });
        require(lease.quiesced(), 83);
        stage.store(3, std::memory_order_release);
        await([&] { return permit.load(std::memory_order_acquire) >= 4; });
        require(lease.retire(), 84);
    });
    std::thread second_reader([&] {
        std::array<TargetLeaseChild, 256> previous{}, current{};
        std::size_t last = 0, required = 0;
        TargetLeaseVisibility last_visibility = TargetLeaseVisibility::active;
        await([&] {
            auto snapshot = lease.snapshot(current, required);
            require(snapshot && required >= last && snapshot->children == required && snapshot->visibility >= last_visibility, 85);
            uint64_t total = 0;
            for (std::size_t index = 0; index < required; ++index) {
                require(current[index].valid && current[index].ordinal == index + 1, 86);
                if (index < last) require(current[index] == previous[index], 87);
                total += current[index].waited;
                previous[index] = current[index];
            }
            require(total == snapshot->measured, 88);
            last = required;
            last_visibility = snapshot->visibility;
            return snapshot->visibility == TargetLeaseVisibility::retired;
        });
        require(last == 200, 89);
    });
    std::array<TargetLeaseChild, 256> copy;
    std::size_t required = 0;
    await([&] { return stage.load(std::memory_order_acquire) == 1; });
    auto unpublished = lease.snapshot(copy, required);
    require(unpublished && required == 0 && unpublished->measured == 0, 90);
    permit.store(1, std::memory_order_release);
    await([&] { return stage.load(std::memory_order_acquire) == 2; });
    auto first = lease.snapshot(copy, required);
    require(first && required == 1 && first->measured == 10000 && copy[0].valid, 91);
    permit.store(2, std::memory_order_release);
    await([&] {
        auto snapshot = lease.snapshot(copy, required);
        require(snapshot.has_value(), 92);
        if (snapshot->visibility == TargetLeaseVisibility::active) return false;
        require(snapshot->visibility == TargetLeaseVisibility::terminal && required == 200
            && snapshot->measured == 2000000 && snapshot->stop == TargetLeaseStop::completed, 93);
        return true;
    });
    permit.store(3, std::memory_order_release);
    await([&] { return stage.load(std::memory_order_acquire) == 3; });
    auto quiet = lease.snapshot(copy, required);
    require(quiet && quiet->visibility == TargetLeaseVisibility::quiesced && !quiet->may_have_delivery, 94);
    permit.store(4, std::memory_order_release);
    writer.join();
    second_reader.join();
}
int main() {
    boundaries();
    settlement();
    cancellationAndRearm();
    eligibilityAndFailures();
    staleGeneration();
    terminalEvidence();
    snapshotBoundaries();
    concurrentSnapshots();
}
