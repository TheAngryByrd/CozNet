#pragma once
#include "PerfSampleRing.h"
#include <atomic>
#include <cstddef>
#include <cstdint>
#include <optional>
#include <span>

namespace coz::service {
constexpr uint64_t target_handler_limit_ns = 5000000;

class DispatchBudget {
public:
    static constexpr uint32_t max_records = 4096;
    static std::optional<DispatchBudget> parse(uint32_t records, uint64_t elapsed_ns) noexcept {
        if (records == 0 || records > max_records || elapsed_ns == 0 || elapsed_ns >= target_handler_limit_ns)
            return std::nullopt;
        return DispatchBudget(records, elapsed_ns);
    }
    uint32_t records() const noexcept { return records_; }
    uint64_t elapsed() const noexcept { return elapsed_; }
private:
    DispatchBudget(uint32_t records, uint64_t elapsed) noexcept : records_(records), elapsed_(elapsed) {}
    uint32_t records_;
    uint64_t elapsed_;
};

struct SampleNotification {
    uint64_t generation{}, sequence{};
};

enum class DispatchOutcome {
    drained_snapshot, records_exhausted, time_exhausted, sink_full, cancelled,
    source_failed, sink_failed, clock_failed, reentered, stale_notification, not_open, terminal
};

enum class DispatchBacklog { none, remaining, unknown };

struct DispatchResult {
    DispatchOutcome outcome{DispatchOutcome::not_open};
    DispatchBacklog backlog{DispatchBacklog::unknown};
    uint32_t accepted{};
    uint64_t generation{}, started{}, finished{};
    PerfSampleRingError source_error{PerfSampleRingError::none};
};

enum class TargetEpochPhase { unstarted, open, closing, closed_complete, terminal };

enum class EpochFailure {
    none, enable_failed, disable_failed, source_failed, sink_failed, clock_failed, count_read_failed, lost_samples, count_regressed
};

struct TargetEpochRecord {
    uint64_t generation{}, accepted{}, last_cumulative_cpu{}, value{}, lost{}, close_steps{};
    bool complete{};
    EpochFailure failure{EpochFailure::none};
};

struct JournalSample {
    PerfCpuSample sample;
    uint64_t generation{}, dispatch{};
};

class SampleJournal {
public:
    explicit SampleJournal(std::span<JournalSample> storage) noexcept : storage_(storage) {}
    size_t available() const noexcept { return storage_.size() - count_; }
    bool accept(const PerfCpuSample& sample, uint64_t generation, uint64_t dispatch) noexcept {
        if (count_ == storage_.size()) return false;
        storage_[count_++] = {sample, generation, dispatch};
        return true;
    }
    std::span<const JournalSample> entries() const noexcept { return storage_.first(count_); }
private:
    std::span<JournalSample> storage_;
    size_t count_{};
};

template<class Source, class Clock, class Sink>
class TargetSampleDispatcher {
public:
    TargetSampleDispatcher(Source& source, Clock& clock, Sink& sink) noexcept : source_(source), clock_(clock), sink_(sink) {}
    TargetSampleDispatcher(const TargetSampleDispatcher&) = delete;
    TargetSampleDispatcher& operator=(const TargetSampleDispatcher&) = delete;

    TargetEpochPhase phase() const noexcept { return phase_; }
    uint64_t generation() const noexcept { return epoch_.generation; }
    const TargetEpochRecord& epoch() const noexcept { return epoch_; }
    uint64_t reentered() const noexcept { return reentered_.load(std::memory_order_relaxed); }
    uint64_t stale() const noexcept { return stale_; }
    bool cancelled() const noexcept { return cancelled_.load(std::memory_order_acquire); }
    void cancel() noexcept { cancelled_.store(true, std::memory_order_release); }

    bool start() noexcept {
        Exclusive exclusive(*this);
        if (!exclusive || phase_ != TargetEpochPhase::unstarted) return false;
        return open(1);
    }

    bool resume() noexcept {
        Exclusive exclusive(*this);
        if (!exclusive || phase_ != TargetEpochPhase::closed_complete || cancelled()) return false;
        return open(epoch_.generation + 1);
    }

    DispatchResult notify(SampleNotification notification, DispatchBudget budget) noexcept {
        Exclusive exclusive(*this);
        if (!exclusive) return {DispatchOutcome::reentered};
        if (phase_ == TargetEpochPhase::terminal) return {DispatchOutcome::terminal};
        if (phase_ != TargetEpochPhase::open) return {DispatchOutcome::not_open};
        if (notification.generation != epoch_.generation || notification.sequence <= sequence_) {
            ++stale_;
            return {DispatchOutcome::stale_notification};
        }
        sequence_ = notification.sequence;
        return drain(budget, true);
    }

    DispatchResult beginClose() noexcept {
        Exclusive exclusive(*this);
        if (!exclusive) return {DispatchOutcome::reentered};
        if (phase_ != TargetEpochPhase::open) return {phase_ == TargetEpochPhase::terminal ? DispatchOutcome::terminal : DispatchOutcome::not_open};
        if (source_.disable() != 0) {
            fail(EpochFailure::disable_failed);
            return {DispatchOutcome::source_failed};
        }
        phase_ = TargetEpochPhase::closing;
        return {DispatchOutcome::drained_snapshot, DispatchBacklog::unknown, 0, epoch_.generation};
    }

    DispatchResult continueClose(DispatchBudget budget) noexcept {
        Exclusive exclusive(*this);
        if (!exclusive) return {DispatchOutcome::reentered};
        if (phase_ != TargetEpochPhase::closing) return {phase_ == TargetEpochPhase::terminal ? DispatchOutcome::terminal : DispatchOutcome::not_open};
        ++epoch_.close_steps;
        auto result = drain(budget, false);
        if (result.outcome != DispatchOutcome::drained_snapshot) return result;
        const auto counts = source_.finalCount();
        if (!counts) return stop(result, DispatchOutcome::source_failed, EpochFailure::count_read_failed);
        epoch_.value = counts->value;
        epoch_.lost = counts->lost;
        if (counts->lost != 0) return stop(result, DispatchOutcome::source_failed, EpochFailure::lost_samples);
        if (counts->value < epoch_.last_cumulative_cpu) return stop(result, DispatchOutcome::source_failed, EpochFailure::count_regressed);
        epoch_.complete = true;
        phase_ = TargetEpochPhase::closed_complete;
        return result;
    }

private:
    class Exclusive {
    public:
        explicit Exclusive(TargetSampleDispatcher& owner) noexcept
            : owner_(owner), held_(!owner.active_.exchange(true, std::memory_order_acquire)) {
            if (!held_) owner_.reentered_.fetch_add(1, std::memory_order_relaxed);
        }
        ~Exclusive() { if (held_) owner_.active_.store(false, std::memory_order_release); }
        explicit operator bool() const noexcept { return held_; }
    private:
        TargetSampleDispatcher& owner_;
        bool held_;
    };

    bool open(uint64_t generation) noexcept {
        epoch_ = {generation, 0, epoch_.last_cumulative_cpu};
        if (source_.enable() != 0) {
            fail(EpochFailure::enable_failed);
            return false;
        }
        phase_ = TargetEpochPhase::open;
        return true;
    }

    void fail(EpochFailure failure) noexcept {
        epoch_.complete = false;
        epoch_.failure = failure;
        phase_ = TargetEpochPhase::terminal;
    }

    DispatchResult drain(DispatchBudget budget, bool cancellable) noexcept {
        DispatchResult result{DispatchOutcome::drained_snapshot, DispatchBacklog::unknown, 0, epoch_.generation};
        result.started = clock_.now();
        auto prior = result.started;
        if (prior == 0) return stop(result, DispatchOutcome::clock_failed, EpochFailure::clock_failed);
        ++dispatches_;
        for (;;) {
            const auto now = clock_.now();
            if (now == 0 || now < prior) return stop(result, DispatchOutcome::clock_failed, EpochFailure::clock_failed);
            prior = now;
            result.finished = now;
            if (cancellable && cancelled()) { result.outcome = DispatchOutcome::cancelled; return result; }
            if (result.accepted == budget.records()) { result.outcome = DispatchOutcome::records_exhausted; return result; }
            if (now - result.started >= budget.elapsed()) { result.outcome = DispatchOutcome::time_exhausted; return result; }
            if (sink_.available() == 0) { result.outcome = DispatchOutcome::sink_full; return result; }
            const auto read = source_.read();
            if (read.record.error != PerfSampleRingError::none || (!read.record.sample && !read.snapshot_complete)) {
                result.source_error = read.record.error;
                return stop(result, DispatchOutcome::source_failed, EpochFailure::source_failed);
            }
            if (read.record.sample) {
                if (!sink_.accept(*read.record.sample, epoch_.generation, dispatches_))
                    return stop(result, DispatchOutcome::sink_failed, EpochFailure::sink_failed);
                ++result.accepted;
                ++epoch_.accepted;
                epoch_.last_cumulative_cpu = read.record.sample->cumulative_cpu;
            }
            result.backlog = read.snapshot_complete ? DispatchBacklog::none : DispatchBacklog::remaining;
            if (read.snapshot_complete) {
                const auto finished = clock_.now();
                if (finished == 0 || finished < prior) return stop(result, DispatchOutcome::clock_failed, EpochFailure::clock_failed);
                result.finished = finished;
                return result;
            }
        }
    }

    DispatchResult stop(DispatchResult result, DispatchOutcome outcome, EpochFailure failure) noexcept {
        fail(failure);
        result.outcome = outcome;
        result.backlog = DispatchBacklog::unknown;
        return result;
    }

    Source& source_;
    Clock& clock_;
    Sink& sink_;
    std::atomic<bool> active_{}, cancelled_{};
    std::atomic<uint64_t> reentered_{};
    TargetEpochPhase phase_{TargetEpochPhase::unstarted};
    TargetEpochRecord epoch_{};
    uint64_t sequence_{}, stale_{}, dispatches_{};
};
}
