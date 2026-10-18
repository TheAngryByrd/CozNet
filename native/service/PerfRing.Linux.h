#pragma once
#include "PerfSampleRing.h"
#include <bit>
#include <cstddef>
#include <cstring>
#include <ctime>
#include <linux/perf_event.h>

namespace coz::service {
class LinuxPerfRingPlan {
public:
    static constexpr uint64_t sample_type = PERF_SAMPLE_IP | PERF_SAMPLE_TID | PERF_SAMPLE_TIME |
        PERF_SAMPLE_PERIOD | PERF_SAMPLE_READ;
    static perf_event_attr attributes(uint64_t period) noexcept {
        perf_event_attr attr{};
        attr.type = PERF_TYPE_SOFTWARE;
        attr.size = PERF_ATTR_SIZE_VER3;
        attr.config = PERF_COUNT_SW_TASK_CLOCK;
        attr.sample_period = period;
        attr.sample_type = sample_type;
        attr.read_format = PERF_FORMAT_LOST;
        attr.disabled = 1;
        attr.exclude_kernel = 1;
        attr.exclude_hv = 1;
        attr.use_clockid = 1;
        attr.clockid = CLOCK_MONOTONIC;
        attr.wakeup_events = 1;
        return attr;
    }
    static std::optional<LinuxPerfRingPlan> parse(std::span<std::byte> mapping, size_t page_size,
        const perf_event_attr& attr, uint32_t process, uint32_t thread) noexcept {
        if (std::endian::native != std::endian::little || page_size < sizeof(perf_event_mmap_page) ||
            (page_size & (page_size - 1)) != 0 || mapping.size() <= page_size ||
            reinterpret_cast<uintptr_t>(mapping.data()) % page_size != 0) return std::nullopt;
        const auto expected = attributes(attr.sample_period);
        if (attr.sample_period == 0 || std::memcmp(&attr, &expected, sizeof(perf_event_attr)) != 0) return std::nullopt;
        auto* metadata = reinterpret_cast<perf_event_mmap_page*>(mapping.data());
        if (metadata->data_offset != page_size || metadata->data_size != mapping.size() - page_size) return std::nullopt;
        auto source = PerfSampleRingPlan::parse(mapping.subspan(page_size), process, thread, attr.sample_period,
            PerfSampleReadFormat::count_and_loss);
        if (!source || __atomic_load_n(&metadata->data_head, __ATOMIC_ACQUIRE) != 0 ||
            __atomic_load_n(&metadata->data_tail, __ATOMIC_RELAXED) != 0) return std::nullopt;
        return LinuxPerfRingPlan(metadata, *source);
    }
private:
    static_assert(offsetof(perf_event_attr, read_format) == 32 && offsetof(perf_event_attr, clockid) == 92);
    static_assert(PERF_ATTR_SIZE_VER3 == 96 && sizeof(perf_event_attr) >= PERF_ATTR_SIZE_VER3);
    friend class LinuxPerfRing;
    LinuxPerfRingPlan(perf_event_mmap_page* metadata, PerfSampleRingPlan source) noexcept
        : metadata_(metadata), source_(source) {}
    perf_event_mmap_page* metadata_;
    PerfSampleRingPlan source_;
};

struct LinuxPerfRingRead {
    PerfSampleRingRead record;
    bool snapshot_complete{};
};

class LinuxPerfRing {
public:
    explicit LinuxPerfRing(LinuxPerfRingPlan plan) noexcept : metadata_(plan.metadata_), reader_(plan.source_, 0) {}
    LinuxPerfRing(const LinuxPerfRing&) = delete;
    LinuxPerfRing& operator=(const LinuxPerfRing&) = delete;
    LinuxPerfRing(LinuxPerfRing&&) = delete;
    LinuxPerfRing& operator=(LinuxPerfRing&&) = delete;
    LinuxPerfRingRead read() noexcept {
        const auto head = __atomic_load_n(&metadata_->data_head, __ATOMIC_ACQUIRE);
        auto result = reader_.read(head);
        if (result.sample) {
            // The perf ABI requires all record reads to finish before tail publication.
            __atomic_thread_fence(__ATOMIC_SEQ_CST);
            __atomic_store_n(&metadata_->data_tail, reader_.acceptedTail(), __ATOMIC_RELAXED);
        }
        return {result, result.error == PerfSampleRingError::none && reader_.acceptedTail() == head};
    }
    uint64_t acceptedTail() const noexcept { return reader_.acceptedTail(); }
private:
    static_assert(__atomic_always_lock_free(sizeof(uint64_t), nullptr));
    perf_event_mmap_page* metadata_;
    PerfSampleRingReader reader_;
};

struct LinuxPerfCountLoss {
    uint64_t value{}, lost{};
    static std::optional<LinuxPerfCountLoss> parse(long returned, std::span<const std::byte, 16> bytes) noexcept {
        if (returned != 16) return std::nullopt;
        LinuxPerfCountLoss result;
        std::memcpy(&result.value, bytes.data(), 8);
        std::memcpy(&result.lost, bytes.data() + 8, 8);
        return result;
    }
};
}
