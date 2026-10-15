#include "CooperativeSpinTiming.h"
#include <limits>
#include <cerrno>
#if defined(_WIN32)
#define NOMINMAX
#include <windows.h>
#else
#include <time.h>
#endif

namespace coz::service {
uint64_t CooperativeSpinTiming::now() noexcept {
    constexpr uint64_t billion = 1000000000;
#if defined(_WIN32)
    LARGE_INTEGER frequency{}, counter{};
    if (!QueryPerformanceFrequency(&frequency) || !QueryPerformanceCounter(&counter)
        || frequency.QuadPart <= 0 || frequency.QuadPart > static_cast<int64_t>(billion)
        || counter.QuadPart <= 0) return 0;
    const auto ticks = static_cast<uint64_t>(counter.QuadPart);
    const auto rate = static_cast<uint64_t>(frequency.QuadPart);
    const auto seconds = ticks / rate;
    const auto fraction = (ticks % rate) * billion / rate;
#else
    timespec value{};
    if (clock_gettime(CLOCK_MONOTONIC, &value) != 0 || value.tv_sec < 0
        || value.tv_nsec < 0 || value.tv_nsec >= static_cast<long>(billion)) return 0;
    const auto seconds = static_cast<uint64_t>(value.tv_sec);
    const auto fraction = static_cast<uint64_t>(value.tv_nsec);
#endif
    if (seconds > (std::numeric_limits<uint64_t>::max() - fraction) / billion) return 0;
    return seconds * billion + fraction;
}

CooperativeCpuReading CooperativeSpinTiming::cpu() noexcept {
#if defined(__linux__)
    timespec value{}, resolution{};
    if (clock_gettime(CLOCK_THREAD_CPUTIME_ID, &value) != 0
        || clock_getres(CLOCK_THREAD_CPUTIME_ID, &resolution) != 0)
        return {CooperativeCpuStatus::failed, CooperativeCpuClock::linux_thread, 0, 0, errno};
    const auto convert = [](timespec input) -> std::optional<uint64_t> {
        constexpr uint64_t billion = 1000000000;
        if (input.tv_sec < 0 || input.tv_nsec < 0 || input.tv_nsec >= static_cast<long>(billion)) return std::nullopt;
        const auto seconds = static_cast<uint64_t>(input.tv_sec);
        const auto fraction = static_cast<uint64_t>(input.tv_nsec);
        if (seconds > (std::numeric_limits<uint64_t>::max() - fraction) / billion) return std::nullopt;
        return seconds * billion + fraction;
    };
    const auto cpu = convert(value), precision = convert(resolution);
    if (!cpu || !precision || *precision == 0)
        return {CooperativeCpuStatus::failed, CooperativeCpuClock::linux_thread, 0, 0, EOVERFLOW};
    return {CooperativeCpuStatus::observed, CooperativeCpuClock::linux_thread, *cpu, *precision, 0};
#else
    return {};
#endif
}

TargetWaitStatus CooperativeSpinTiming::wait(uint64_t requested) noexcept {
    if (requested == 0 || requested > 50000) return TargetWaitStatus::failed;
    const auto started = now();
    if (started == 0) return TargetWaitStatus::failed;
    auto previous = started;
    for (uint32_t attempt = 0; attempt < 1000000; ++attempt) {
        const auto current = now();
        if (current == 0 || current < previous) return TargetWaitStatus::failed;
        if (current - started >= requested) return TargetWaitStatus::completed;
        previous = current;
    }
    return TargetWaitStatus::failed;
}
}
