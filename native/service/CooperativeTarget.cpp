#include "CooperativeTarget.h"
#include "TimerJournalSnapshot.h"
#include <mutex>
#include <algorithm>

namespace coz::service {
using TargetCallGuard = detail::CooperativeCallGuard<detail::CooperativeTargetCall>;
bool CooperativeCodeLifetime::tryRevoke() {
    TargetCallGuard entry;
    if (!entry) return false;
    std::unique_lock lock(gate_, std::try_to_lock);
    if (!lock.owns_lock()) return false;
    active_ = false;
    return true;
}

CooperativeTarget::CooperativeTarget(CooperativePollSite site, std::shared_ptr<CooperativeCodeLifetime> code,
    std::thread::id controller, std::size_t capacity, CooperativeTiming& timing, bool observeCpu, bool observeProgress)
    : site_(site), code_(std::move(code)), controller_(controller), owner_(std::this_thread::get_id()),
      timing_(timing), journal_(capacity), observe_cpu_(observeCpu), observe_progress_(observeProgress), cpu_journal_(observeCpu ? capacity : 0) {}

std::unique_ptr<CooperativeTarget> CooperativeTarget::registerCurrentThread(CooperativePollSite site,
    std::shared_ptr<CooperativeCodeLifetime> code, std::thread::id controller,
    std::size_t capacity, CooperativeTiming& timing, bool observeCpu, bool observeProgress, CooperativeOwnerRole role) {
    TargetCallGuard entry;
    if (!entry || (role != CooperativeOwnerRole::separate && role != CooperativeOwnerRole::controller)) return nullptr;
    if (!code || controller == std::thread::id{}
        || (controller == std::this_thread::get_id()) != (role == CooperativeOwnerRole::controller)
        || capacity == 0 || capacity > 100000 || code->version_ != site.codeVersion()) return nullptr;
    std::shared_lock guard(code->gate_);
    if (!code->active_) return nullptr;
    return std::unique_ptr<CooperativeTarget>(new CooperativeTarget(site, code, controller, capacity, timing, observeCpu, observeProgress));
}

std::optional<uint32_t> CooperativeTarget::submit(uint64_t command, uint64_t budget) {
    TargetCallGuard entry;
    if (!entry) return std::nullopt;
    if (!controller() || outstanding_.load(std::memory_order_acquire) != 0
        || !TargetLeasePlan::parseCooperative(1, command, budget, site_)) return std::nullopt;
    std::shared_lock guard(code_->gate_);
    if (!code_->active_) return std::nullopt;
    const auto armed = timing_.now();
    if (armed == 0) return std::nullopt;
    const auto submission = mailbox_.begin();
    if (!submission) return std::nullopt;
    lease_.emplace(*TargetLeasePlan::parseCooperative(submission->generation(), command, budget, site_), journal_);
    if (!lease_->start(TimerArmResult::accepted(armed))) {
        mailbox_.abandonConfiguration(*submission);
        return std::nullopt;
    }
    cpu_count_ = 0;
    observed_retired_ = false;
    if (observe_progress_) progress_.begin(submission->generation());
    outstanding_.store(submission->generation(), std::memory_order_release);
    if (!mailbox_.publish(*submission)) return std::nullopt;
    announced_.store(submission->generation(), std::memory_order_release);
    return submission->generation();
}

bool CooperativeTarget::finish() noexcept {
    if (!lease_->quiesced() || !lease_->retire()) return false;
    const auto execution = *execution_;
    execution_.reset();
    const auto generation = announced_.load(std::memory_order_acquire);
    announced_.store(0, std::memory_order_release);
    const bool finished = mailbox_.quiesce(execution);
    recordProgress(generation, finished ? CooperativeProgressPhase::quiesced : CooperativeProgressPhase::failed);
    return finished;
}

CooperativePollResult CooperativeTarget::poll(CooperativePollSite observed) {
    TargetCallGuard entry;
    if (!entry) return CooperativePollResult::rejected;
    if (std::this_thread::get_id() != owner_ || retired() || polling_) return CooperativePollResult::rejected;
    const auto generation = announced_.load(std::memory_order_acquire);
    if (generation == 0) return CooperativePollResult::idle;
    struct PollGuard {
        bool& active;
        explicit PollGuard(bool& value) : active(value) { active = true; }
        ~PollGuard() { active = false; }
    } polling(polling_);
    recordProgress(generation, CooperativeProgressPhase::poll_entered);
    std::shared_lock guard(code_->gate_);
    recordProgress(generation, CooperativeProgressPhase::code_guard_acquired);
    const auto failedPoll = [&] {
        recordProgress(generation, CooperativeProgressPhase::failed);
        return CooperativePollResult::failed;
    };
    if (!execution_) {
        execution_ = mailbox_.claim(generation);
        if (!execution_) return failedPoll();
    }
    const auto cancelled = [&] { return !code_->active_ || mailbox_.cancelled(*execution_); };
    const auto admission = lease_->arriveAtPoll(generation, observed, timing_.now(), cancelled());
    if (!admission) {
        if (lease_->phase() == TargetLeasePhase::terminal) finish();
        return failedPoll();
    }
    recordProgress(generation, CooperativeProgressPhase::child_admitted, admission->ordinal());
    uint64_t waited = 0;
    auto status = TargetWaitStatus::completed;
    if (admission->shouldWait()) {
        CooperativeCpuChild* observation = nullptr;
        if (observe_cpu_) {
            observation = &cpu_journal_[cpu_count_++];
            *observation = {admission->generation(), admission->command(), admission->ordinal(),
                observed.worker(), observed.codeVersion(), observed.site(), admission->requested(), timing_.cpu(), {}};
        }
        const auto before = timing_.now();
        if (before == 0) status = TargetWaitStatus::failed;
        else {
            recordProgress(generation, CooperativeProgressPhase::wait_entered);
            status = timing_.wait(admission->requested());
            recordProgress(generation, CooperativeProgressPhase::wait_returned);
            const auto after = timing_.now();
            if (after == 0 || after < before) status = TargetWaitStatus::failed;
            else waited = after - before;
        }
        if (observation) observation->after = timing_.cpu();
    }
    const auto completion = lease_->prepare(*admission, waited, status, cancelled());
    if (!completion) return failedPoll();
    const auto next = completion->shouldRearm() ? TimerArmResult::accepted(timing_.now()) : TimerArmResult::notRequested();
    if (!lease_->publish(*completion, timing_.now(), next, cancelled())) return failedPoll();
    recordProgress(generation, CooperativeProgressPhase::child_published);
    const bool failed = lease_->error() != TargetLeaseError::none;
    if (lease_->phase() == TargetLeasePhase::terminal && !finish()) return failedPoll();
    recordProgress(generation, failed ? CooperativeProgressPhase::failed : CooperativeProgressPhase::returning);
    return failed ? CooperativePollResult::failed : CooperativePollResult::serviced;
}

bool CooperativeTarget::cancel(uint32_t generation) noexcept {
    TargetCallGuard entry;
    if (!entry) return false;
    return controller() && generation != 0 && outstanding_.load(std::memory_order_acquire) == generation
        && mailbox_.cancel(generation);
}
std::optional<bool> CooperativeTarget::receiptReady(uint32_t generation) const noexcept {
    if (generation == 0 || outstanding_.load(std::memory_order_acquire) != generation) return std::nullopt;
    return mailbox_.quiescent(generation);
}
void CooperativeTarget::recordProgress(uint32_t generation, CooperativeProgressPhase phase, uint64_t ordinal) noexcept {
    if (observe_progress_) progress_.record(generation, phase, ordinal);
}
std::optional<CooperativeProgressSnapshot> CooperativeTarget::progress(uint32_t generation) const noexcept {
    if (!observe_progress_ || generation == 0 || outstanding_.load(std::memory_order_acquire) != generation)
        return std::nullopt;
    const auto result = progress_.read(generation);
    if (outstanding_.load(std::memory_order_acquire) != generation) return std::nullopt;
    return result;
}
std::optional<TargetLeaseSnapshot> CooperativeTarget::snapshot(uint32_t generation,
    std::span<TargetLeaseChild> destination, std::size_t& required) noexcept {
    TargetCallGuard entry;
    if (!entry) return std::nullopt;
    if (!controller() || generation == 0 || outstanding_.load(std::memory_order_acquire) != generation) return std::nullopt;
    auto reader = mailbox_.read(generation);
    if (!reader) return std::nullopt;
    const auto result = captureTimerJournal(*lease_, *reader, destination, required);
    if (result && result->visibility == TargetLeaseVisibility::retired) observed_retired_ = true;
    return result;
}
std::optional<std::size_t> CooperativeTarget::cpuSnapshot(uint32_t generation,
    std::span<CooperativeCpuChild> destination) noexcept {
    TargetCallGuard entry;
    if (!entry) return std::nullopt;
    if (!controller() || !observe_cpu_ || generation == 0
        || outstanding_.load(std::memory_order_acquire) != generation) return std::nullopt;
    auto reader = mailbox_.read(generation);
    if (!reader || !reader->quiescent()) return std::nullopt;
    if (destination.size() >= cpu_count_)
        std::copy_n(cpu_journal_.begin(), cpu_count_, destination.begin());
    return cpu_count_;
}

bool CooperativeTarget::release(uint32_t generation) noexcept {
    TargetCallGuard entry;
    if (!entry) return false;
    if (!controller() || generation == 0 || outstanding_.load(std::memory_order_acquire) != generation
        || !observed_retired_) return false;
    {
        auto reader = mailbox_.read(generation);
        if (!reader || !reader->quiescent()) return false;
    }
    observed_retired_ = false;
    outstanding_.store(0, std::memory_order_release);
    return true;
}
bool CooperativeTarget::retire() noexcept {
    TargetCallGuard entry;
    if (!entry) return false;
    if (std::this_thread::get_id() != owner_ || outstanding_.load(std::memory_order_acquire) != 0
        || !mailbox_.close()) return false;
    retired_.store(true, std::memory_order_release);
    return true;
}
}
