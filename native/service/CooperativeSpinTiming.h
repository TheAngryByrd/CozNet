#pragma once
#include "CooperativeTarget.h"

namespace coz::service {
class CooperativeSpinTiming final : public CooperativeTiming {
public:
    uint64_t now() noexcept override;
    TargetWaitStatus wait(uint64_t requested) noexcept override;
    CooperativeCpuReading cpu() noexcept override;
};
}
