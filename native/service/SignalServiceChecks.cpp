#include "SignalTransport.Linux.h"
#include "../signal/Signal.h"
#include <atomic>
#include <cerrno>
#include <chrono>
#include <csignal>
#include <cstdlib>
#include <fstream>
#include <stdexcept>
#include <string>
#include <string_view>
#include <utility>
#include <pthread.h>

using namespace coz::service;
extern "C" void coz_service_workload(std::atomic<uint32_t>*, std::atomic<uint32_t>*);
extern "C" char coz_service_workload_begin, coz_service_workload_end;
static_assert(sizeof(std::atomic<uint32_t>) == sizeof(uint32_t));
static_assert(std::atomic<uint32_t>::is_always_lock_free);

static std::string evidence;
static void require(bool value, int code) { if (!value) std::_Exit(code); }

static void save(const Snapshot& value) {
    std::ofstream file(evidence, std::ios::trunc);
    file << "phase=" << static_cast<int>(value.phase) << " error=" << static_cast<int>(value.error)
         << " transport_error=" << value.transport_error << " outstanding=" << value.outstanding
         << " cancellation=" << value.cancellation << " budget=" << value.budget << " measured=" << value.measured << '\n';
    file << "ordinal\trequested\tsequence\toutcome\tip\thandler_ns\twait_ns\twait_result\tstarted\tqueued\thandler_started\thandler_finished\tobserved\n";
    for (const auto& child : value.children) {
        const auto& ack = child.acknowledgment;
        file << child.ordinal << '\t' << child.requested << '\t' << ack.sequence << '\t' << ack.outcome << '\t' << ack.ip
             << '\t' << ack.handler_ns << '\t' << ack.wait_ns << '\t' << ack.wait_result << '\t' << ack.started << '\t' << ack.queued
             << '\t' << ack.handler_started << '\t' << ack.handler_finished << '\t' << ack.observed << '\n';
    }
    file.flush();
    require(file.good(), 90);
}

template<class Predicate> static void await(Predicate predicate) {
    const auto deadline = std::chrono::steady_clock::now() + std::chrono::seconds(2);
    while (!predicate()) {
        require(std::chrono::steady_clock::now() < deadline, 91);
        std::this_thread::yield();
    }
}

template<class Predicate> static Snapshot observe(Owner& owner, int slot, Predicate predicate) {
    Snapshot value;
    const auto deadline = std::chrono::steady_clock::now() + std::chrono::seconds(2);
    do {
        require(owner.snapshot(slot, {1, 2}, value), 92);
        if (predicate(value)) return value;
        std::this_thread::yield();
    } while (std::chrono::steady_clock::now() < deadline);
    save(value);
    std::_Exit(93);
}

static Snapshot terminal(Owner& owner, int slot) {
    auto value = observe(owner, slot, [](const Snapshot& state) { return state.phase != Phase::active && !state.outstanding; });
    save(value);
    return value;
}

static void restored() {
    struct sigaction action{};
    require(sigaction(SIGRTMIN + 6, nullptr, &action) == 0 && action.sa_handler == SIG_DFL, 94);
}

static void lease(SignalWaitMode mode, int slot) {
    const auto begin = reinterpret_cast<uint64_t>(&coz_service_workload_begin);
    const auto end = reinterpret_cast<uint64_t>(&coz_service_workload_end);
    require(end > begin, 1);
    LinuxSignalTransport transport(InstructionRange(begin, end - begin), mode);
    require(transport.eligible(begin) && transport.eligible(end - 1) && !transport.eligible(end), 2);
    Owner owner(transport);
    require(owner.start() == 0, 3);
    std::atomic<uint32_t> stop{0}, entered{0};
    std::thread target([&] {
        require(transport.registerTarget(slot) == 0, 4);
        coz_service_workload(&stop, &entered);
        require(transport.retireTarget(slot) == 0, 5);
    });
    await([&] { return entered.load() == 1; });
    uint64_t sequence{};
    SignalResult result{};
    SignalTiming timing{};
    require(coz_signal_request(slot, 1, &sequence) == EINVAL && coz_signal_read_timed(slot, 1, &result, &timing) == EINVAL, 6);
    require(coz_signal_restore() == EINVAL, 7);
    const uint64_t budget = slot == 0 ? 50000 : 110000;
    require(owner.submit(slot, {1, 2}, 1, budget), 8);
    const auto done = terminal(owner, slot);
    require(done.phase == Phase::completed && done.error == Error::none && done.rejected.empty() && !done.children.empty(), 9);
    require(slot == 0 ? done.children.size() == 1 && done.measured > 0 : done.measured >= budget, 10);
    uint64_t measured = 0, previous = 0;
    for (const auto& child : done.children) {
        require(child.requested > 0 && child.requested <= 50000 && child.acknowledgment.sequence > previous, 11);
        require(child.acknowledgment.outcome == 2 && transport.eligible(child.acknowledgment.ip), 12);
        measured += child.acknowledgment.wait_ns;
        previous = child.acknowledgment.sequence;
    }
    require(measured == done.measured && owner.release(slot, {1, 2}), 13);
    stop = 1;
    target.join();
    require(owner.shutdown() == 0, 14);
    restored();
}

static void pendingCancellation() {
    LinuxSignalTransport transport(InstructionRange(1, 1), SignalWaitMode::spin);
    Owner owner(transport);
    require(owner.start() == 0, 20);
    std::atomic<int> phase{0};
    std::thread target([&] {
        require(transport.registerTarget(1) == 0, 21);
        sigset_t mask;
        sigemptyset(&mask);
        sigaddset(&mask, SIGRTMIN + 6);
        require(pthread_sigmask(SIG_BLOCK, &mask, nullptr) == 0, 22);
        phase = 1;
        await([&] { return phase.load() == 2; });
        require(transport.retireTarget(1) == EBUSY, 23);
        phase = 3;
        await([&] { return phase.load() == 4; });
        require(pthread_sigmask(SIG_UNBLOCK, &mask, nullptr) == 0, 24);
        phase = 5;
        await([&] { return phase.load() == 6; });
        require(transport.retireTarget(1) == 0, 25);
    });
    await([&] { return phase.load() == 1; });
    require(owner.submit(1, {1, 2}, 1, 110000), 26);
    const auto pending = observe(owner, 1, [](const Snapshot& value) { return value.outstanding; });
    require(pending.children.empty() && owner.cancel(1, {1, 2}) && !owner.release(1, {1, 2}), 27);
    require(owner.shutdown() == EBUSY, 28);
    phase = 2;
    await([&] { return phase.load() == 3; });
    phase = 4;
    const auto done = terminal(owner, 1);
    require(done.phase == Phase::cancelled && done.error == Error::none && done.children.size() == 1 && done.measured == 0, 29);
    require(done.children[0].acknowledgment.outcome == 1 && owner.release(1, {1, 2}), 30);
    await([&] { return phase.load() == 5; });
    phase = 6;
    target.join();
    require(owner.shutdown() == 0, 31);
    restored();
}

static void liveTargetBlocksRestoration() {
    LinuxSignalTransport transport(InstructionRange(1, 1), SignalWaitMode::spin);
    Owner owner(transport);
    require(owner.start() == 0, 40);
    std::atomic<int> phase{0};
    std::thread target([&] {
        require(transport.registerTarget(0) == 0, 41);
        phase = 1;
        await([&] { return phase.load() == 2; });
        require(transport.retireTarget(0) == 0, 42);
    });
    await([&] { return phase.load() == 1; });
    require(owner.shutdown() == EBUSY && !owner.submit(0, {1, 2}, 1, 1), 43);
    phase = 2;
    target.join();
    require(owner.shutdown() == 0, 44);
    restored();
}

static void occupiedSignalRemainsUnchanged() {
    struct sigaction action{};
    action.sa_handler = SIG_IGN;
    sigemptyset(&action.sa_mask);
    require(sigaction(SIGRTMIN + 6, &action, nullptr) == 0, 50);
    LinuxSignalTransport transport(InstructionRange(1, 1), SignalWaitMode::spin);
    Owner owner(transport);
    require(owner.start() == EBUSY, 51);
    require(sigaction(SIGRTMIN + 6, nullptr, &action) == 0 && action.sa_handler == SIG_IGN, 52);
}

static void parsedRangeAndMode() {
    for (const auto& values : {std::pair<uint64_t, uint64_t>{0, 1}, {1, 0}, {UINT64_MAX, 1}}) {
        bool rejected = false;
        try { InstructionRange range(values.first, values.second); }
        catch (const std::invalid_argument&) { rejected = true; }
        require(rejected, 60);
    }
    bool rejected = false;
    try { LinuxSignalTransport transport(InstructionRange(1, 1), static_cast<SignalWaitMode>(2)); }
    catch (const std::invalid_argument&) { rejected = true; }
    require(rejected, 61);
}

int main(int argc, char** argv) {
    require(argc == 3, 80);
    const std::string_view mode{argv[1]};
    evidence = argv[2];
    if (mode == "lease-spin") lease(SignalWaitMode::spin, 1);
    else if (mode == "lease-sleep") lease(SignalWaitMode::sleep, 1);
    else if (mode == "selected") lease(SignalWaitMode::spin, 0);
    else if (mode == "pending-cancel") pendingCancellation();
    else if (mode == "restore-live") liveTargetBlocksRestoration();
    else if (mode == "occupied") occupiedSignalRemainsUnchanged();
    else if (mode == "range") parsedRangeAndMode();
    else require(false, 81);
    return 0;
}
