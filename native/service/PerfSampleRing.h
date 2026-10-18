#pragma once
#include <cstddef>
#include <cstdint>
#include <optional>
#include <span>

namespace coz::service {
enum class PerfSampleReadFormat { count, count_and_loss };

class PerfSampleRingPlan {
public:
    static std::optional<PerfSampleRingPlan> parse(std::span<const std::byte> bytes,
        uint32_t process, uint32_t thread, uint64_t period, PerfSampleReadFormat format) noexcept {
        const auto size = bytes.size();
        if (size < 64 || (size & (size - 1)) != 0 || process == 0 || thread == 0 || period == 0)
            return std::nullopt;
        if (format != PerfSampleReadFormat::count && format != PerfSampleReadFormat::count_and_loss) return std::nullopt;
        return PerfSampleRingPlan(bytes, process, thread, period, format);
    }
private:
    friend class PerfSampleRingReader;
    PerfSampleRingPlan(std::span<const std::byte> bytes, uint32_t process, uint32_t thread, uint64_t period,
        PerfSampleReadFormat format) noexcept
        : bytes_(bytes), process_(process), thread_(thread), period_(period), format_(format) {}
    std::span<const std::byte> bytes_;
    uint32_t process_, thread_;
    uint64_t period_;
    PerfSampleReadFormat format_;
};

struct PerfCpuSample {
    uint64_t instruction_pointer{}, timestamp{}, period{}, cumulative_cpu{};
    uint32_t process{}, thread{};
    bool operator==(const PerfCpuSample&) const = default;
};

enum class PerfSampleRingError {
    none, head_regressed, ring_bounds, truncated_header, record_length,
    lost_records, throttled, unknown_record, sample_layout, sample_identity,
    sample_time, sample_period, sample_cpu
};

struct PerfSampleRingRead {
    std::optional<PerfCpuSample> sample;
    PerfSampleRingError error{PerfSampleRingError::none};
};

class PerfSampleRingReader {
public:
    explicit PerfSampleRingReader(PerfSampleRingPlan plan, uint64_t initial_tail) noexcept
        : plan_(plan), tail_(initial_tail), head_(initial_tail) {}
    PerfSampleRingReader(const PerfSampleRingReader&) = delete;
    PerfSampleRingReader& operator=(const PerfSampleRingReader&) = delete;
    PerfSampleRingReader(PerfSampleRingReader&&) = delete;
    PerfSampleRingReader& operator=(PerfSampleRingReader&&) = delete;

    uint64_t acceptedTail() const noexcept { return tail_; }

    PerfSampleRingRead read(uint64_t acquired_head) noexcept {
        if (error_ != PerfSampleRingError::none) return {std::nullopt, error_};
        if (acquired_head < head_) return fail(PerfSampleRingError::head_regressed);
        if (acquired_head < tail_ || acquired_head - tail_ > plan_.bytes_.size())
            return fail(PerfSampleRingError::ring_bounds);
        head_ = acquired_head;
        const auto available = head_ - tail_;
        if (available == 0) return {};
        if (available < 8) return fail(PerfSampleRingError::truncated_header);
        const auto type = value(0, 4);
        const auto misc = value(4, 2);
        const auto length = value(6, 2);
        if (length < 8 || length > available) return fail(PerfSampleRingError::record_length);
        if (type == 2) return fail(PerfSampleRingError::lost_records);
        if (type == 5 || type == 6) return fail(PerfSampleRingError::throttled);
        if (type != 9) return fail(PerfSampleRingError::unknown_record);
        const uint64_t expected_length = plan_.format_ == PerfSampleReadFormat::count ? 48 : 56;
        if (length != expected_length || (misc & 7) != 2) return fail(PerfSampleRingError::sample_layout);
        if (plan_.format_ == PerfSampleReadFormat::count_and_loss && value(48, 8) != 0)
            return fail(PerfSampleRingError::lost_records);
        PerfCpuSample sample{value(8, 8), value(24, 8), value(32, 8), value(40, 8),
            static_cast<uint32_t>(value(16, 4)), static_cast<uint32_t>(value(20, 4))};
        if (sample.instruction_pointer == 0 || sample.process != plan_.process_ || sample.thread != plan_.thread_)
            return fail(PerfSampleRingError::sample_identity);
        if (sample.timestamp == 0 || sample.timestamp < prior_time_) return fail(PerfSampleRingError::sample_time);
        if (sample.period != plan_.period_) return fail(PerfSampleRingError::sample_period);
        if (sample.cumulative_cpu < prior_cpu_) return fail(PerfSampleRingError::sample_cpu);
        prior_time_ = sample.timestamp;
        prior_cpu_ = sample.cumulative_cpu;
        tail_ += length;
        return {sample, PerfSampleRingError::none};
    }
private:
    uint64_t value(size_t offset, size_t count) const noexcept {
        uint64_t result = 0;
        const auto mask = plan_.bytes_.size() - 1;
        const auto start = static_cast<size_t>(tail_ & mask);
        for (size_t index = 0; index < count; ++index)
            result |= uint64_t(std::to_integer<uint8_t>(plan_.bytes_[(start + offset + index) & mask])) << (index * 8);
        return result;
    }
    PerfSampleRingRead fail(PerfSampleRingError error) noexcept {
        error_ = error;
        return {std::nullopt, error_};
    }
    PerfSampleRingPlan plan_;
    uint64_t tail_, head_, prior_time_{}, prior_cpu_{};
    PerfSampleRingError error_{PerfSampleRingError::none};
};
}
