#pragma once
#include "PerfSampleRing.h"
#include <array>
#include <atomic>
#include <cstddef>
#include <cstdint>
#include <optional>
#include <span>

namespace coz::service {
constexpr uint64_t ledger_debt_limit_ns = 2000000, ledger_child_limit_ns = 50000, ledger_payment_limit_ns = 2000000;
constexpr uint64_t ledger_handler_limit_ns = 5000000, ledger_handler_reserve_ns = 1000000;

struct InstructionSpan {
    uint64_t begin{}, end{};
    bool contains(uint64_t ip) const noexcept { return ip >= begin && ip < end; }
};

class InstructionSet {
public:
    static constexpr size_t capacity = 16;
    static std::optional<InstructionSet> parse(std::span<const InstructionSpan> spans) noexcept {
        if (spans.size() > capacity) return std::nullopt;
        InstructionSet set;
        for (const auto& span : spans) {
            if (span.begin == 0 || span.end <= span.begin) return std::nullopt;
            for (size_t index = 0; index < set.count_; ++index)
                if (span.begin < set.spans_[index].end && set.spans_[index].begin < span.end) return std::nullopt;
            set.spans_[set.count_++] = span;
        }
        return set;
    }
    bool contains(uint64_t ip) const noexcept {
        for (size_t index = 0; index < count_; ++index) if (spans_[index].contains(ip)) return true;
        return false;
    }
    size_t size() const noexcept { return count_; }
private:
    std::array<InstructionSpan, capacity> spans_{};
    size_t count_{};
};

class LedgerPlan {
public:
    static std::optional<LedgerPlan> parse(uint32_t speedup_percent, uint64_t period,
        std::span<const InstructionSpan> selected, std::span<const InstructionSpan> eligible) noexcept {
        if (speedup_percent > 100 || period == 0 || period > 1000000000ULL) return std::nullopt;
        auto selected_set = InstructionSet::parse(selected);
        auto eligible_set = InstructionSet::parse(eligible);
        if (!selected_set || !eligible_set || eligible_set->size() == 0) return std::nullopt;
        return LedgerPlan(speedup_percent, period, *selected_set, *eligible_set);
    }
    uint64_t credit() const noexcept { return period_ * speedup_ / 100; }
    uint32_t speedup() const noexcept { return speedup_; }
    uint64_t period() const noexcept { return period_; }
    bool selected(uint64_t ip) const noexcept { return selected_.contains(ip); }
    bool eligible(uint64_t ip) const noexcept { return eligible_.contains(ip); }
private:
    LedgerPlan(uint32_t speedup, uint64_t period, InstructionSet selected, InstructionSet eligible) noexcept
        : speedup_(speedup), period_(period), selected_(selected), eligible_(eligible) {}
    uint32_t speedup_;
    uint64_t period_;
    InstructionSet selected_, eligible_;
};

enum class LedgerFailure : uint32_t { none, debt_exceeded, journal_full, clock_failed, wait_failed };

class ExperimentDelay {
public:
    uint64_t global() const noexcept { return global_.load(std::memory_order_acquire); }
    LedgerFailure failure() const noexcept { return LedgerFailure(failure_.load(std::memory_order_acquire)); }
    uint64_t sequences() const noexcept { return sequence_.load(std::memory_order_acquire); }
private:
    friend class ThreadDelayAccount;
    void fail(LedgerFailure failure) noexcept {
        uint32_t none = 0;
        failure_.compare_exchange_strong(none, uint32_t(failure), std::memory_order_acq_rel);
    }
    uint64_t next() noexcept { return sequence_.fetch_add(1, std::memory_order_acq_rel) + 1; }
    std::atomic<uint64_t> global_{}, sequence_{};
    std::atomic<uint32_t> failure_{};
    static_assert(std::atomic<uint64_t>::is_always_lock_free && std::atomic<uint32_t>::is_always_lock_free);
};

enum class LedgerEventKind : uint32_t { sample, publish, child, deferred, failure };

struct LedgerEvent {
    uint64_t sequence{}, observed_global{}, local_before{}, amount{}, requested{}, time{}, ip{};
    LedgerEventKind kind{LedgerEventKind::sample};
    uint32_t detail{};
};

enum class SettleOutcome { caught_up, published, budget_deferred, ineligible_deferred, failed };

struct SettleResult {
    SettleOutcome outcome{SettleOutcome::failed};
    uint64_t debt_before{}, debt_after{}, requested{}, measured{}, children{};
};

enum class LedgerWaitStatus : uint32_t { completed, failed };

struct LedgerWait {
    LedgerWaitStatus status{LedgerWaitStatus::failed};
    uint64_t measured{};
};

class ThreadDelayAccount {
public:
    static constexpr size_t settle_reserve = ledger_payment_limit_ns / ledger_child_limit_ns + 5;

    ThreadDelayAccount(ExperimentDelay& experiment, const LedgerPlan& plan, std::span<LedgerEvent> journal) noexcept
        : experiment_(experiment), plan_(plan), journal_(journal), local_(experiment.global()) {}
    ThreadDelayAccount(const ThreadDelayAccount&) = delete;
    ThreadDelayAccount& operator=(const ThreadDelayAccount&) = delete;

    uint64_t local() const noexcept { return local_; }
    std::span<const LedgerEvent> events() const noexcept { return journal_.first(count_); }

    size_t available() const noexcept {
        const auto free = journal_.size() - count_;
        return free > settle_reserve ? free - settle_reserve : 0;
    }

    bool accept(const PerfCpuSample& sample, uint64_t, uint64_t) noexcept {
        if (count_ + settle_reserve >= journal_.size()) {
            experiment_.fail(LedgerFailure::journal_full);
            return false;
        }
        const bool selected = plan_.selected(sample.instruction_pointer);
        const uint64_t amount = selected ? plan_.credit() : 0;
        append({0, experiment_.global(), local_, amount, 0, sample.timestamp, sample.instruction_pointer,
            LedgerEventKind::sample, selected ? 1u : 0u});
        local_ += amount;
        return true;
    }

    template<class Clock, class Waiter>
    SettleResult settle(Clock& clock, Waiter& waiter, uint64_t ip, uint64_t handler_started) noexcept {
        SettleResult result;
        if (journal_.size() - count_ < settle_reserve) return failed(result, LedgerFailure::journal_full, ip);
        bool first = true;
        for (;;) {
            auto observed = experiment_.global();
            if (local_ >= observed) {
                bool swapped = false;
                while (observed < local_ && !swapped)
                    swapped = experiment_.global_.compare_exchange_weak(observed, local_,
                        std::memory_order_acq_rel, std::memory_order_acquire);
                if (!swapped && observed > local_) {
                    first = false;
                    continue;
                }
                result.debt_after = 0;
                result.outcome = swapped ? SettleOutcome::published : SettleOutcome::caught_up;
                if (swapped) append({0, observed, local_, local_, 0, stamp(clock), ip, LedgerEventKind::publish, 0});
                return result;
            }
            const auto debt = observed - local_;
            if (first) result.debt_before = debt;
            first = false;
            if (debt > ledger_debt_limit_ns && !debt_failed_) {
                debt_failed_ = true;
                experiment_.fail(LedgerFailure::debt_exceeded);
                append({0, observed, local_, debt, 0, stamp(clock), ip, LedgerEventKind::failure, uint32_t(LedgerFailure::debt_exceeded)});
            }
            result.debt_after = debt;
            if (!plan_.eligible(ip)) {
                append({0, observed, local_, debt, 0, stamp(clock), ip, LedgerEventKind::deferred, 0});
                result.outcome = SettleOutcome::ineligible_deferred;
                return result;
            }
            const auto now = clock.now();
            if (now == 0 || now < handler_started) return failed(result, LedgerFailure::clock_failed, ip);
            auto requested = debt < ledger_child_limit_ns ? debt : ledger_child_limit_ns;
            if (requested > ledger_payment_limit_ns - result.requested) requested = ledger_payment_limit_ns - result.requested;
            if (requested == 0 || now - handler_started + requested > ledger_handler_limit_ns - ledger_handler_reserve_ns
                || journal_.size() - count_ < 3) {
                append({0, observed, local_, debt, 0, now, ip, LedgerEventKind::deferred, 1});
                result.outcome = SettleOutcome::budget_deferred;
                return result;
            }
            const auto wait = waiter.wait(requested);
            append({0, observed, local_, wait.measured, requested, stamp(clock), ip, LedgerEventKind::child, uint32_t(wait.status)});
            local_ += wait.measured;
            result.requested += requested;
            result.measured += wait.measured;
            ++result.children;
            if (wait.status != LedgerWaitStatus::completed) return failed(result, LedgerFailure::wait_failed, ip);
        }
    }

private:
    template<class Clock> uint64_t stamp(Clock& clock) noexcept {
        const auto now = clock.now();
        if (now == 0) {
            experiment_.fail(LedgerFailure::clock_failed);
            clock_failure_pending_ = !clock_failure_recorded_;
        }
        return now;
    }

    void append(LedgerEvent event) noexcept {
        if (count_ == journal_.size()) {
            experiment_.fail(LedgerFailure::journal_full);
            return;
        }
        event.sequence = experiment_.next();
        journal_[count_++] = event;
        if (clock_failure_pending_ && count_ < journal_.size()) {
            clock_failure_pending_ = false;
            clock_failure_recorded_ = true;
            // The caller adds a child's measured wait to local delay after this append.
            const auto local = event.kind == LedgerEventKind::child ? event.local_before + event.amount : event.local_before;
            journal_[count_++] = {experiment_.next(), experiment_.global(), local, 0, 0, 0, event.ip,
                LedgerEventKind::failure, uint32_t(LedgerFailure::clock_failed)};
        }
    }

    SettleResult failed(SettleResult result, LedgerFailure failure, uint64_t ip) noexcept {
        experiment_.fail(failure);
        if (count_ < journal_.size())
            append({0, experiment_.global(), local_, 0, 0, 0, ip, LedgerEventKind::failure, uint32_t(failure)});
        result.outcome = SettleOutcome::failed;
        return result;
    }

    ExperimentDelay& experiment_;
    const LedgerPlan& plan_;
    std::span<LedgerEvent> journal_;
    size_t count_{};
    uint64_t local_;
    bool debt_failed_{}, clock_failure_pending_{}, clock_failure_recorded_{};
};

template<class Clock, class Waiter>
class TargetCreditSink {
public:
    TargetCreditSink(ThreadDelayAccount& account, Clock& clock, Waiter& waiter) noexcept
        : account_(account), clock_(clock), waiter_(waiter) {}
    size_t available() const noexcept { return account_.available(); }
    bool accept(const PerfCpuSample& sample, uint64_t generation, uint64_t dispatch) noexcept {
        return account_.accept(sample, generation, dispatch);
    }
    void settle(uint64_t ip, uint64_t started) noexcept {
        last_ = account_.settle(clock_, waiter_, ip, started);
        ++settlements_;
    }
    const ThreadDelayAccount& account() const noexcept { return account_; }
    const SettleResult& last() const noexcept { return last_; }
    uint64_t settlements() const noexcept { return settlements_; }
private:
    ThreadDelayAccount& account_;
    Clock& clock_;
    Waiter& waiter_;
    SettleResult last_{};
    uint64_t settlements_{};
};
}
