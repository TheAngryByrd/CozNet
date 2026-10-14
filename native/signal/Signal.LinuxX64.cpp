#include <atomic>
#include <cerrno>
#include <cstdint>
#include <csignal>
#include <ctime>
#include <sys/syscall.h>
#include <ucontext.h>
#include <unistd.h>
#include "Signal.h"

namespace {
static_assert(sizeof(SignalResult) == 48);
static_assert(sizeof(SignalTiming) == 40);
static_assert(sizeof(sigval_t) == sizeof(uint64_t));
static_assert(std::atomic<uint64_t>::is_always_lock_free);
enum class State : uint64_t { unused, ready, publishing, pending, complete, retired };
struct Slot {
    std::atomic<State> state{State::unused};
    int tid{};
    uint64_t begin{}, end{}, sequence{}, delay_ns{};
    uint64_t request_started_ns{}, queue_returned_ns{}, handler_started_ns{}, handler_finished_ns{};
    SignalResult result{};
};
static_assert(std::atomic<State>::is_always_lock_free);
Slot slots[2];
int signal_number{}, process_id{}, owner_tid{};
uid_t owner_uid{};
bool installed{}, used{};
uint64_t next_sequence{};
struct sigaction previous_action{};

inline long direct_call(long number, long a = 0, long b = 0, long c = 0, long d = 0) {
    register long fourth asm("r10") = d;
    long result;
    asm volatile("syscall" : "=a"(result) : "a"(number), "D"(a), "S"(b), "d"(c), "r"(fourth) : "rcx", "r11", "memory");
    return result;
}

inline uint64_t monotonic() {
    timespec time{};
    if (direct_call(SYS_clock_gettime, CLOCK_MONOTONIC, reinterpret_cast<long>(&time)) != 0) return 0;
    return static_cast<uint64_t>(time.tv_sec) * 1000000000ULL + time.tv_nsec;
}

void handler(int number, siginfo_t* info, void* context) {
    if (number != signal_number || info->si_code != SI_QUEUE || info->si_pid != process_id || info->si_uid != owner_uid) return;
    const auto tid = direct_call(SYS_gettid);
    const auto sequence = reinterpret_cast<uint64_t>(info->si_value.sival_ptr);
    for (auto& slot : slots) {
        if (slot.state.load(std::memory_order_acquire) != State::pending || slot.tid != tid || slot.sequence != sequence) continue;
        const auto start = monotonic();
        const auto ip = static_cast<uint64_t>(static_cast<ucontext_t*>(context)->uc_mcontext.gregs[REG_RIP]);
        SignalResult result{sequence, 1, ip, 0, 0, 0};
        if (start == 0) result.outcome = 4;
        else if (ip >= slot.begin && ip < slot.end) {
            result.outcome = 2;
            if (slot.delay_ns != 0) {
                const auto deadline_ns = start + slot.delay_ns;
                timespec deadline{static_cast<time_t>(deadline_ns / 1000000000ULL), static_cast<long>(deadline_ns % 1000000000ULL)};
                const auto before_wait = monotonic();
                result.wait_result = direct_call(SYS_clock_nanosleep, CLOCK_MONOTONIC, TIMER_ABSTIME, reinterpret_cast<long>(&deadline), 0);
                const auto after_wait = monotonic();
                if (before_wait == 0 || after_wait < before_wait) result.outcome = 4;
                else result.wait_ns = after_wait - before_wait;
                if (result.wait_result != 0 && result.wait_result != -EINTR) result.outcome = 4;
                else if (result.wait_result == -EINTR) result.outcome = 3;
            }
        }
        const auto end = monotonic();
        if (end < start || end == 0) result.outcome = 4;
        else result.residence_ns = end - start;
        slot.handler_started_ns = start;
        slot.handler_finished_ns = end;
        slot.result = result;
        slot.state.store(State::complete, std::memory_order_release);
        return;
    }
}

bool controller() { return installed && direct_call(SYS_gettid) == owner_tid; }
}

EXPORT int coz_signal_install() {
    if (used) return EALREADY;
    const int candidate = SIGRTMIN + 6;
    if (candidate > SIGRTMAX) return ENOTSUP;
    if (sigaction(candidate, nullptr, &previous_action) != 0) return errno;
    if (previous_action.sa_handler != SIG_DFL) return EBUSY;
    signal_number = candidate;
    process_id = getpid();
    owner_uid = getuid();
    owner_tid = static_cast<int>(direct_call(SYS_gettid));
    struct sigaction action{};
    action.sa_sigaction = handler;
    action.sa_flags = SA_SIGINFO;
    sigemptyset(&action.sa_mask);
    if (sigaction(candidate, &action, nullptr) != 0) return errno;
    installed = used = true;
    return 0;
}

EXPORT int coz_signal_register(int index, uint64_t begin, uint64_t size) {
    if (!installed || index < 0 || index >= 2 || begin == 0 || size == 0 || size > UINT64_MAX - begin) return EINVAL;
    sigset_t mask;
    if (sigprocmask(SIG_SETMASK, nullptr, &mask) != 0) return errno;
    if (sigismember(&mask, signal_number) != 0) return EACCES;
    auto& slot = slots[index];
    auto expected = State::unused;
    if (!slot.state.compare_exchange_strong(expected, State::publishing)) return EBUSY;
    slot.tid = static_cast<int>(direct_call(SYS_gettid));
    slot.begin = begin;
    slot.end = begin + size;
    slot.state.store(State::ready, std::memory_order_release);
    return 0;
}

EXPORT int coz_signal_request(int index, uint64_t delay_ns, uint64_t* sequence) {
    if (!controller() || index < 0 || index >= 2 || delay_ns > 50000 || sequence == nullptr || next_sequence == UINT64_MAX) return EINVAL;
    auto& slot = slots[index];
    auto expected = State::ready;
    if (!slot.state.compare_exchange_strong(expected, State::publishing, std::memory_order_acquire)) return EBUSY;
    slot.sequence = ++next_sequence;
    slot.delay_ns = delay_ns;
    siginfo_t info{};
    info.si_signo = signal_number;
    info.si_code = SI_QUEUE;
    info.si_pid = process_id;
    info.si_uid = owner_uid;
    info.si_value.sival_ptr = reinterpret_cast<void*>(slot.sequence);
    slot.request_started_ns = monotonic();
    if (slot.request_started_ns == 0) {
        slot.state.store(State::ready, std::memory_order_release);
        return EIO;
    }
    slot.state.store(State::pending, std::memory_order_release);
    auto result = direct_call(SYS_rt_tgsigqueueinfo, process_id, slot.tid, signal_number, reinterpret_cast<long>(&info));
    slot.queue_returned_ns = monotonic();
    if (result != 0) {
        slot.state.store(State::ready, std::memory_order_release);
        return static_cast<int>(-result);
    }
    *sequence = slot.sequence;
    return 0;
}

static int read_result(int index, uint64_t sequence, SignalResult* result, SignalTiming* timing) {
    if (!controller() || index < 0 || index >= 2 || result == nullptr) return EINVAL;
    auto& slot = slots[index];
    auto state = slot.state.load(std::memory_order_acquire);
    if (state != State::pending && state != State::complete) return EINVAL;
    if (slot.sequence != sequence) return EINVAL;
    if (state == State::pending) return EAGAIN;
    if (timing != nullptr) {
        *timing = {slot.request_started_ns, slot.queue_returned_ns, slot.handler_started_ns,
            slot.handler_finished_ns, monotonic()};
    }
    *result = slot.result;
    slot.state.store(State::ready, std::memory_order_release);
    return 0;
}
EXPORT int coz_signal_read(int index, uint64_t sequence, SignalResult* result) {
    return read_result(index, sequence, result, nullptr);
}
EXPORT int coz_signal_read_timed(int index, uint64_t sequence, SignalResult* result, SignalTiming* timing) {
    if (timing == nullptr) return EINVAL;
    return read_result(index, sequence, result, timing);
}

EXPORT int coz_signal_retire(int index) {
    if (!installed || index < 0 || index >= 2) return EINVAL;
    auto& slot = slots[index];
    if (slot.tid != direct_call(SYS_gettid)) return EINVAL;
    if (slot.state.load(std::memory_order_acquire) != State::ready) return EBUSY;
    sigset_t mask;
    sigemptyset(&mask);
    sigaddset(&mask, signal_number);
    if (sigprocmask(SIG_BLOCK, &mask, nullptr) != 0) return errno;
    timespec immediate{};
    siginfo_t pending{};
    int received = sigtimedwait(&mask, &pending, &immediate);
    if (received >= 0) return EBUSY;
    if (errno != EAGAIN) return errno;
    auto expected = State::ready;
    return slot.state.compare_exchange_strong(expected, State::retired) ? 0 : EBUSY;
}

EXPORT int coz_signal_restore() {
    if (!controller()) return EINVAL;
    for (auto& slot : slots) {
        auto state = slot.state.load(std::memory_order_acquire);
        if (state != State::retired && state != State::unused) return EBUSY;
    }
    struct sigaction current{};
    if (sigaction(signal_number, nullptr, &current) != 0) return errno;
    if (!(current.sa_flags & SA_SIGINFO) || current.sa_sigaction != handler) return EBUSY;
    if (sigaction(signal_number, &previous_action, nullptr) != 0) return errno;
    installed = false;
    return 0;
}
