#include "PerfSampleRing.h"
#include <array>
#include <limits>
#include <stdexcept>
#include <type_traits>

using namespace coz::service;
namespace {
void require(bool condition, const char* message) {
    if (!condition) throw std::runtime_error(message);
}

struct Fixture {
    std::array<std::byte, 128> bytes{};
    PerfSampleReadFormat format{PerfSampleReadFormat::count};
    uint64_t length() const { return format == PerfSampleReadFormat::count ? 48 : 56; }
    void put(uint64_t position, uint64_t value, size_t count) {
        for (size_t index = 0; index < count; ++index)
            bytes[static_cast<size_t>((position + index) & 127)] = std::byte((value >> (index * 8)) & 255);
    }
    void sample(uint64_t position, uint64_t timestamp = 100, uint64_t cpu = 1000000, uint64_t lost = 0) {
        put(position, 9, 4);
        put(position + 4, 2, 2);
        put(position + 6, length(), 2);
        put(position + 8, 4096, 8);
        put(position + 16, 7, 4);
        put(position + 20, 14, 4);
        put(position + 24, timestamp, 8);
        put(position + 32, 1000000, 8);
        put(position + 40, cpu, 8);
        if (format == PerfSampleReadFormat::count_and_loss) put(position + 48, lost, 8);
    }
    PerfSampleRingPlan plan() {
        auto parsed = PerfSampleRingPlan::parse(bytes, 7, 14, 1000000, format);
        require(parsed.has_value(), "The valid fixture plan did not parse.");
        return *parsed;
    }
};

void plansRejectInvalidInputs() {
    Fixture fixture;
    require(!PerfSampleRingPlan::parse({}, 7, 14, 1, PerfSampleReadFormat::count), "An empty ring was accepted.");
    require(!PerfSampleRingPlan::parse(std::span(fixture.bytes).first(32), 7, 14, 1, PerfSampleReadFormat::count), "A ring smaller than one sample was accepted.");
    require(!PerfSampleRingPlan::parse(std::span(fixture.bytes).first(96), 7, 14, 1, PerfSampleReadFormat::count), "A non-power-of-two ring was accepted.");
    require(!PerfSampleRingPlan::parse(fixture.bytes, 0, 14, 1, PerfSampleReadFormat::count), "A zero process identity was accepted.");
    require(!PerfSampleRingPlan::parse(fixture.bytes, 7, 0, 1, PerfSampleReadFormat::count), "A zero thread identity was accepted.");
    require(!PerfSampleRingPlan::parse(fixture.bytes, 7, 14, 0, PerfSampleReadFormat::count), "A zero period was accepted.");
    require(PerfSampleRingPlan::parse(std::span(fixture.bytes).first(64), 7, 14, 80000, PerfSampleReadFormat::count).has_value(),
        "The minimum ring and explicit synthetic period must parse.");
    require(PerfSampleRingPlan::parse(std::span(fixture.bytes).first(64), 7, 14, 1, PerfSampleReadFormat::count_and_loss).has_value(),
        "The minimum ring must admit one count-and-loss sample.");
    require(!PerfSampleRingPlan::parse(fixture.bytes, 7, 14, 1, static_cast<PerfSampleReadFormat>(2)),
        "An unnamed read format was accepted.");
}

void everyWrapPositionDecodesOwnedValues() {
    for (uint64_t position = 0; position < 128; ++position) {
        Fixture fixture;
        fixture.sample(position);
        PerfSampleRingReader reader(fixture.plan(), position);
        auto result = reader.read(position + 48);
        require(result.error == PerfSampleRingError::none && result.sample == PerfCpuSample{4096, 100, 1000000, 1000000, 7, 14},
            "A wrapped sample changed its decoded fields.");
        require(reader.acceptedTail() == position + 48, "Accepted tail must advance by exactly one record.");
        fixture.bytes.fill(std::byte{255});
        require(result.sample->instruction_pointer == 4096 && result.sample->cumulative_cpu == 1000000,
            "The decoded sample must own its values independently of ring reuse.");
        auto empty = reader.read(position + 48);
        require(!empty.sample && empty.error == PerfSampleRingError::none, "An empty ring must not replay the last sample.");
    }
}

void oneRecordPerCallAndProducerAdvance() {
    Fixture fixture;
    fixture.sample(0);
    fixture.sample(48, 200, 2000000);
    PerfSampleRingReader reader(fixture.plan(), 0);
    auto first = reader.read(96);
    require(first.sample && first.sample->timestamp == 100 && reader.acceptedTail() == 48,
        "One read must consume only the first record in a backlog.");
    auto second = reader.read(96);
    require(second.sample && second.sample->timestamp == 200 && reader.acceptedTail() == 96,
        "A repeated head must permit the next unconsumed record.");
    fixture.sample(96, 300, 3000000);
    auto third = reader.read(144);
    require(third.sample && third.sample->timestamp == 300 && reader.acceptedTail() == 144,
        "A later producer head must permit a wrapped record after accepted progress.");
}

void fullWidthFieldsAndLostRecords() {
    Fixture fixture;
    constexpr uint64_t ip = 0x12345678abcdef00ULL, stamp = 0xfedcba9876543210ULL;
    fixture.sample(120, stamp, std::numeric_limits<uint64_t>::max());
    fixture.put(128, ip, 8);
    PerfSampleRingReader reader(fixture.plan(), 120);
    const auto result = reader.read(168);
    require(result.sample && result.sample->instruction_pointer == ip && result.sample->timestamp == stamp
        && result.sample->cumulative_cpu == std::numeric_limits<uint64_t>::max(),
        "All eight bytes of wrapped IP, time, and CPU fields must survive decoding.");
    for (uint64_t type : {2ULL, 5ULL, 6ULL}) {
        Fixture loss;
        loss.put(0, type, 4);
        loss.put(6, type == 2 ? 24 : 32, 2);
        loss.put(16, 3, 8);
        PerfSampleRingReader failed(loss.plan(), 0);
        auto record = failed.read(type == 2 ? 24 : 32);
        require(!record.sample && record.error == (type == 2 ? PerfSampleRingError::lost_records : PerfSampleRingError::throttled)
            && failed.acceptedTail() == 0, "A valid loss or throttle record must terminate source acceptance.");
    }
}

void countAndLossLayoutIsExplicit() {
    for (uint64_t position = 0; position < 128; ++position) {
        Fixture fixture;
        fixture.format = PerfSampleReadFormat::count_and_loss;
        fixture.sample(position);
        PerfSampleRingReader reader(fixture.plan(), position);
        auto result = reader.read(position + 56);
        require(result.error == PerfSampleRingError::none && result.sample == PerfCpuSample{4096, 100, 1000000, 1000000, 7, 14}
            && reader.acceptedTail() == position + 56, "A wrapped count-and-loss sample must decode and advance by 56 bytes.");
    }
    for (bool count_record_in_loss_plan : {false, true}) {
        Fixture fixture;
        fixture.format = count_record_in_loss_plan ? PerfSampleReadFormat::count : PerfSampleReadFormat::count_and_loss;
        fixture.sample(0);
        fixture.format = count_record_in_loss_plan ? PerfSampleReadFormat::count_and_loss : PerfSampleReadFormat::count;
        PerfSampleRingReader reader(fixture.plan(), 0);
        require(reader.read(64).error == PerfSampleRingError::sample_layout && reader.acceptedTail() == 0,
            "A record length from the other read format must fail before acceptance.");
    }
    for (uint64_t lost : {1ULL, 1ULL << 63}) {
        Fixture fixture;
        fixture.format = PerfSampleReadFormat::count_and_loss;
        fixture.sample(100, 100, 1000000, lost);
        PerfSampleRingReader reader(fixture.plan(), 100);
        require(reader.read(156).error == PerfSampleRingError::lost_records && reader.acceptedTail() == 100,
            "A wrapped nonzero embedded loss counter must fail before acceptance.");
        fixture.sample(100);
        require(reader.read(156).error == PerfSampleRingError::lost_records && reader.acceptedTail() == 100,
            "Clearing an embedded loss counter cannot repair a terminal loss failure.");
    }
}

void malformedRecordsFailBeforeAcceptance() {
    struct Mutation { size_t offset, count; uint64_t value; PerfSampleRingError error; };
    constexpr Mutation mutations[] = {
        {0, 4, 2, PerfSampleRingError::lost_records},
        {0, 4, 5, PerfSampleRingError::throttled},
        {0, 4, 6, PerfSampleRingError::throttled},
        {0, 4, 99, PerfSampleRingError::unknown_record},
        {6, 2, 0, PerfSampleRingError::record_length},
        {6, 2, 7, PerfSampleRingError::record_length},
        {6, 2, 49, PerfSampleRingError::record_length},
        {6, 2, 40, PerfSampleRingError::sample_layout},
        {4, 2, 1, PerfSampleRingError::sample_layout},
        {8, 8, 0, PerfSampleRingError::sample_identity},
        {16, 4, 8, PerfSampleRingError::sample_identity},
        {20, 4, 15, PerfSampleRingError::sample_identity},
        {24, 8, 0, PerfSampleRingError::sample_time},
        {32, 8, 0, PerfSampleRingError::sample_period},
        {32, 8, 80000, PerfSampleRingError::sample_period}
    };
    for (const auto& mutation : mutations) {
        Fixture fixture;
        fixture.sample(0);
        fixture.put(mutation.offset, mutation.value, mutation.count);
        PerfSampleRingReader reader(fixture.plan(), 0);
        auto failed = reader.read(48);
        require(!failed.sample && failed.error == mutation.error && reader.acceptedTail() == 0,
            "A malformed sample must fail with its specific error before tail advancement.");
        fixture.sample(0);
        auto retry = reader.read(48);
        require(!retry.sample && retry.error == mutation.error && reader.acceptedTail() == 0,
            "Repairing source bytes cannot repair a terminal decoder failure.");
    }
}

void monotonicityFailurePreservesAcceptedPrefix() {
    for (bool cpu_regresses : {false, true}) {
        Fixture fixture;
        fixture.sample(0);
        fixture.sample(48, cpu_regresses ? 200 : 99, cpu_regresses ? 999999 : 2000000);
        PerfSampleRingReader reader(fixture.plan(), 0);
        require(reader.read(96).sample.has_value(), "The first prefix record must be accepted.");
        const auto expected = cpu_regresses ? PerfSampleRingError::sample_cpu : PerfSampleRingError::sample_time;
        auto failed = reader.read(96);
        require(!failed.sample && failed.error == expected && reader.acceptedTail() == 48,
            "A regressed observation must preserve the accepted prefix and reject its own record.");
        fixture.sample(48, 200, 2000000);
        require(reader.read(96).error == expected && reader.acceptedTail() == 48,
            "A terminal monotonicity failure cannot advance after source repair.");
    }
    Fixture fixture;
    fixture.sample(0);
    fixture.sample(48);
    PerfSampleRingReader reader(fixture.plan(), 0);
    require(reader.read(96).sample && reader.read(96).sample,
        "Equal timestamps and counters are allowed; byte positions provide record identity.");
}

void headBoundsAndTruncatedHeadersReject() {
    for (uint64_t head = 1; head < 8; ++head) {
        Fixture fixture;
        PerfSampleRingReader reader(fixture.plan(), 0);
        require(reader.read(head).error == PerfSampleRingError::truncated_header && reader.acceptedTail() == 0,
            "A partial header must fail before tail advancement.");
    }
    Fixture fixture;
    PerfSampleRingReader overrun(fixture.plan(), 0);
    require(overrun.read(129).error == PerfSampleRingError::ring_bounds, "Producer overrun must reject.");
    fixture.sample(0);
    fixture.sample(48, 200, 2000000);
    PerfSampleRingReader regression(fixture.plan(), 0);
    require(regression.read(96).sample.has_value(), "The initial head must admit its first sample.");
    require(regression.read(95).error == PerfSampleRingError::head_regressed && regression.acceptedTail() == 48,
        "Producer head regression must reject even when head remains above accepted tail.");
    const auto limit = std::numeric_limits<uint64_t>::max();
    Fixture large;
    large.sample(limit - 48);
    PerfSampleRingReader near_limit(large.plan(), limit - 48);
    require(near_limit.read(limit).sample && near_limit.acceptedTail() == limit,
        "A record ending at the maximum cursor must decode without arithmetic overflow.");
    require(near_limit.read(0).error == PerfSampleRingError::head_regressed,
        "Logical cursor rollover must fail rather than reuse earlier positions.");
}
}

static_assert(!std::is_copy_constructible_v<PerfSampleRingReader> && !std::is_move_constructible_v<PerfSampleRingReader>);
static_assert(noexcept(std::declval<PerfSampleRingReader&>().read(0)));

int main() {
    plansRejectInvalidInputs();
    everyWrapPositionDecodesOwnedValues();
    oneRecordPerCallAndProducerAdvance();
    fullWidthFieldsAndLostRecords();
    countAndLossLayoutIsExplicit();
    malformedRecordsFailBeforeAcceptance();
    monotonicityFailurePreservesAcceptedPrefix();
    headBoundsAndTruncatedHeadersReject();
}
