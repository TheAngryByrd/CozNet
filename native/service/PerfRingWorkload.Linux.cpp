#include "PerfRing.Linux.h"
#include <array>
#include <cerrno>
#include <cstdio>
#include <ctime>
#include <string>
#include <string_view>
#include <sys/ioctl.h>
#include <sys/mman.h>
#include <sys/syscall.h>
#include <sys/utsname.h>
#include <unistd.h>
#include <vector>

using namespace coz::service;
namespace {
constexpr uint64_t period = 1000000;

const char* errorName(PerfSampleRingError error) {
    constexpr const char* names[] = {"none", "head_regressed", "ring_bounds", "truncated_header", "record_length",
        "lost_records", "throttled", "unknown_record", "sample_layout", "sample_identity", "sample_time",
        "sample_period", "sample_cpu"};
    static_assert(std::size(names) == size_t(PerfSampleRingError::sample_cpu) + 1);
    return names[static_cast<size_t>(error)];
}

uint64_t threadCpu() {
    timespec now{};
    clock_gettime(CLOCK_THREAD_CPUTIME_ID, &now);
    return uint64_t(now.tv_sec) * 1000000000ULL + uint64_t(now.tv_nsec);
}

void burn(uint64_t nanoseconds) {
    volatile uint64_t accumulator = 0;
    const auto end = threadCpu() + nanoseconds;
    while (threadCpu() < end)
        for (int index = 0; index < 1000; ++index) accumulator = accumulator + uint64_t(index);
}

struct Record {
    uint64_t tail{};
    PerfCpuSample sample;
};

struct Drain {
    std::vector<Record> records;
    PerfSampleRingError error{PerfSampleRingError::none};
    bool complete{};
};

void drain(LinuxPerfRing& ring, Drain& totals) {
    for (;;) {
        auto result = ring.read();
        if (result.record.sample) totals.records.push_back({ring.acceptedTail(), *result.record.sample});
        if (result.record.error != PerfSampleRingError::none) {
            totals.error = result.record.error;
            totals.complete = false;
            return;
        }
        if (result.snapshot_complete) {
            totals.complete = true;
            return;
        }
    }
}

struct Epoch {
    bool disabled{}, complete{}, read_complete{};
    uint64_t value{}, lost{}, tail{}, samples{}, last_cpu{};
    PerfSampleRingError error{PerfSampleRingError::none};
    bool captureComplete() const { return complete && read_complete && lost == 0 && error == PerfSampleRingError::none; }
};

Epoch closeEpoch(int fd, LinuxPerfRing& ring, Drain& totals) {
    Epoch epoch;
    epoch.disabled = ioctl(fd, PERF_EVENT_IOC_DISABLE, 0) == 0;
    if (epoch.disabled) drain(ring, totals);
    std::array<std::byte, 16> bytes{};
    const auto returned = read(fd, bytes.data(), bytes.size());
    if (auto counts = LinuxPerfCountLoss::parse(long(returned), bytes)) {
        epoch.read_complete = true;
        epoch.value = counts->value;
        epoch.lost = counts->lost;
    }
    epoch.tail = ring.acceptedTail();
    epoch.samples = totals.records.size();
    epoch.last_cpu = totals.records.empty() ? 0 : totals.records.back().sample.cumulative_cpu;
    epoch.complete = epoch.disabled && totals.complete;
    epoch.error = totals.error;
    return epoch;
}

std::string hex(const void* data, size_t size) {
    std::string text;
    char digit[3];
    for (size_t index = 0; index < size; ++index) {
        std::snprintf(digit, sizeof digit, "%02x", unsigned(static_cast<const unsigned char*>(data)[index]));
        text += digit;
    }
    return text;
}

bool finish(std::FILE* output) {
    const bool written = std::ferror(output) == 0;
    return std::fclose(output) == 0 && written;
}
}

int main(int argc, char** argv) {
    if (argc != 3) {
        std::fprintf(stderr, "usage: PerfRingWorkload normal|overflow|epochs <evidence.json>\n");
        return 64;
    }
    const std::string_view mode = argv[1];
    if (mode != "normal" && mode != "overflow" && mode != "epochs") {
        std::fprintf(stderr, "Unknown mode: %s\n", argv[1]);
        return 64;
    }
    const auto page = size_t(sysconf(_SC_PAGESIZE));
    const size_t data_pages = mode == "overflow" ? 1 : 2;
    const uint32_t process = uint32_t(getpid()), thread = uint32_t(gettid());
    auto attr = LinuxPerfRingPlan::attributes(period);
    utsname host{};
    uname(&host);
    std::FILE* output = std::fopen(argv[2], "w");
    if (!output) return 65;
    std::fprintf(output, "{\"mode\":\"%s\",\"kernel\":\"%s\",\"pageSize\":%zu,\"process\":%u,\"thread\":%u,"
        "\"attributes\":\"%s\"", argv[1], host.release, page, process, thread,
        hex(&attr, PERF_ATTR_SIZE_VER3).c_str());
    const int fd = int(syscall(SYS_perf_event_open, &attr, 0, -1, -1, PERF_FLAG_FD_CLOEXEC));
    if (fd < 0) {
        std::fprintf(output, ",\"available\":false,\"errno\":%d,\"passed\":false}\n", errno);
        finish(output);
        return 2;
    }
    const auto length = page * (1 + data_pages);
    void* address = mmap(nullptr, length, PROT_READ | PROT_WRITE, MAP_SHARED, fd, 0);
    if (address == MAP_FAILED) {
        std::fprintf(output, ",\"available\":true,\"mapErrno\":%d,\"passed\":false}\n", errno);
        finish(output);
        return 1;
    }
    const auto* metadata = static_cast<const perf_event_mmap_page*>(address);
    std::fprintf(output, ",\"mapping\":{\"length\":%zu,\"dataOffset\":%llu,\"dataSize\":%llu,\"version\":%u,\"compatVersion\":%u}",
        length, (unsigned long long)metadata->data_offset, (unsigned long long)metadata->data_size,
        metadata->version, metadata->compat_version);
    auto plan = LinuxPerfRingPlan::parse({static_cast<std::byte*>(address), length}, page, attr, process, thread);
    if (!plan) {
        std::fprintf(output, ",\"available\":true,\"planAccepted\":false,\"passed\":false}\n");
        finish(output);
        return 1;
    }
    LinuxPerfRing ring(*plan);
    Drain totals;
    std::vector<Epoch> epochs;
    bool enabled = true, passed = true;
    const int epoch_count = mode == "epochs" ? 3 : 1;
    for (int epoch = 0; epoch < epoch_count && enabled; ++epoch) {
        enabled = ioctl(fd, PERF_EVENT_IOC_ENABLE, 0) == 0;
        if (!enabled) break;
        if (mode == "overflow") {
            burn(500000000);
        } else {
            for (int slice = 0; slice < (mode == "normal" ? 10 : 4); ++slice) {
                burn(mode == "normal" ? 50000000 : 25000000);
                drain(ring, totals);
                if (totals.error != PerfSampleRingError::none) break;
            }
        }
        epochs.push_back(closeEpoch(fd, ring, totals));
        if (!epochs.back().captureComplete()) break;
    }
    const auto data_size = page * data_pages;
    uint64_t prior_value = 0, prior_samples = 0;
    for (const auto& epoch : epochs) {
        passed = passed && epoch.disabled && epoch.complete && epoch.read_complete && epoch.value > prior_value
            && epoch.samples > prior_samples && epoch.value >= epoch.last_cpu;
        passed = passed && (mode == "overflow" ? epoch.lost > 0 && !epoch.captureComplete() : epoch.captureComplete());
        prior_value = epoch.value;
        prior_samples = epoch.samples;
    }
    passed = passed && enabled && int(epochs.size()) == epoch_count;
    if (mode != "overflow") passed = passed && ring.acceptedTail() > data_size;
    std::fprintf(output, ",\"available\":true,\"planAccepted\":true,\"samples\":%zu,\"acceptedTail\":%llu,"
        "\"wrapped\":%s,\"drainError\":\"%s\",\"epochs\":[", totals.records.size(), (unsigned long long)ring.acceptedTail(),
        ring.acceptedTail() > data_size ? "true" : "false", errorName(totals.error));
    for (size_t index = 0; index < epochs.size(); ++index) {
        const auto& epoch = epochs[index];
        std::fprintf(output, "%s{\"disabled\":%s,\"drainComplete\":%s,\"readComplete\":%s,\"value\":%llu,\"lost\":%llu,"
            "\"captureComplete\":%s,\"acceptedTail\":%llu,\"samples\":%llu,\"lastCumulativeCpu\":%llu,\"error\":\"%s\"}",
            index == 0 ? "" : ",", epoch.disabled ? "true" : "false", epoch.complete ? "true" : "false",
            epoch.read_complete ? "true" : "false", (unsigned long long)epoch.value, (unsigned long long)epoch.lost,
            epoch.captureComplete() ? "true" : "false", (unsigned long long)epoch.tail, (unsigned long long)epoch.samples,
            (unsigned long long)epoch.last_cpu, errorName(epoch.error));
    }
    std::fprintf(output, "],\"records\":[");
    for (size_t index = 0; index < totals.records.size(); ++index) {
        const auto& record = totals.records[index];
        std::fprintf(output, "%s[%llu,%llu,%llu,%llu,%llu]", index == 0 ? "" : ",", (unsigned long long)record.tail,
            (unsigned long long)record.sample.instruction_pointer, (unsigned long long)record.sample.timestamp,
            (unsigned long long)record.sample.period, (unsigned long long)record.sample.cumulative_cpu);
    }
    std::fprintf(output, "],\"recordFields\":[\"acceptedTail\",\"ip\",\"time\",\"period\",\"cumulativeCpu\"],\"passed\":%s}\n",
        passed ? "true" : "false");
    const bool written = finish(output);
    munmap(address, length);
    close(fd);
    return passed && written ? 0 : 1;
}
