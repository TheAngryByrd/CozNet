#include "TargetLease.h"
#include <array>
#include <atomic>
#include <cerrno>
#include <chrono>
#include <csignal>
#include <cstdint>
#include <fstream>
#include <string_view>
#include <thread>
#include <sys/syscall.h>
#include <ucontext.h>
#include <unistd.h>

extern "C" void coz_timer_workload(std::atomic<uint32_t>*, std::atomic<uint32_t>*, std::atomic<uint64_t>*);
extern "C" char coz_timer_workload_begin, coz_timer_workload_end;
static_assert(std::atomic<uint32_t>::is_always_lock_free);
static_assert(std::atomic<uint64_t>::is_always_lock_free);
static_assert(sizeof(std::atomic<uint32_t>) == sizeof(uint32_t));
static_assert(sizeof(std::atomic<uint64_t>) == sizeof(uint64_t));

namespace {
using namespace coz::service;
constexpr uint64_t budget = 500000, maximum_wait = 50000, interval = 10000;
constexpr uint64_t cookie = 0x436f7a54696d6572ULL;
std::array<TargetLeaseChild, 128> children{};
std::array<uint64_t, 128> child_progress{}, first_receipts{};
uint64_t snapshot_reads{};
bool snapshot_consistent{true};
TargetLease* lease{};
std::atomic<uint32_t> stop{}, entered{}, target_ready{}, cancel{}, terminal{};
std::atomic<uint64_t> progress{};
uint64_t observed{};
std::atomic<uint64_t> armed{};
int timer_id{}, signal_number{}, handler_error{}, target_error{}, drained{};
bool timer_created{}, timer_deleted{}, restored{}, pending_seen{};
bool cancel_rearm{}, rearm_pending_seen{}, disarmed{}, drain_empty{};
int rearm_drained{}, cancellation_error{};
uint64_t cancellation_started{}, cancellation_finished{};
siginfo_t cancellation_record{};

inline long direct(long number, long a = 0, long b = 0, long c = 0, long d = 0) {
    register long fourth asm("r10") = d;
    long result;
    asm volatile("syscall" : "=a"(result) : "a"(number), "D"(a), "S"(b), "d"(c), "r"(fourth)
        : "rcx", "r11", "memory");
    return result;
}
uint64_t now() {
    timespec value{};
    if (direct(SYS_clock_gettime, CLOCK_MONOTONIC, reinterpret_cast<long>(&value)) != 0) return 0;
    return static_cast<uint64_t>(value.tv_sec) * 1000000000ULL + value.tv_nsec;
}
int arm(uint64_t delay = interval) {
    itimerspec next{};
    next.it_value.tv_nsec = static_cast<long>(delay);
    const auto timestamp = now();
    if (timestamp == 0) return EIO;
    armed.store(timestamp, std::memory_order_release);
    return static_cast<int>(-direct(SYS_timer_settime, timer_id, 0, reinterpret_cast<long>(&next)));
}
void finish(int error) {
    handler_error = error;
    terminal.store(1, std::memory_order_release);
}
int cancelRearmedTimer() {
    cancellation_started = now();
    if (cancellation_started == 0) return EIO;
    const uint64_t mask = uint64_t{1} << (signal_number - 1);
    for (unsigned attempt = 0; attempt < 100000; ++attempt) {
        uint64_t pending = 0;
        const auto result = direct(SYS_rt_sigpending, reinterpret_cast<long>(&pending), sizeof(pending));
        if (result < 0) return static_cast<int>(-result);
        if ((pending & mask) != 0) { rearm_pending_seen = true; break; }
        const auto timestamp = now();
        if (timestamp == 0 || timestamp < cancellation_started) return EIO;
        if (timestamp - cancellation_started > 2000000) return ETIMEDOUT;
    }
    if (!rearm_pending_seen) return ETIMEDOUT;
    itimerspec disabled{};
    const auto result = direct(SYS_timer_settime, timer_id, 0, reinterpret_cast<long>(&disabled));
    if (result < 0) return static_cast<int>(-result);
    disarmed = true;
    timespec immediate{};
    for (unsigned attempt = 0; attempt < 8; ++attempt) {
        siginfo_t info{};
        const auto received = direct(SYS_rt_sigtimedwait, reinterpret_cast<long>(&mask),
            reinterpret_cast<long>(&info), reinterpret_cast<long>(&immediate), sizeof(mask));
        if (received == -EINTR) continue;
        if (received == -EAGAIN) { drain_empty = true; return 0; }
        if (received < 0) return static_cast<int>(-received);
        cancellation_record = info;
        ++rearm_drained;
        if (received != signal_number || info.si_code != SI_TIMER || info.si_timerid != timer_id
            || reinterpret_cast<uint64_t>(info.si_value.sival_ptr) != cookie) return EPROTO;
        if (rearm_drained != 1) return EOVERFLOW;
    }
    return EOVERFLOW;
}
void handler(int number, siginfo_t* info, void* context) {
    if (number != signal_number || info->si_code != SI_TIMER || info->si_timerid != timer_id
        || reinterpret_cast<uint64_t>(info->si_value.sival_ptr) != cookie) return;
    if (terminal.load(std::memory_order_relaxed) != 0) return;
    const auto start = now();
    const auto ip = static_cast<uint64_t>(static_cast<ucontext_t*>(context)->uc_mcontext.gregs[REG_RIP]);
    const auto admission = lease->arrive(cookie, ip, start, cancel.load(std::memory_order_acquire) != 0);
    if (!admission) {
        if (lease->phase() == TargetLeasePhase::terminal) finish(EIO);
        return;
    }
    const auto before = now();
    auto after = before;
    int error = before == 0 ? EIO : 0;
    for (unsigned iteration = 0; error == 0 && admission->shouldWait() && after - before < admission->requested(); ++iteration) {
        if (iteration == 4096) { error = ETIMEDOUT; break; }
        const auto next = now();
        if (next == 0 || next < after) { error = EIO; break; }
        after = next;
        asm volatile("pause");
    }
    const auto waited = after >= before ? after - before : 0;
    const auto completion = lease->prepare(*admission, waited,
        error == 0 ? TargetWaitStatus::completed : TargetWaitStatus::failed,
        cancel.load(std::memory_order_acquire) != 0);
    if (!completion) { finish(EPROTO); return; }
    child_progress[lease->journal().size()] = progress.load(std::memory_order_relaxed);
    auto next = TimerArmResult::notRequested();
    if (completion->shouldRearm()) {
        error = arm();
        next = error == 0 ? TimerArmResult::accepted(armed.load(std::memory_order_acquire)) : TimerArmResult::rejected(error);
        if (cancel_rearm && error == 0) {
            cancel.store(1, std::memory_order_release);
            cancellation_error = cancelRearmedTimer();
            cancellation_finished = now();
        }
    }
    if (!lease->publish(*completion, now(), next, cancel.load(std::memory_order_acquire) != 0)) { finish(EPROTO); return; }
    if (lease->phase() == TargetLeasePhase::terminal)
        finish(lease->error() == TargetLeaseError::none ? 0 : error != 0 ? error : EIO);
}
template<class Predicate> bool await(Predicate predicate) {
    const auto deadline = std::chrono::steady_clock::now() + std::chrono::seconds(2);
    while (!predicate()) {
        if (std::chrono::steady_clock::now() >= deadline) return false;
        std::this_thread::yield();
    }
    return true;
}
void retire(const sigset_t& mask) {
    const int blocked = pthread_sigmask(SIG_BLOCK, &mask, nullptr);
    if (blocked != 0) { target_error = blocked; return; }
    cancel.store(1, std::memory_order_release);
    if (lease->phase() == TargetLeasePhase::waiting) lease->cancelPending();
    if (syscall(SYS_timer_delete, timer_id) != 0) { target_error = errno; return; }
    timer_deleted = true;
    timespec immediate{};
    for (;;) {
        siginfo_t info{};
        const int result = sigtimedwait(&mask, &info, &immediate);
        if (result < 0) {
            if (errno != EAGAIN) target_error = errno;
            else if (!lease->quiesced() || !lease->retire()) target_error = EPROTO;
            return;
        }
        ++drained;
        if (result != signal_number || info.si_code != SI_TIMER || info.si_timerid != timer_id
            || reinterpret_cast<uint64_t>(info.si_value.sival_ptr) != cookie) {
            target_error = EPROTO;
            return;
        }
    }
}
}

int main(int argc, char** argv) {
    if (argc != 3) return 2;
    const bool pending = std::string_view(argv[1]) == "pending-retirement";
    const bool concurrent = std::string_view(argv[1]) == "snapshot-chain";
    cancel_rearm = std::string_view(argv[1]) == "cancel-rearm";
    if (!pending && !concurrent && !cancel_rearm && std::string_view(argv[1]) != "chain") return 2;
    signal_number = SIGRTMIN + 7;
    if (signal_number > SIGRTMAX) return 3;
    struct sigaction previous{}, action{};
    if (sigaction(signal_number, nullptr, &previous) != 0 || previous.sa_handler != SIG_DFL) return 4;
    action.sa_sigaction = handler;
    action.sa_flags = SA_SIGINFO;
    sigemptyset(&action.sa_mask);
    if (sigaction(signal_number, &action, nullptr) != 0) return 5;
    const auto plan = TargetLeasePlan::parse(cookie, 1, budget,
        reinterpret_cast<uint64_t>(&coz_timer_workload_begin),
        reinterpret_cast<uint64_t>(&coz_timer_workload_end) - reinterpret_cast<uint64_t>(&coz_timer_workload_begin));
    if (!plan) return 6;
    TargetLease state(*plan, children);
    lease = &state;
    int arm_error = 0;
    std::thread target([&] {
        sigset_t mask;
        sigemptyset(&mask);
        sigaddset(&mask, signal_number);
        target_error = pthread_sigmask(SIG_BLOCK, &mask, nullptr);
        if (target_error != 0) { target_ready.store(1, std::memory_order_release); return; }
        sigevent event{};
        event.sigev_notify = SIGEV_THREAD_ID;
        event.sigev_signo = signal_number;
        event.sigev_value.sival_ptr = reinterpret_cast<void*>(cookie);
        event._sigev_un._tid = static_cast<pid_t>(syscall(SYS_gettid));
        if (syscall(SYS_timer_create, CLOCK_MONOTONIC, &event, &timer_id) != 0) target_error = errno;
        else timer_created = true;
        if (timer_created) {
            arm_error = arm(1000000);
            state.start(arm_error == 0 ? TimerArmResult::accepted(armed.load(std::memory_order_acquire)) : TimerArmResult::rejected(arm_error));
        }
        target_ready.store(1, std::memory_order_release);
        if (!timer_created) return;
        if (pending) {
            pending_seen = await([&] {
                sigset_t signals;
                if (sigpending(&signals) != 0) return false;
                return sigismember(&signals, signal_number) == 1;
            });
            if (!await([&] { return stop.load(std::memory_order_acquire) != 0; })) target_error = ETIMEDOUT;
        } else {
            target_error = pthread_sigmask(SIG_UNBLOCK, &mask, nullptr);
            if (target_error == 0) coz_timer_workload(&stop, &entered, &progress);
        }
        retire(mask);
    });
    bool ready = await([&] { return target_ready.load(std::memory_order_acquire) != 0; });
    bool completed = false;
    if (ready && timer_created) {
        if (!pending) ready = await([&] { return entered.load(std::memory_order_acquire) != 0; });
        if (ready) {
            if (concurrent) {
                std::array<TargetLeaseChild, 128> previous{}, current{};
                std::size_t last = 0, required = 0;
                completed = await([&] {
                    const auto snapshot = state.snapshot(current, required);
                    const auto captured = now();
                    ++snapshot_reads;
                    if (!snapshot || required < last || captured == 0) { snapshot_consistent = false; return true; }
                    uint64_t total = 0;
                    for (std::size_t index = 0; index < required; ++index) {
                        if (index < last && current[index] != previous[index]) snapshot_consistent = false;
                        if (current[index].ordinal != index + 1) snapshot_consistent = false;
                        if (current[index].valid) total += current[index].waited;
                        if (index >= last) first_receipts[index] = captured;
                        previous[index] = current[index];
                    }
                    snapshot_consistent = snapshot_consistent && total == snapshot->measured
                        && snapshot->generation == cookie && snapshot->command == 1 && snapshot->budget == budget;
                    last = required;
                    return !snapshot_consistent || snapshot->visibility != TargetLeaseVisibility::active;
                });
                completed = completed && await([&] { return terminal.load(std::memory_order_acquire) != 0; });
            } else {
                std::this_thread::sleep_for(std::chrono::milliseconds(20));
                completed = pending || await([&] { return terminal.load(std::memory_order_acquire) != 0; });
            }
        }
    }
    cancel.store(1, std::memory_order_release);
    stop.store(1, std::memory_order_release);
    target.join();
    std::array<TargetLeaseChild, 128> final_copy;
    std::size_t copied = 0;
    const auto final_snapshot = state.snapshot(final_copy, copied);
    observed = now();
    ++snapshot_reads;
    snapshot_consistent = snapshot_consistent && observed != 0 && final_snapshot.has_value()
        && copied == state.journal().size() && final_snapshot->measured == state.measured();
    for (std::size_t index = 0; index < copied; ++index)
        if (first_receipts[index] == 0) first_receipts[index] = observed;
    if (!timer_created || timer_deleted) restored = sigaction(signal_number, &previous, nullptr) == 0;
    const auto count = state.journal().size();
    const auto measured = state.measured();
    bool valid = snapshot_consistent && state.phase() == TargetLeasePhase::retired && ready && arm_error == 0 && target_error == 0 && handler_error == 0 && completed && timer_deleted && restored;
    if (pending) valid = valid && pending_seen && count == 0;
    else if (cancel_rearm) valid = valid && count == 1 && measured >= maximum_wait && measured < budget
        && state.stop() == TargetLeaseStop::cancelled && rearm_pending_seen && disarmed && drain_empty
        && rearm_drained == 1 && drained == 0 && cancellation_error == 0
        && cancellation_started >= children[0].started && cancellation_finished >= cancellation_started
        && cancellation_finished <= children[0].finished;
    else valid = valid && count > 1 && measured >= budget;
    for (std::size_t i = 0; i < count; ++i) {
        const auto& child = children[i];
        valid = valid && child.requested <= maximum_wait && child.started >= child.armed
            && child.finished >= child.started && child.finished - child.started <= 5000000
            && child.waited <= child.finished - child.started && first_receipts[i] >= child.finished
            && first_receipts[i] - child.armed <= 100000000;
    }
    std::ofstream output(argv[2]);
    output << "{\"passed\":" << (valid ? "true" : "false") << ",\"mode\":\"" << argv[1]
        << "\",\"budgetNanoseconds\":" << budget << ",\"measuredNanoseconds\":" << measured
        << ",\"leasePhase\":" << static_cast<int>(state.phase()) << ",\"leaseStop\":" << static_cast<int>(state.stop())
        << ",\"leaseError\":" << static_cast<int>(state.error()) << ",\"observed\":" << observed << ",\"handlerError\":" << handler_error
        << ",\"snapshotReads\":" << snapshot_reads << ",\"snapshotConsistent\":" << (snapshot_consistent ? "true" : "false")
        << ",\"targetError\":" << target_error << ",\"armError\":" << arm_error
        << ",\"timerDeleted\":" << (timer_deleted ? "true" : "false")
        << ",\"restored\":" << (restored ? "true" : "false") << ",\"drained\":" << drained
        << ",\"rearmPendingSeen\":" << (rearm_pending_seen ? "true" : "false")
        << ",\"disarmed\":" << (disarmed ? "true" : "false") << ",\"drainEmpty\":" << (drain_empty ? "true" : "false")
        << ",\"rearmDrained\":" << rearm_drained << ",\"cancellationError\":" << cancellation_error
        << ",\"cancellationStarted\":" << cancellation_started << ",\"cancellationFinished\":" << cancellation_finished
        << ",\"drainedSignal\":" << cancellation_record.si_signo << ",\"drainedCode\":" << cancellation_record.si_code
        << ",\"drainedTimerId\":" << cancellation_record.si_timerid
        << ",\"drainedCookie\":" << reinterpret_cast<uint64_t>(cancellation_record.si_value.sival_ptr)
        << ",\"pendingSeen\":" << (pending_seen ? "true" : "false") << ",\"children\":[";
    for (std::size_t i = 0; i < count; ++i) {
        const auto& child = children[i];
        if (i != 0) output << ',';
        output << "{\"ordinal\":" << i + 1 << ",\"requested\":" << child.requested
            << ",\"armed\":" << child.armed << ",\"started\":" << child.started
            << ",\"finished\":" << child.finished << ",\"waited\":" << child.waited
            << ",\"ip\":" << child.ip << ",\"firstNativeReceipt\":" << first_receipts[i] << ",\"progress\":" << child_progress[i] << '}';
    }
    output << "]}\n";
    output.flush();
    return valid && output.good() ? 0 : 1;
}
