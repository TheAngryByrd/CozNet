#pragma once
#include <cstdint>

namespace coz::service {
class OwnerPollSchedule {
public:
    static OwnerPollSchedule yielding() noexcept { return OwnerPollSchedule(1); }
    static OwnerPollSchedule bounded() noexcept { return OwnerPollSchedule(64); }
    bool yieldAfterActiveIteration() noexcept {
        ++completed_;
        if (completed_ < limit_) return false;
        reset();
        return true;
    }
    void reset() noexcept { completed_ = 0; }
private:
    explicit OwnerPollSchedule(uint32_t limit) noexcept : limit_(limit) {}
    uint32_t limit_;
    uint32_t completed_{};
};
}
