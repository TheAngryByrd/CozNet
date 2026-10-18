#pragma once
#include "PerfRing.Linux.h"
#include "TargetDelayLedger.h"
#include "TargetSampleDispatch.h"
#include <array>
#include <atomic>
#include <cerrno>
#include <csignal>
#include <ctime>
#include <fcntl.h>
#include <memory>
#include <new>
#include <sched.h>
#include <sys/ioctl.h>
#include <sys/mman.h>
#include <sys/syscall.h>
#include <ucontext.h>
#include <unistd.h>

namespace coz::service {
// One process-wide owner for the notification signal across every sink type.
struct LinuxTargetSignalOwner {
    inline static std::atomic<const void*> installed{};
    inline static std::atomic<uint32_t> live{};
};
struct LinuxPerfSource {
    int fd{-1};
    LinuxPerfRing* ring{};
    LinuxPerfRingRead read() noexcept { return ring->read(); }
    int enable() noexcept { return ioctl(fd, PERF_EVENT_IOC_ENABLE, 0) == 0 ? 0 : errno; }
    int disable() noexcept { return ioctl(fd, PERF_EVENT_IOC_DISABLE, 0) == 0 ? 0 : errno; }
    std::optional<LinuxPerfCountLoss> finalCount() noexcept {
        std::array<std::byte, 16> bytes{};
        const auto returned = ::read(fd, bytes.data(), bytes.size());
        return LinuxPerfCountLoss::parse(long(returned), bytes);
    }
};

struct LinuxMonotonicClock {
    uint64_t now() noexcept {
        timespec value{};
        if (clock_gettime(CLOCK_MONOTONIC, &value) != 0) return 0;
        return uint64_t(value.tv_sec) * 1000000000ULL + uint64_t(value.tv_nsec);
    }
};

struct LinuxSpinWaiter {
    static constexpr unsigned iteration_limit = 1u << 20;
    LedgerWait wait(uint64_t requested) noexcept {
        LinuxMonotonicClock clock;
        const auto started = clock.now();
        if (started == 0) return {LedgerWaitStatus::failed, 0};
        auto now = started;
        for (unsigned iteration = 0; now - started < requested; ++iteration) {
            if (iteration == iteration_limit) return {LedgerWaitStatus::failed, now - started};
            const auto next = clock.now();
            if (next == 0 || next < now) return {LedgerWaitStatus::failed, now - started};
            now = next;
            __builtin_ia32_pause();
        }
        return {LedgerWaitStatus::completed, now - started};
    }
};

struct TargetDispatchLogEntry {
    uint64_t sequence{}, handler_started{}, handler_finished{}, ip{};
    DispatchResult result;
};

class TargetDispatchLog {
public:
    explicit TargetDispatchLog(std::span<TargetDispatchLogEntry> storage) noexcept : storage_(storage) {}
    void append(const TargetDispatchLogEntry& entry) noexcept {
        if (count_ < storage_.size()) storage_[count_++] = entry;
        else ++dropped_;
    }
    std::span<const TargetDispatchLogEntry> entries() const noexcept { return storage_.first(count_); }
    uint64_t dropped() const noexcept { return dropped_; }
private:
    std::span<TargetDispatchLogEntry> storage_;
    size_t count_{};
    uint64_t dropped_{};
};

struct TargetRetirement {
    bool blocked{}, disabled{}, async_cleared{}, quiescent{true}, unmapped{}, closed{}, mask_restored{}, raw_count_read{};
    uint64_t pending_drained{}, raw_value{}, raw_lost{};
    int error{};
};

// All member functions except cancel() must run on the thread that called create().
// The sink must outlive the session handle.
template<class Sink>
class LinuxTargetSession {
public:
    using Dispatcher = TargetSampleDispatcher<LinuxPerfSource, LinuxMonotonicClock, Sink>;
    struct Config {
        uint64_t period;
        size_t data_pages;
        DispatchBudget budget;
    };

    static int signalNumber() noexcept { return SIGRTMIN + 8; }

    static int install() noexcept {
        const void* empty = nullptr;
        const auto* claim = reinterpret_cast<const void*>(&fallback);
        if (!LinuxTargetSignalOwner::installed.compare_exchange_strong(empty, claim, std::memory_order_acq_rel)) return EBUSY;
        const int error = installHandlers();
        LinuxTargetSignalOwner::installed.store(error == 0 ? reinterpret_cast<const void*>(&handler) : nullptr,
            std::memory_order_release);
        return error;
    }

    static int restore() noexcept {
        if (LinuxTargetSignalOwner::installed.load(std::memory_order_acquire) != reinterpret_cast<const void*>(&handler)) return EPERM;
        if (LinuxTargetSignalOwner::live.load(std::memory_order_acquire) != 0) return EBUSY;
        for (auto& slot : slots_) if (slot.load(std::memory_order_acquire)) return EBUSY;
        sigset_t pending;
        if (sigpending(&pending) != 0) return errno;
        if (sigismember(&pending, SIGIO) == 1 || sigismember(&pending, signalNumber()) == 1) return EBUSY;
        struct sigaction action{};
        action.sa_handler = SIG_DFL;
        sigemptyset(&action.sa_mask);
        for (const int number : {signalNumber(), SIGIO})
            if (sigaction(number, &action, nullptr) != 0) return errno;
        LinuxTargetSignalOwner::installed.store(nullptr, std::memory_order_release);
        return 0;
    }

    static uint64_t foreignSignals() noexcept { return foreign_.load(std::memory_order_relaxed); }
    static uint64_t sigioFallbacks() noexcept { return sigio_.load(std::memory_order_relaxed); }

    // Another thread's handler can still hold the registry pointer after a quiescence timeout.
    struct Release {
        void operator()(LinuxTargetSession* session) const noexcept {
            if (session->retire().quiescent) delete session;
        }
    };
    using Handle = std::unique_ptr<LinuxTargetSession, Release>;

    static int create(Config config, Sink& sink, std::span<TargetDispatchLogEntry> log,
        Handle& result) noexcept {
        result.reset();
        if (config.period == 0 || config.data_pages == 0 || (config.data_pages & (config.data_pages - 1)) != 0) return EINVAL;
        const auto thread = uint32_t(gettid());
        for (auto& owner : owners_) if (owner.load(std::memory_order_acquire) == thread) return EBUSY;
        Handle session(new (std::nothrow) LinuxTargetSession(config, sink, log));
        if (!session) return ENOMEM;
        const int error = session->open();
        if (error != 0) return error;
        result = std::move(session);
        return 0;
    }

    LinuxTargetSession(const LinuxTargetSession&) = delete;
    LinuxTargetSession& operator=(const LinuxTargetSession&) = delete;

    int start() noexcept {
        if (!blocked_ || !dispatcher_->start()) return EPROTO;
        return arm();
    }

    DispatchResult closeEpoch(DispatchBudget budget, uint32_t max_steps) noexcept {
        if (const int error = block(); error != 0) return lifecycle(error);
        DispatchResult result{DispatchOutcome::drained_snapshot};
        if (dispatcher_->phase() != TargetEpochPhase::closing) result = dispatcher_->beginClose();
        if (const int error = drainPending(); error != 0) return lifecycle(error);
        if (result.outcome != DispatchOutcome::drained_snapshot) return result;
        for (uint32_t step = 0; step < max_steps; ++step) {
            result = dispatcher_->continueClose(budget);
            if (result.outcome != DispatchOutcome::records_exhausted && result.outcome != DispatchOutcome::time_exhausted) return result;
        }
        return result;
    }

    int resume() noexcept {
        if (!blocked_ || !dispatcher_->resume()) return EPROTO;
        return arm();
    }

    const TargetRetirement& retire() noexcept {
        if (retired_) return retirement_;
        retired_ = true;
        if (!mask_saved_) {
            retirement_.async_cleared = retirement_.unmapped = retirement_.closed = retirement_.mask_restored = true;
            return retirement_;
        }
        record(block());
        retirement_.blocked = blocked_;
        if (fd_ >= 0) {
            retirement_.disabled = ioctl(fd_, PERF_EVENT_IOC_DISABLE, 0) == 0;
            if (!retirement_.disabled) record(errno);
        }
        if (async_) {
            const int flags = fcntl(fd_, F_GETFL);
            if (flags >= 0 && fcntl(fd_, F_SETFL, flags & ~O_ASYNC) == 0) async_ = false;
            else record(errno);
        }
        retirement_.async_cleared = !async_;
        record(drainPending());
        unregister();
        if (const int error = awaitHandlers(); error != 0) {
            record(error);
            retirement_.quiescent = false;
        }
        if (fd_ >= 0 && dispatcher_ && dispatcher_->phase() != TargetEpochPhase::closed_complete) {
            if (auto counts = source_.finalCount()) {
                retirement_.raw_count_read = true;
                retirement_.raw_value = counts->value;
                retirement_.raw_lost = counts->lost;
            }
        }
        if (mapping_ != MAP_FAILED) {
            if (munmap(mapping_, mapping_length_) == 0) mapping_ = MAP_FAILED;
            else record(errno);
        }
        retirement_.unmapped = mapping_ == MAP_FAILED;
        if (fd_ >= 0) {
            if (close(fd_) == 0) fd_ = -1;
            else record(errno);
        }
        retirement_.closed = fd_ < 0;
        if (blocked_ && retirement_.error == 0) {
            const int error = pthread_sigmask(SIG_SETMASK, &original_mask_, nullptr);
            if (error == 0) blocked_ = false;
            else record(error);
        }
        retirement_.mask_restored = !blocked_;
        retirement_.pending_drained = pending_drained_;
        return retirement_;
    }

    void cancel() noexcept { if (dispatcher_) dispatcher_->cancel(); }
    int lifecycleError() const noexcept { return lifecycle_error_; }
    const Dispatcher& dispatcher() const noexcept { return *dispatcher_; }
    const Sink& sink() const noexcept { return sink_; }
    const TargetDispatchLog& log() const noexcept { return log_; }
    uint64_t deliveries() const noexcept { return deliveries_.load(std::memory_order_relaxed); }
    uint64_t pendingDrained() const noexcept { return pending_drained_; }
    uint64_t acceptedTail() const noexcept { return ring_ ? ring_->acceptedTail() : 0; }
    size_t dataSize() const noexcept { return mapping_length_ - page_; }

private:
    static constexpr size_t slot_count = 64;
    static constexpr unsigned pending_limit = 65536;
    static constexpr uint64_t quiescence_limit_ns = 100000000;
    inline static std::array<std::atomic<LinuxTargetSession*>, slot_count> slots_{};
    inline static std::atomic<uint64_t> foreign_{}, sigio_{};
    inline static std::atomic<uint32_t> handlers_{};
    inline static std::array<std::atomic<uint32_t>, slot_count> owners_{};

    LinuxTargetSession(Config config, Sink& sink, std::span<TargetDispatchLogEntry> log) noexcept
        : config_(config), sink_(sink), log_(log), attr_(LinuxPerfRingPlan::attributes(config.period)) {}

    DispatchResult lifecycle(int error) noexcept {
        lifecycle_error_ = error;
        return {DispatchOutcome::source_failed};
    }

    void record(int error) noexcept {
        if (error != 0 && retirement_.error == 0) retirement_.error = error;
    }

    int open() noexcept {
        sigset_t mask;
        sigemptyset(&mask);
        sigaddset(&mask, signalNumber());
        if (const int error = pthread_sigmask(SIG_BLOCK, &mask, &original_mask_); error != 0) return error;
        blocked_ = mask_saved_ = true;
        if (sigismember(&original_mask_, signalNumber()) != 0) return EINVAL;
        process_ = uint32_t(getpid());
        thread_ = uint32_t(gettid());
        page_ = size_t(sysconf(_SC_PAGESIZE));
        fd_ = int(syscall(SYS_perf_event_open, &attr_, 0, -1, -1, PERF_FLAG_FD_CLOEXEC));
        if (fd_ < 0) return errno;
        mapping_length_ = page_ * (1 + config_.data_pages);
        mapping_ = mmap(nullptr, mapping_length_, PROT_READ | PROT_WRITE, MAP_SHARED, fd_, 0);
        if (mapping_ == MAP_FAILED) return errno;
        auto plan = LinuxPerfRingPlan::parse({static_cast<std::byte*>(mapping_), mapping_length_}, page_, attr_, process_, thread_);
        if (!plan) return EPROTO;
        ring_.emplace(*plan);
        source_ = {fd_, &*ring_};
        dispatcher_.emplace(source_, clock_, sink_);
        bool registered = false;
        for (size_t index = 0; index < slot_count && !registered; ++index) {
            LinuxTargetSession* empty = nullptr;
            if (slots_[index].compare_exchange_strong(empty, this, std::memory_order_seq_cst)) {
                owners_[index].store(thread_, std::memory_order_release);
                LinuxTargetSignalOwner::live.fetch_add(1, std::memory_order_acq_rel);
                slot_ = index;
                registered = true;
            }
        }
        if (!registered) return EBUSY;
        const f_owner_ex owner{F_OWNER_TID, pid_t(thread_)};
        if (fcntl(fd_, F_SETOWN_EX, &owner) != 0 || fcntl(fd_, F_SETSIG, signalNumber()) != 0) return errno;
        const int flags = fcntl(fd_, F_GETFL);
        if (flags < 0 || fcntl(fd_, F_SETFL, flags | O_ASYNC) != 0) return errno;
        async_ = true;
        return 0;
    }

    int arm() noexcept {
        armed_generation_.store(dispatcher_->generation(), std::memory_order_release);
        const int error = pthread_sigmask(SIG_SETMASK, &original_mask_, nullptr);
        if (error == 0) blocked_ = false;
        return error;
    }

    int block() noexcept {
        if (!blocked_) {
            sigset_t mask;
            sigemptyset(&mask);
            sigaddset(&mask, signalNumber());
            if (const int error = pthread_sigmask(SIG_BLOCK, &mask, nullptr); error != 0) return error;
            blocked_ = true;
        }
        armed_generation_.store(0, std::memory_order_release);
        return 0;
    }

    int drainPending() noexcept {
        sigset_t mask;
        sigemptyset(&mask);
        sigaddset(&mask, signalNumber());
        const timespec immediate{};
        for (unsigned attempt = 0; attempt < pending_limit; ++attempt) {
            siginfo_t info{};
            const int result = sigtimedwait(&mask, &info, &immediate);
            if (result < 0 && errno == EINTR) continue;
            if (result < 0 && errno == EAGAIN) return 0;
            if (result < 0) return errno;
            if (info.si_code != POLL_IN || info.si_fd != fd_) return EPROTO;
            ++pending_drained_;
        }
        return EOVERFLOW;
    }

    int awaitHandlers() noexcept {
        LinuxMonotonicClock clock;
        const auto started = clock.now();
        while (handlers_.load(std::memory_order_seq_cst) != 0) {
            const auto now = clock.now();
            if (now == 0 || started == 0 || now - started > quiescence_limit_ns) return ETIMEDOUT;
            sched_yield();
        }
        return 0;
    }

    void unregister() noexcept {
        if (slot_ == slot_count) return;
        owners_[slot_].store(0, std::memory_order_release);
        LinuxTargetSignalOwner::live.fetch_sub(1, std::memory_order_acq_rel);
        slots_[slot_].store(nullptr, std::memory_order_seq_cst);
        slot_ = slot_count;
    }

    static int installHandlers() noexcept {
        for (const int number : {signalNumber(), SIGIO}) {
            struct sigaction previous{};
            if (sigaction(number, nullptr, &previous) != 0) return errno;
            if (previous.sa_handler != SIG_DFL) return EBUSY;
        }
        struct sigaction action{};
        action.sa_sigaction = handler;
        action.sa_flags = SA_SIGINFO | SA_RESTART;
        sigemptyset(&action.sa_mask);
        if (sigaction(signalNumber(), &action, nullptr) != 0) return errno;
        action.sa_sigaction = fallback;
        if (sigaction(SIGIO, &action, nullptr) != 0) {
            const int error = errno;
            struct sigaction reset{};
            reset.sa_handler = SIG_DFL;
            sigaction(signalNumber(), &reset, nullptr);
            return error;
        }
        return 0;
    }

    static void fallback(int, siginfo_t*, void*) noexcept { sigio_.fetch_add(1, std::memory_order_relaxed); }

    static void handler(int number, siginfo_t* info, void* context) noexcept {
        const int saved = errno;
        struct Active {
            Active() noexcept { handlers_.fetch_add(1, std::memory_order_seq_cst); }
            ~Active() { handlers_.fetch_sub(1, std::memory_order_seq_cst); }
        } active;
        LinuxMonotonicClock clock;
        const auto started = clock.now();
        const auto thread = uint32_t(gettid());
        LinuxTargetSession* session = nullptr;
        if (number == signalNumber() && info->si_code == POLL_IN)
            for (auto& slot : slots_)
                if (auto* candidate = slot.load(std::memory_order_seq_cst);
                    candidate && candidate->thread_ == thread && candidate->fd_ == info->si_fd) {
                    session = candidate;
                    break;
                }
        if (!session) {
            foreign_.fetch_add(1, std::memory_order_relaxed);
            errno = saved;
            return;
        }
        const auto sequence = session->deliveries_.fetch_add(1, std::memory_order_relaxed) + 1;
        const SampleNotification notification{session->armed_generation_.load(std::memory_order_acquire), sequence};
        auto result = session->dispatcher_->notify(notification, session->config_.budget);
        const auto ip = uint64_t(static_cast<ucontext_t*>(context)->uc_mcontext.gregs[REG_RIP]);
        if constexpr (requires { session->sink_.settle(ip, started); })
            if (result.outcome != DispatchOutcome::reentered && result.outcome != DispatchOutcome::stale_notification)
                session->sink_.settle(ip, started);
        session->log_.append({sequence, started, clock.now(), ip, result});
        errno = saved;
    }

    Config config_;
    Sink& sink_;
    TargetDispatchLog log_;
    perf_event_attr attr_;
    LinuxMonotonicClock clock_;
    LinuxPerfSource source_;
    std::optional<LinuxPerfRing> ring_;
    std::optional<Dispatcher> dispatcher_;
    std::atomic<uint64_t> armed_generation_{}, deliveries_{};
    size_t slot_{slot_count};
    sigset_t original_mask_{};
    void* mapping_{MAP_FAILED};
    size_t mapping_length_{}, page_{};
    uint64_t pending_drained_{};
    TargetRetirement retirement_;
    uint32_t process_{}, thread_{};
    int fd_{-1}, lifecycle_error_{};
    bool blocked_{}, mask_saved_{}, async_{}, retired_{};
};

using LinuxTargetSampleSession = LinuxTargetSession<SampleJournal>;
}
