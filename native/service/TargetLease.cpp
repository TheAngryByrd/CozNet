#include "TargetLease.h"
#include <algorithm>
#include <limits>
#include <functional>

namespace coz::service {
static_assert(std::atomic<uint64_t>::is_always_lock_free);
static_assert(std::atomic<TargetLeaseVisibility>::is_always_lock_free);
std::optional<TargetLeasePlan> TargetLeasePlan::parse(uint64_t generation, uint64_t command,
    uint64_t budget, uint64_t begin, uint64_t size) noexcept {
    if (generation == 0 || command == 0 || budget == 0 || budget > 2000000 || begin == 0
        || size == 0 || size > std::numeric_limits<uint64_t>::max() - begin) return std::nullopt;
    return TargetLeasePlan(generation, command, budget, begin, size);
}

TargetLease::TargetLease(TargetLeasePlan plan, std::span<TargetLeaseChild> journal) noexcept
    : plan_(plan), journal_(journal) {
    if (journal.empty() || journal.size() > 100000) fail(TargetLeaseError::journal_full);
    publishVisibility();
}
void TargetLease::fail(TargetLeaseError error, int transport) noexcept {
    error_ = error;
    transport_error_ = transport;
    stop_ = TargetLeaseStop::failed;
    phase_ = TargetLeasePhase::terminal;
}
bool TargetLease::start(TimerArmResult result) noexcept {
    if (phase_ != TargetLeasePhase::unarmed) return false;
    pending_ = result.pending();
    armed_ = result.timestamp();
    if (!pending_) fail(TargetLeaseError::arm_failed, result.error());
    else if (armed_ == 0) fail(TargetLeaseError::clock);
    else phase_ = TargetLeasePhase::waiting;
    publishVisibility();
    return true;
}
std::optional<TargetLease::Admission> TargetLease::arrive(uint64_t generation, uint64_t ip,
    uint64_t timestamp, bool cancelled) noexcept {
    if (generation != plan_.generation() || phase_ != TargetLeasePhase::waiting) return std::nullopt;
    pending_ = false;
    if (count_ == journal_.size()) { fail(TargetLeaseError::journal_full); publishVisibility(); return std::nullopt; }
    auto& child = journal_[count_];
    child = {count_ + 1, std::min<uint64_t>(50000, plan_.budget() - measured_), armed_, timestamp, 0, 0, ip};
    if (timestamp == 0 || timestamp < armed_) {
        ++count_;
        fail(TargetLeaseError::clock);
        publishVisibility();
        return std::nullopt;
    }
    stop_ = cancelled ? TargetLeaseStop::cancelled : plan_.eligible(ip) ? TargetLeaseStop::none : TargetLeaseStop::deferred;
    phase_ = TargetLeasePhase::executing;
    return Admission(this, plan_, child.ordinal, child.requested, stop_ == TargetLeaseStop::none);
}
std::optional<TargetLease::Completion> TargetLease::prepare(const Admission& admission, uint64_t waited,
    TargetWaitStatus status, bool cancelled) noexcept {
    if (phase_ != TargetLeasePhase::executing || admission.owner_ != this || admission.generation_ != plan_.generation()
        || admission.command_ != plan_.command() || admission.ordinal_ != count_ + 1)
        return std::nullopt;
    auto& child = journal_[count_];
    child.waited = waited;
    child.wait_status = status;
    if (waited > 5000000 || (!admission.wait_ && (waited != 0 || status != TargetWaitStatus::completed))
        || (status != TargetWaitStatus::completed && status != TargetWaitStatus::interrupted && status != TargetWaitStatus::failed)) {
        error_ = TargetLeaseError::invalid_wait;
        stop_ = TargetLeaseStop::failed;
    } else if (status == TargetWaitStatus::failed) {
        error_ = TargetLeaseError::invalid_wait;
        stop_ = TargetLeaseStop::failed;
    } else if (cancelled) stop_ = TargetLeaseStop::cancelled;
    else if (status == TargetWaitStatus::interrupted) stop_ = TargetLeaseStop::interrupted;
    else if (stop_ == TargetLeaseStop::none && measured_ + waited >= plan_.budget()) stop_ = TargetLeaseStop::completed;
    else if (stop_ == TargetLeaseStop::none && count_ + 1 == journal_.size()) {
        error_ = TargetLeaseError::journal_full;
        stop_ = TargetLeaseStop::failed;
    }
    phase_ = TargetLeasePhase::prepared;
    return Completion(this, plan_, child.ordinal, stop_ == TargetLeaseStop::none);
}
bool TargetLease::publish(const Completion& completion, uint64_t finished, TimerArmResult next, bool cancelled) noexcept {
    if (phase_ != TargetLeasePhase::prepared || completion.owner_ != this || completion.generation_ != plan_.generation()
        || completion.command_ != plan_.command() || completion.ordinal_ != count_ + 1)
        return false;
    auto& child = journal_[count_++];
    child.finished = finished;
    pending_ = next.pending();
    if (finished == 0 || finished < child.started) fail(TargetLeaseError::clock);
    else if (finished - child.started > 5000000) fail(TargetLeaseError::handler_bound);
    else if (child.waited > finished - child.started) fail(TargetLeaseError::invalid_wait);
    else if (error_ != TargetLeaseError::invalid_wait) {
        child.valid = true;
        measured_ += child.waited;
        if (!completion.rearm_ && (next.pending() || next.error() != 0)) fail(TargetLeaseError::unexpected_arm, next.error());
        else if (completion.rearm_ && !next.pending()) fail(TargetLeaseError::arm_failed, next.error());
        else if (next.pending() && (next.timestamp() < child.started || next.timestamp() > finished)) fail(TargetLeaseError::clock);
        else if (stop_ == TargetLeaseStop::failed) phase_ = TargetLeasePhase::terminal;
        else if (cancelled) { stop_ = TargetLeaseStop::cancelled; phase_ = TargetLeasePhase::terminal; }
        else if (stop_ != TargetLeaseStop::none) phase_ = TargetLeasePhase::terminal;
        else { armed_ = next.timestamp(); phase_ = TargetLeasePhase::waiting; }
    } else phase_ = TargetLeasePhase::terminal;
    publishVisibility();
    return true;
}
bool TargetLease::cancelPending() noexcept {
    if (phase_ != TargetLeasePhase::waiting) return false;
    stop_ = TargetLeaseStop::cancelled;
    phase_ = TargetLeasePhase::terminal;
    publishVisibility();
    return true;
}
bool TargetLease::quiesced() noexcept {
    if (phase_ != TargetLeasePhase::terminal) return false;
    pending_ = false;
    quiesced_ = true;
    publishVisibility();
    return true;
}
bool TargetLease::retire() noexcept {
    if (phase_ != TargetLeasePhase::terminal || !quiesced_) return false;
    phase_ = TargetLeasePhase::retired;
    publishVisibility();
    return true;
}
void TargetLease::publishVisibility() noexcept {
    if (phase_ == TargetLeasePhase::retired) {
        visibility_.store(TargetLeaseVisibility::retired, std::memory_order_release);
    } else if (phase_ == TargetLeasePhase::terminal) {
        if (visibility_.load(std::memory_order_relaxed) == TargetLeaseVisibility::active) {
            terminal_snapshot_ = {plan_.generation(), plan_.command(), plan_.budget(), count_, measured_,
                TargetLeaseVisibility::terminal, stop_, error_, transport_error_, pending_};
            visibility_.store(TargetLeaseVisibility::terminal, std::memory_order_release);
        }
        if (quiesced_) visibility_.store(TargetLeaseVisibility::quiesced, std::memory_order_release);
    } else if (phase_ == TargetLeasePhase::waiting) {
        published_count_.store(count_, std::memory_order_release);
    }
}
std::optional<TargetLeaseSnapshot> TargetLease::snapshot(std::span<TargetLeaseChild> destination,
    std::size_t& required) const noexcept {
    const auto visible = visibility_.load(std::memory_order_acquire);
    TargetLeaseSnapshot result;
    if (visible == TargetLeaseVisibility::active) {
        result = {plan_.generation(), plan_.command(), plan_.budget(), published_count_.load(std::memory_order_acquire),
            0, visible, TargetLeaseStop::none, TargetLeaseError::none, 0, true};
    } else {
        result = terminal_snapshot_;
        result.visibility = visible;
        if (visible == TargetLeaseVisibility::quiesced || visible == TargetLeaseVisibility::retired)
            result.may_have_delivery = false;
    }
    required = static_cast<std::size_t>(result.children);
    if (destination.size() < required) return std::nullopt;
    const auto less = std::less<const TargetLeaseChild*>{};
    if (required != 0 && less(destination.data(), journal_.data() + journal_.size())
        && less(journal_.data(), destination.data() + required)) return std::nullopt;
    for (std::size_t index = 0; index < required; ++index) {
        destination[index] = journal_[index];
        if (visible == TargetLeaseVisibility::active && destination[index].valid)
            result.measured += destination[index].waited;
    }
    return result;
}
}
