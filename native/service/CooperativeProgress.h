#pragma once
#include <atomic>
#include <cstdint>
#include <optional>

namespace coz::service {
enum class CooperativeProgressPhase : uint32_t {
    pending = 1, poll_entered, code_guard_acquired, child_admitted,
    wait_entered, wait_returned, child_published, returning, quiesced, failed
};

struct CooperativeProgressSnapshot {
    uint32_t generation, ordinal;
    CooperativeProgressPhase phase;
};

class CooperativeProgress {
public:
    void begin(uint32_t generation) noexcept {
        value_.store((uint64_t{generation} << 32) | static_cast<uint32_t>(CooperativeProgressPhase::pending),
            std::memory_order_release);
    }

    void record(uint32_t generation, CooperativeProgressPhase phase, uint64_t ordinal = 0) noexcept {
        auto current = value_.load(std::memory_order_relaxed);
        for (;;) {
            if (static_cast<uint32_t>(current >> 32) != generation || generation == 0 || ordinal > 100000) return;
            const auto previous = static_cast<CooperativeProgressPhase>(current & 15);
            if (previous == CooperativeProgressPhase::quiesced ||
                (previous == CooperativeProgressPhase::failed && phase != CooperativeProgressPhase::quiesced)) return;
            const auto child = ordinal == 0 ? (current >> 4) & 0x1ffff : ordinal;
            const auto next = (uint64_t{generation} << 32) | (child << 4) | static_cast<uint32_t>(phase);
            if (value_.compare_exchange_weak(current, next, std::memory_order_release, std::memory_order_relaxed)) return;
        }
    }

    std::optional<CooperativeProgressSnapshot> read(uint32_t generation) const noexcept {
        const auto value = value_.load(std::memory_order_acquire);
        if (generation == 0 || static_cast<uint32_t>(value >> 32) != generation) return std::nullopt;
        return CooperativeProgressSnapshot{generation, static_cast<uint32_t>((value >> 4) & 0x1ffff),
            static_cast<CooperativeProgressPhase>(value & 15)};
    }

private:
    std::atomic<uint64_t> value_{};
};
static_assert(std::atomic<uint64_t>::is_always_lock_free);
}
