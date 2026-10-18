#include "PerfRing.Linux.h"
#include <array>
#include <functional>
#include <stdexcept>
#include <type_traits>

using namespace coz::service;
namespace {
constexpr size_t page = 4096, data = 128;
constexpr uint64_t period = 1000000;

void require(bool condition, const char* message) {
    if (!condition) throw std::runtime_error(message);
}

struct alignas(page) Mapping {
    std::array<std::byte, page + data> bytes{};
    Mapping() {
        metadata().data_offset = page;
        metadata().data_size = data;
    }
    perf_event_mmap_page& metadata() { return *reinterpret_cast<perf_event_mmap_page*>(bytes.data()); }
    void put(uint64_t position, uint64_t value, size_t count) {
        for (size_t index = 0; index < count; ++index)
            bytes[page + static_cast<size_t>((position + index) & (data - 1))] = std::byte((value >> (index * 8)) & 255);
    }
    void sample(uint64_t position, uint64_t timestamp, uint64_t cpu, uint64_t lost = 0) {
        put(position, 9, 4);
        put(position + 4, 2, 2);
        put(position + 6, 56, 2);
        put(position + 8, 4096, 8);
        put(position + 16, 7, 4);
        put(position + 20, 14, 4);
        put(position + 24, timestamp, 8);
        put(position + 32, period, 8);
        put(position + 40, cpu, 8);
        put(position + 48, lost, 8);
    }
    std::optional<LinuxPerfRingPlan> plan(const perf_event_attr& attr = LinuxPerfRingPlan::attributes(period)) {
        return LinuxPerfRingPlan::parse(bytes, page, attr, 7, 14);
    }
};

void plansRequireExactConfiguration() {
    Mapping mapping;
    require(mapping.plan().has_value(), "The canonical attributes and mapping must parse.");
    const std::function<void(perf_event_attr&)> mutations[] = {
        [](perf_event_attr& attr) { attr.read_format = 0; },
        [](perf_event_attr& attr) { attr.sample_type |= PERF_SAMPLE_CPU; },
        [](perf_event_attr& attr) { attr.config = PERF_COUNT_SW_CPU_CLOCK; },
        [](perf_event_attr& attr) { attr.size = sizeof(perf_event_attr); },
        [](perf_event_attr& attr) { attr.wakeup_events = 2; },
        [](perf_event_attr& attr) { attr.watermark = 1; },
        [](perf_event_attr& attr) { attr.disabled = 0; },
        [](perf_event_attr& attr) { attr.inherit = 1; },
        [](perf_event_attr& attr) { attr.exclude_user = 1; },
        [](perf_event_attr& attr) { attr.exclude_kernel = 0; },
        [](perf_event_attr& attr) { attr.mmap = 1; },
        [](perf_event_attr& attr) { attr.comm = 1; },
        [](perf_event_attr& attr) { attr.sample_id_all = 1; },
        [](perf_event_attr& attr) { attr.write_backward = 1; },
        [](perf_event_attr& attr) { attr.freq = 1; },
        [](perf_event_attr& attr) { attr.clockid = CLOCK_MONOTONIC_RAW; },
        [](perf_event_attr& attr) { attr.__reserved_2 = 1; },
        [](perf_event_attr& attr) { attr.sample_period = 0; }
    };
    for (const auto& mutate : mutations) {
        auto attr = LinuxPerfRingPlan::attributes(period);
        mutate(attr);
        require(!mapping.plan(attr), "A noncanonical perf attribute was accepted.");
    }
    require(!LinuxPerfRingPlan::parse(mapping.bytes, page, LinuxPerfRingPlan::attributes(period), 0, 14),
        "A zero process identity was accepted.");
    require(!LinuxPerfRingPlan::parse(std::span(mapping.bytes).first(page), page, LinuxPerfRingPlan::attributes(period), 7, 14),
        "A mapping without a data area was accepted.");
    require(!LinuxPerfRingPlan::parse(std::span(mapping.bytes).subspan(64), page, LinuxPerfRingPlan::attributes(period), 7, 14),
        "A mapping that does not start on a page boundary was accepted.");
    require(!LinuxPerfRingPlan::parse(mapping.bytes, 3072, LinuxPerfRingPlan::attributes(period), 7, 14),
        "A non-power-of-two page size was accepted.");
    require(!LinuxPerfRingPlan::parse(std::span(mapping.bytes).first(page + 96), page, LinuxPerfRingPlan::attributes(period), 7, 14),
        "A data area that disagrees with its metadata was accepted.");
}

void plansRequireUnstartedMetadata() {
    for (int field = 0; field < 4; ++field) {
        Mapping mapping;
        if (field == 0) mapping.metadata().data_offset = page * 2;
        if (field == 1) mapping.metadata().data_size = data / 2;
        if (field == 2) mapping.metadata().data_head = 56;
        if (field == 3) mapping.metadata().data_tail = 56;
        require(!mapping.plan(), "Metadata with a different geometry or a started cursor was accepted.");
    }
}

void readsPublishOnlyAcceptedRecords() {
    Mapping mapping;
    LinuxPerfRing ring(*mapping.plan());
    constexpr uint64_t unwritten = 0xa5a5a5a5a5a5a5a5ULL;
    mapping.metadata().data_tail = unwritten;
    auto empty = ring.read();
    require(!empty.record.sample && empty.snapshot_complete && mapping.metadata().data_tail == unwritten,
        "An empty snapshot must complete without tail publication.");
    mapping.sample(0, 100, 1000000);
    mapping.sample(56, 200, 2000000);
    mapping.metadata().data_head = 112;
    auto first = ring.read();
    require(first.record.sample && !first.snapshot_complete && mapping.metadata().data_tail == 56,
        "One read must publish one record and report the remaining backlog.");
    auto second = ring.read();
    require(second.record.sample && second.snapshot_complete && mapping.metadata().data_tail == 112,
        "The final record in a snapshot must publish its tail and complete the snapshot.");
    mapping.sample(112, 300, 3000000, 2);
    mapping.metadata().data_head = 168;
    mapping.metadata().data_tail = unwritten;
    auto lost = ring.read();
    require(!lost.record.sample && lost.record.error == PerfSampleRingError::lost_records && !lost.snapshot_complete
        && mapping.metadata().data_tail == unwritten && ring.acceptedTail() == 112,
        "A wrapped embedded loss must fail without publishing its record.");
    mapping.sample(112, 300, 3000000);
    require(ring.read().record.error == PerfSampleRingError::lost_records && mapping.metadata().data_tail == unwritten,
        "A terminal adapter failure must persist after source repair.");
}

void malformedSnapshotsNeverComplete() {
    Mapping mapping;
    LinuxPerfRing ring(*mapping.plan());
    mapping.metadata().data_head = data + 8;
    mapping.metadata().data_tail = 0xa5a5a5a5a5a5a5a5ULL;
    auto overrun = ring.read();
    require(overrun.record.error == PerfSampleRingError::ring_bounds && !overrun.snapshot_complete
        && mapping.metadata().data_tail == 0xa5a5a5a5a5a5a5a5ULL, "Producer overrun must fail without tail publication.");
}

void finalCountsRequireTheWholeRead() {
    std::array<std::byte, 16> bytes{};
    bytes[0] = std::byte{5};
    bytes[8] = std::byte{3};
    auto parsed = LinuxPerfCountLoss::parse(16, bytes);
    require(parsed && parsed->value == 5 && parsed->lost == 3, "A complete count/loss read must decode both counters.");
    for (long returned : {-1L, 0L, 8L, 15L, 24L})
        require(!LinuxPerfCountLoss::parse(returned, bytes), "An incomplete or oversized count/loss read was accepted.");
}
}

static_assert(!std::is_copy_constructible_v<LinuxPerfRing> && !std::is_move_constructible_v<LinuxPerfRing>);
static_assert(noexcept(std::declval<LinuxPerfRing&>().read()));

int main() {
    plansRequireExactConfiguration();
    plansRequireUnstartedMetadata();
    readsPublishOnlyAcceptedRecords();
    malformedSnapshotsNeverComplete();
    finalCountsRequireTheWholeRead();
}
