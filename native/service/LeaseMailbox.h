#pragma once
#include <atomic>
#include <cstdint>
#include <limits>
#include <optional>
#include <utility>

namespace coz::service {
enum class LeaseMailboxPhase : uint64_t { idle, configuring, ready, active, quiescent, closed, recovering };

class LeaseMailbox {
public:
    class Submission {
    public:
        uint32_t generation() const noexcept { return generation_; }
    private:
        friend class LeaseMailbox;
        Submission(LeaseMailbox* owner, uint32_t generation) noexcept : owner_(owner), generation_(generation) {}
        LeaseMailbox* owner_;
        uint32_t generation_;
    };
    class Execution {
    public:
        uint32_t generation() const noexcept { return generation_; }
    private:
        friend class LeaseMailbox;
        Execution(LeaseMailbox* owner, uint32_t generation) noexcept : owner_(owner), generation_(generation) {}
        LeaseMailbox* owner_;
        uint32_t generation_;
    };
    class Recovery {
    public:
        uint32_t generation() const noexcept { return generation_; }
    private:
        friend class LeaseMailbox;
        Recovery(LeaseMailbox* owner, uint32_t generation) noexcept : owner_(owner), generation_(generation) {}
        LeaseMailbox* owner_;
        uint32_t generation_;
    };
    class Reader {
    public:
        Reader(const Reader&) = delete;
        Reader& operator=(const Reader&) = delete;
        Reader(Reader&& other) noexcept : owner_(std::exchange(other.owner_, nullptr)), generation_(other.generation_) {}
        Reader& operator=(Reader&&) = delete;
        ~Reader() { if (owner_) owner_->state_.fetch_sub(reader_unit, std::memory_order_release); }
        uint32_t generation() const noexcept { return generation_; }
        bool quiescent() const noexcept {
            if (!owner_) return false;
            const auto current = owner_->state_.load(std::memory_order_acquire);
            return generationOf(current) == generation_ && phaseOf(current) == LeaseMailboxPhase::quiescent;
        }
    private:
        friend class LeaseMailbox;
        Reader(LeaseMailbox* owner, uint32_t generation) noexcept : owner_(owner), generation_(generation) {}
        LeaseMailbox* owner_;
        uint32_t generation_;
    };

    explicit LeaseMailbox(uint32_t completedGeneration = 0) noexcept : state_(uint64_t{completedGeneration} << 32) {}
    LeaseMailbox(const LeaseMailbox&) = delete;
    LeaseMailbox& operator=(const LeaseMailbox&) = delete;

    std::optional<Submission> begin() noexcept {
        auto current = state_.load(std::memory_order_acquire);
        for (;;) {
            const auto phase = phaseOf(current);
            if ((phase != LeaseMailboxPhase::idle && phase != LeaseMailboxPhase::quiescent)
                || (current & readers_mask) != 0 || generationOf(current) == std::numeric_limits<uint32_t>::max())
                return std::nullopt;
            const auto generation = generationOf(current) + 1;
            const auto next = (uint64_t{generation} << 32) | static_cast<uint64_t>(LeaseMailboxPhase::configuring);
            if (state_.compare_exchange_weak(current, next, std::memory_order_acq_rel)) return Submission(this, generation);
        }
    }
    bool publish(const Submission& submission) noexcept {
        return submission.owner_ == this && transition(submission.generation_, LeaseMailboxPhase::configuring, LeaseMailboxPhase::ready);
    }
    bool abandonConfiguration(const Submission& submission) noexcept {
        return submission.owner_ == this && transition(submission.generation_, LeaseMailboxPhase::configuring, LeaseMailboxPhase::quiescent);
    }
    bool abandonUndelivered(const Submission& submission) noexcept {
        return submission.owner_ == this && transition(submission.generation_, LeaseMailboxPhase::ready, LeaseMailboxPhase::quiescent);
    }
    std::optional<Recovery> recover(const Submission& submission) noexcept {
        if (submission.owner_ != this
            || !transition(submission.generation_, LeaseMailboxPhase::ready, LeaseMailboxPhase::recovering)) return std::nullopt;
        return Recovery(this, submission.generation_);
    }
    bool quiesce(const Recovery& recovery) noexcept {
        return recovery.owner_ == this && transition(recovery.generation_, LeaseMailboxPhase::recovering, LeaseMailboxPhase::quiescent);
    }
    std::optional<Execution> claim(uint32_t generation) noexcept {
        if (!transition(generation, LeaseMailboxPhase::ready, LeaseMailboxPhase::active)) return std::nullopt;
        return Execution(this, generation);
    }
    bool quiesce(const Execution& execution) noexcept {
        return execution.owner_ == this && transition(execution.generation_, LeaseMailboxPhase::active, LeaseMailboxPhase::quiescent);
    }
    bool cancel(uint32_t generation) noexcept {
        auto current = state_.load(std::memory_order_acquire);
        for (;;) {
            const auto phase = phaseOf(current);
            if (generation == 0 || generationOf(current) != generation
                || (phase != LeaseMailboxPhase::configuring && phase != LeaseMailboxPhase::ready && phase != LeaseMailboxPhase::active))
                return false;
            if (state_.compare_exchange_weak(current, current | cancelled_bit, std::memory_order_acq_rel)) return true;
        }
    }
    bool cancelled(const Execution& execution) const noexcept {
        const auto current = state_.load(std::memory_order_acquire);
        return execution.owner_ == this && generationOf(current) == execution.generation_ && (current & cancelled_bit) != 0;
    }
    std::optional<Reader> read(uint32_t generation) noexcept {
        auto current = state_.load(std::memory_order_acquire);
        for (;;) {
            const auto phase = phaseOf(current);
            if (generation == 0 || generationOf(current) != generation || (current & readers_mask) == readers_mask
                || (phase != LeaseMailboxPhase::ready && phase != LeaseMailboxPhase::active && phase != LeaseMailboxPhase::quiescent))
                return std::nullopt;
            if (state_.compare_exchange_weak(current, current + reader_unit, std::memory_order_acq_rel)) return Reader(this, generation);
        }
    }
    bool quiescent(uint32_t generation) const noexcept {
        const auto current = state_.load(std::memory_order_acquire);
        return generation != 0 && generationOf(current) == generation
            && phaseOf(current) == LeaseMailboxPhase::quiescent;
    }
    bool close() noexcept {
        auto current = state_.load(std::memory_order_acquire);
        for (;;) {
            const auto phase = phaseOf(current);
            if ((phase != LeaseMailboxPhase::idle && phase != LeaseMailboxPhase::quiescent) || (current & readers_mask) != 0) return false;
            const auto next = (current & ~phase_mask) | static_cast<uint64_t>(LeaseMailboxPhase::closed);
            if (state_.compare_exchange_weak(current, next, std::memory_order_acq_rel)) return true;
        }
    }
private:
    static constexpr uint64_t phase_mask = 7, cancelled_bit = 8, reader_unit = 16, readers_mask = 0xfffffff0ULL;
    static LeaseMailboxPhase phaseOf(uint64_t value) noexcept { return static_cast<LeaseMailboxPhase>(value & phase_mask); }
    static uint32_t generationOf(uint64_t value) noexcept { return static_cast<uint32_t>(value >> 32); }
    bool transition(uint32_t generation, LeaseMailboxPhase from, LeaseMailboxPhase to) noexcept {
        auto current = state_.load(std::memory_order_acquire);
        for (;;) {
            if (generation == 0 || generationOf(current) != generation || phaseOf(current) != from) return false;
            const auto next = (current & ~phase_mask) | static_cast<uint64_t>(to);
            if (state_.compare_exchange_weak(current, next, std::memory_order_acq_rel)) return true;
        }
    }
    static_assert(std::atomic<uint64_t>::is_always_lock_free);
    std::atomic<uint64_t> state_{};
};
}
