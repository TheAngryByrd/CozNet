#include "TimerApi.h"
#include "LeaseMailbox.h"
#include "TargetLease.h"
#include "TimerJournalSnapshot.h"
#include <cerrno>
#include <csignal>
#include <cstddef>
#include <memory>
#include <mutex>
#include <thread>
#include <vector>
#include <sys/syscall.h>
#include <ucontext.h>
#include <unistd.h>

using namespace coz::service;
namespace {
static_assert(sizeof(CozTimerChild) == 72 && offsetof(CozTimerChild, valid) == 64);
static_assert(sizeof(CozTimerSnapshot) == 96 && offsetof(CozTimerSnapshot, backend_error) == 88);
inline long direct(long number, long a = 0, long b = 0, long c = 0, long d = 0) noexcept {
    register long fourth asm("r10") = d;
    long result;
    asm volatile("syscall" : "=a"(result) : "a"(number), "D"(a), "S"(b), "d"(c), "r"(fourth) : "rcx", "r11", "memory");
    return result;
}
uint64_t now() noexcept {
    timespec value{};
    if (direct(SYS_clock_gettime, CLOCK_MONOTONIC, reinterpret_cast<long>(&value)) != 0) return 0;
    return static_cast<uint64_t>(value.tv_sec) * 1000000000ULL + value.tv_nsec;
}
struct Target {
    LeaseMailbox mailbox;
    std::vector<TargetLeaseChild> journal, copy;
    std::optional<TargetLease> lease;
    std::optional<LeaseMailbox::Execution> execution;
    std::optional<LeaseMailbox::Submission> submission;
    std::atomic<uint32_t> announced{};
    std::atomic<int> backend_error{};
    std::thread::id thread;
    sigset_t original_mask{};
    int timer{};
    bool registered{}, ever_registered{}, timer_exists{}, outstanding{}, copied_retired{};
    uint32_t generation{};
    int initial_arm_error{};
    explicit Target(uint32_t capacity) : journal(capacity), copy(capacity) {}
};
}
struct CozTimerSession {
    uint64_t begin, size;
    uint64_t initial_interval_ns{1000000};
    int signal_number;
    struct sigaction previous{};
    std::thread::id controller{std::this_thread::get_id()};
    bool closing{};
    std::vector<std::unique_ptr<Target>> targets;
    CozTimerSession(uint64_t begin, uint64_t size, int signal_number, uint32_t count, uint32_t capacity)
        : begin(begin), size(size), signal_number(signal_number) {
        for (uint32_t index = 0; index < count; ++index) targets.push_back(std::make_unique<Target>(capacity));
    }
};
namespace {
std::mutex api_mutex;
CozTimerSession* active{};
std::atomic<CozTimerSession*> signal_session{};
std::atomic<uint32_t> active_handlers{};
bool closed{};
static_assert(std::atomic<CozTimerSession*>::is_always_lock_free && std::atomic<int>::is_always_lock_free);
void fail(Target& target, int error) noexcept {
    int clear = 0;
    target.backend_error.compare_exchange_strong(clear, error, std::memory_order_release);
}
int drain(CozTimerSession& session, Target& target) noexcept {
    const uint64_t mask = uint64_t{1} << (session.signal_number - 1);
    timespec immediate{};
    for (unsigned attempt = 0; attempt < 64; ++attempt) {
        siginfo_t info{};
        const auto result = direct(SYS_rt_sigtimedwait, reinterpret_cast<long>(&mask), reinterpret_cast<long>(&info),
            reinterpret_cast<long>(&immediate), sizeof(mask));
        if (result == -EINTR) continue;
        if (result == -EAGAIN) return 0;
        if (result < 0) return static_cast<int>(-result);
        if (result != session.signal_number || info.si_code != SI_TIMER || info.si_timerid != target.timer
            || info.si_value.sival_ptr != &target) return EPROTO;
    }
    return EOVERFLOW;
}
bool resolveDelivery(CozTimerSession& session, Target& target) noexcept {
    itimerspec disabled{};
    const auto result = direct(SYS_timer_settime, target.timer, 0, reinterpret_cast<long>(&disabled));
    if (result != 0) { fail(target, static_cast<int>(-result)); return false; }
    const int error = drain(session, target);
    if (error != 0) { fail(target, error); return false; }
    return true;
}
bool retireCore(Target& target) noexcept {
    if (!target.lease->quiesced() || !target.lease->retire()) { fail(target, EPROTO); return false; }
    const auto execution = *target.execution;
    target.execution.reset();
    const bool retired = target.mailbox.quiesce(execution);
    if (!retired) fail(target, EPROTO);
    return retired;
}
void deliver(CozTimerSession& session, Target& target, uint32_t generation, uint64_t ip, uint64_t started) noexcept {
    if (!target.execution) {
        target.execution = target.mailbox.claim(generation);
        if (!target.execution) { fail(target, EPROTO); return; }
    }
    const auto admission = target.lease->arrive(generation, ip, started, target.mailbox.cancelled(*target.execution));
    if (!admission) { fail(target, EPROTO); return; }
    const auto before = now();
    auto after = before;
    int error = before == 0 ? EIO : 0;
    for (unsigned attempt = 0; error == 0 && admission->shouldWait() && after - before < admission->requested(); ++attempt) {
        if (attempt == 4096) { error = ETIMEDOUT; break; }
        const auto next = now();
        if (next == 0 || next < after) { error = EIO; break; }
        after = next;
        asm volatile("pause");
    }
    const auto completion = target.lease->prepare(*admission, after >= before ? after - before : 0,
        error == 0 ? TargetWaitStatus::completed : TargetWaitStatus::failed, target.mailbox.cancelled(*target.execution));
    if (!completion) { fail(target, EPROTO); return; }
    TimerArmResult arm = TimerArmResult::notRequested();
    if (completion->shouldRearm()) {
        const auto armed = now();
        if (armed == 0) arm = TimerArmResult::rejected(EIO);
        else {
            itimerspec next{};
            next.it_value.tv_nsec = 10000;
            const auto result = direct(SYS_timer_settime, target.timer, 0, reinterpret_cast<long>(&next));
            arm = result == 0 ? TimerArmResult::accepted(armed) : TimerArmResult::rejected(static_cast<int>(-result));
        }
    }
    const bool cancelled = target.mailbox.cancelled(*target.execution);
    const bool stopping = !completion->shouldRearm() || !arm.pending() || cancelled;
    const bool cleaned = stopping && resolveDelivery(session, target);
    if (!target.lease->publish(*completion, now(), arm, cancelled)) {
        fail(target, EPROTO); return;
    }
    if (error != 0) fail(target, error);
    if (target.lease->phase() == TargetLeasePhase::terminal && (cleaned || (!stopping && resolveDelivery(session, target))))
        static_cast<void>(retireCore(target));
}
void handler(int number, siginfo_t* info, void* context) noexcept {
    const auto started = now();
    struct HandlerGuard {
        HandlerGuard() { active_handlers.fetch_add(1, std::memory_order_seq_cst); }
        ~HandlerGuard() { active_handlers.fetch_sub(1, std::memory_order_seq_cst); }
    } guard;
    auto* session = signal_session.load(std::memory_order_seq_cst);
    if (!session || number != session->signal_number || info->si_code != SI_TIMER) return;
    for (auto& item : session->targets) {
        if (info->si_value.sival_ptr != item.get()) continue;
        const auto generation = item->announced.load(std::memory_order_acquire);
        if (generation == 0) return;
        if (info->si_timerid != item->timer) { fail(*item, EPROTO); return; }
        const auto ip = static_cast<uint64_t>(static_cast<ucontext_t*>(context)->uc_mcontext.gregs[REG_RIP]);
        deliver(*session, *item, generation, ip, started);
        return;
    }
}
template<class Action> int32_t guarded(CozTimerSession* session, bool controller, Action action, bool closing_allowed = false) noexcept {
    try {
        std::lock_guard lock(api_mutex);
        if (!session || session != active) return EINVAL;
        if (controller && session->controller != std::this_thread::get_id()) return EPERM;
        if (session->closing && !closing_allowed) return EBUSY;
        return action(*session);
    } catch (const std::bad_alloc&) { return ENOMEM; }
    catch (...) { return EIO; }
}
Target* get(CozTimerSession& session, uint32_t slot) noexcept {
    return slot < session.targets.size() ? session.targets[slot].get() : nullptr;
}
}
int32_t coz_timer_open(uint64_t begin, uint64_t size, uint32_t count, uint32_t capacity, CozTimerSession** result) {
    if (!result) return EINVAL;
    *result = nullptr;
    try {
        std::lock_guard lock(api_mutex);
        if (active || closed) return EALREADY;
        if (!TargetLeasePlan::parse(1, 1, 1, begin, size) || count == 0 || count > 64 || capacity == 0
            || capacity > 100000 || count > 1000000 / capacity) return EINVAL;
        const int number = SIGRTMIN + 7;
        if (number > SIGRTMAX || number > 64) return ENOTSUP;
        auto session = std::make_unique<CozTimerSession>(begin, size, number, count, capacity);
        if (sigaction(number, nullptr, &session->previous) != 0) return errno;
        if (session->previous.sa_handler != SIG_DFL) return EBUSY;
        struct sigaction action{};
        action.sa_sigaction = handler;
        action.sa_flags = SA_SIGINFO;
        sigemptyset(&action.sa_mask);
        if (sigaction(number, &action, nullptr) != 0) return errno;
        active = session.release();
        signal_session.store(active, std::memory_order_release);
        *result = active;
        return 0;
    } catch (const std::bad_alloc&) { return ENOMEM; }
    catch (...) { return EIO; }
}
int32_t coz_timer_configure_initial_interval(CozTimerSession* session, uint64_t nanoseconds) {
    return guarded(session, true, [&](auto& value) {
        if (nanoseconds != 10000 && nanoseconds != 1000000) return EINVAL;
        for (const auto& target : value.targets) if (target->ever_registered) return EBUSY;
        value.initial_interval_ns = nanoseconds;
        return 0;
    });
}
int32_t coz_timer_register(CozTimerSession* session, uint32_t slot) {
    return guarded(session, false, [&](auto& value) {
        auto* target = get(value, slot);
        if (!target) return EINVAL;
        if (value.controller == std::this_thread::get_id()) return EPERM;
        if (target->ever_registered) return EALREADY;
        for (const auto& other : value.targets)
            if (other->registered && other->thread == std::this_thread::get_id()) return EALREADY;
        sigset_t mask;
        sigemptyset(&mask);
        sigaddset(&mask, value.signal_number);
        int error = pthread_sigmask(SIG_BLOCK, &mask, &target->original_mask);
        if (error != 0) return error;
        if (sigismember(&target->original_mask, value.signal_number) != 0) return EINVAL;
        sigevent event{};
        event.sigev_notify = SIGEV_THREAD_ID;
        event.sigev_signo = value.signal_number;
        event.sigev_value.sival_ptr = target;
        event._sigev_un._tid = static_cast<pid_t>(syscall(SYS_gettid));
        const auto created = direct(SYS_timer_create, CLOCK_MONOTONIC, reinterpret_cast<long>(&event), reinterpret_cast<long>(&target->timer));
        if (created != 0) {
            const auto restored = pthread_sigmask(SIG_SETMASK, &target->original_mask, nullptr);
            return restored == 0 ? static_cast<int>(-created) : restored;
        }
        target->thread = std::this_thread::get_id();
        target->registered = target->ever_registered = target->timer_exists = true;
        error = pthread_sigmask(SIG_SETMASK, &target->original_mask, nullptr);
        if (error != 0) fail(*target, error);
        return error;
    });
}
int32_t coz_timer_submit(CozTimerSession* session, uint32_t slot, uint64_t command, uint64_t budget, uint64_t* generation) {
    if (!generation) return EINVAL;
    *generation = 0;
    return guarded(session, true, [&](auto& value) {
        auto* target = get(value, slot);
        if (!target || !target->registered || !TargetLeasePlan::parse(1, command, budget, value.begin, value.size)) return EINVAL;
        if (target->outstanding || target->backend_error.load(std::memory_order_acquire) != 0) return EBUSY;
        const auto submitted = now();
        if (submitted == 0) return EIO;
        const auto submission = target->mailbox.begin();
        if (!submission) return EBUSY;
        target->submission = submission;
        target->generation = submission->generation();
        target->lease.emplace(*TargetLeasePlan::parse(target->generation, command, budget, value.begin, value.size), target->journal);
        target->outstanding = true;
        target->copied_retired = false;
        target->initial_arm_error = 0;
        *generation = target->generation;
        if (!target->lease->start(TimerArmResult::accepted(submitted))) return EPROTO;
        target->announced.store(target->generation, std::memory_order_release);
        if (!target->mailbox.publish(*submission)) return EPROTO;
        itimerspec next{};
        next.it_value.tv_nsec = value.initial_interval_ns;
        const auto result = direct(SYS_timer_settime, target->timer, 0, reinterpret_cast<long>(&next));
        if (result != 0) {
            target->initial_arm_error = static_cast<int>(-result);
            fail(*target, target->initial_arm_error);
        }
        return static_cast<int>(-result);
    });
}
int32_t coz_timer_cancel(CozTimerSession* session, uint32_t slot, uint64_t generation) {
    return guarded(session, true, [&](auto& value) {
        auto* target = get(value, slot);
        if (!target || !target->outstanding || generation != target->generation) return EINVAL;
        return target->mailbox.cancel(target->generation) ? 0 : EBUSY;
    });
}
int32_t coz_timer_snapshot(CozTimerSession* session, uint32_t slot, uint64_t generation,
    CozTimerSnapshot* result, CozTimerChild* children, uint32_t capacity) {
    if (!result || (capacity != 0 && !children) || capacity > 100000) return EINVAL;
    *result = {};
    return guarded(session, true, [&](auto& value) {
        auto* target = get(value, slot);
        if (!target || !target->outstanding || generation != target->generation) return EINVAL;
        target->copied_retired = false;
        auto reader = target->mailbox.read(target->generation);
        if (!reader) return EBUSY;
        std::size_t count = 0;
        const auto snapshot = captureTimerJournal(*target->lease, *reader, target->copy, count);
        if (!snapshot) return EPROTO;
        const auto& header = *snapshot;
        *result = {header.generation, header.command, header.budget, header.children, header.measured,
            static_cast<uint64_t>(header.visibility), static_cast<uint64_t>(header.stop), static_cast<uint64_t>(header.error),
            header.transport_error, header.may_have_delivery, 0, target->backend_error.load(std::memory_order_acquire)};
        if (count > capacity) {
            result->captured_ns = now();
            return result->captured_ns == 0 ? EIO : ENOBUFS;
        }
        for (std::size_t index = 0; index < count; ++index) {
            const auto& child = target->copy[index];
            children[index] = {child.ordinal, child.requested, child.armed, child.started, child.finished, child.waited, child.ip,
                static_cast<uint64_t>(child.wait_status), child.valid};
        }
        result->captured_ns = now();
        if (result->captured_ns == 0) return EIO;
        target->copied_retired = header.visibility == TargetLeaseVisibility::retired && !header.may_have_delivery;
        return 0;
    });
}
int32_t coz_timer_release(CozTimerSession* session, uint32_t slot, uint64_t generation) {
    return guarded(session, true, [&](auto& value) {
        auto* target = get(value, slot);
        if (!target || !target->outstanding || generation != target->generation) return EINVAL;
        if (!target->copied_retired) return EBUSY;
        target->outstanding = false;
        target->copied_retired = false;
        return 0;
    });
}
int32_t coz_timer_retire(CozTimerSession* session, uint32_t slot) {
    return guarded(session, false, [&](auto& value) {
        auto* target = get(value, slot);
        if (!target || !target->registered) return EINVAL;
        if (target->thread != std::this_thread::get_id()) return EPERM;
        sigset_t mask;
        sigemptyset(&mask);
        sigaddset(&mask, value.signal_number);
        int error = pthread_sigmask(SIG_BLOCK, &mask, nullptr);
        if (error != 0) { fail(*target, error); return error; }
        if (target->timer_exists) {
            const auto result = direct(SYS_timer_delete, target->timer);
            if (result != 0) { fail(*target, static_cast<int>(-result)); return static_cast<int>(-result); }
            target->timer_exists = false;
        }
        error = drain(value, *target);
        if (error != 0) { fail(*target, error); return error; }
        if (target->lease && target->lease->phase() != TargetLeasePhase::retired) {
            std::optional<LeaseMailbox::Recovery> recovery;
            if (!target->execution) {
                if (!target->submission) return EPROTO;
                recovery = target->mailbox.recover(*target->submission);
                if (!recovery) return EPROTO;
            }
            if (target->lease->phase() == TargetLeasePhase::waiting) {
                const bool stopped = recovery && target->initial_arm_error != 0
                    ? target->lease->rejectInitialArm(target->initial_arm_error) : target->lease->cancelPending();
                if (!stopped) return EPROTO;
            }
            if (recovery) {
                if (!target->lease->quiesced() || !target->lease->retire() || !target->mailbox.quiesce(*recovery)) return EPROTO;
            } else if (!retireCore(*target)) return EPROTO;
        }
        error = pthread_sigmask(SIG_SETMASK, &target->original_mask, nullptr);
        if (error != 0) fail(*target, error);
        else target->registered = false;
        return error;
    });
}
int32_t coz_timer_clock(CozTimerSession* session, uint64_t* nanoseconds) {
    if (!nanoseconds) return EINVAL;
    *nanoseconds = 0;
    return guarded(session, true, [&](auto&) { *nanoseconds = now(); return *nanoseconds == 0 ? EIO : 0; });
}
int32_t coz_timer_close(CozTimerSession* session) {
    return guarded(session, true, [&](auto& value) {
        for (const auto& target : value.targets) if (target->registered || target->outstanding) return EBUSY;
        if (!value.closing) {
            if (sigaction(value.signal_number, &value.previous, nullptr) != 0) return errno;
            value.closing = true;
            signal_session.store(nullptr, std::memory_order_seq_cst);
        }
        if (active_handlers.load(std::memory_order_seq_cst) != 0) return EBUSY;
        active = nullptr;
        closed = true;
        delete &value;
        return 0;
    }, true);
}
