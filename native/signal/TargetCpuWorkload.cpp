#include "Signal.h"
#include <atomic>
#include <cstdint>
#include <ctime>
#include <exception>
#include <fstream>
#include <stdexcept>
#include <string>
#include <sys/syscall.h>
#include <thread>
#include <unistd.h>
#include <vector>

static uint64_t clock_ns(clockid_t clock) {
    timespec value{};
    if (clock_gettime(clock, &value) != 0) throw std::runtime_error("clock read failed");
    return static_cast<uint64_t>(value.tv_sec) * 1000000000ULL + value.tv_nsec;
}

static void check(int status) {
    if (status != 0) throw std::runtime_error("native operation failed: " + std::to_string(status));
}

static char thread_state(int tid) {
    std::ifstream input("/proc/self/task/" + std::to_string(tid) + "/stat");
    std::string line;
    std::getline(input, line);
    auto end = line.rfind(')');
    if (end == std::string::npos || end + 2 >= line.size()) throw std::runtime_error("target state unavailable");
    return line[end + 2];
}

struct Reading {
    uint64_t before{}, after{};
    SignalTargetCpu target{};
    int status{};
};

int main(int argc, char** argv) {
    if (argc != 4) return 2;
    const std::string activity = argv[1], capture = argv[2], prefix = argv[3];
    if ((activity != "busy" && activity != "blocked") || (capture != "baseline" && capture != "remote")) return 2;
    constexpr int count = 10000, warmup = 1000;
    const bool remote = capture == "remote";
    std::vector<Reading> readings(count);
    std::atomic<int> phase{0};
    std::atomic<bool> stop{false};
    int tid{};
    uint64_t self_before{}, self_after{}, checksum{}, elapsed{}, controller_cpu{};
    char initial_state = '?', final_state = '?';
    std::exception_ptr target_error;
    std::string error;
    std::thread target;
    bool installed = false;
    try {
        check(coz_signal_install());
        installed = true;
        target = std::thread([&] {
            bool registered = false;
            try {
                check(coz_signal_register_with_cpu(0, 1, 1));
                registered = true;
                tid = static_cast<int>(syscall(SYS_gettid));
                self_before = clock_ns(CLOCK_THREAD_CPUTIME_ID);
                phase.store(1);
                if (activity == "blocked") {
                    while (!stop.load()) stop.wait(false);
                } else {
                    uint64_t value = 1;
                    while (!stop.load(std::memory_order_relaxed)) value = value * 6364136223846793005ULL + 1;
                    checksum = value;
                }
                self_after = clock_ns(CLOCK_THREAD_CPUTIME_ID);
            } catch (...) { target_error = std::current_exception(); }
            if (registered) {
                while (!stop.load()) std::this_thread::yield();
                try { check(coz_signal_retire(0)); }
                catch (...) { target_error = std::current_exception(); }
            }
            phase.store(3);
        });
        const auto ready_deadline = clock_ns(CLOCK_MONOTONIC) + 1000000000ULL;
        while (phase.load() == 0 && clock_ns(CLOCK_MONOTONIC) < ready_deadline) std::this_thread::yield();
        if (phase.load() != 1) throw std::runtime_error("target registration did not become ready");
        if (activity == "blocked") {
            while (thread_state(tid) != 'S' && clock_ns(CLOCK_MONOTONIC) < ready_deadline) std::this_thread::yield();
            if (thread_state(tid) != 'S') throw std::runtime_error("target did not enter sleeping state");
        }
        initial_state = thread_state(tid);
        for (int i = 0; i < warmup; ++i) {
            SignalTargetCpu ignored{};
            if (remote) check(coz_signal_target_cpu(0, &ignored));
            else { clock_ns(CLOCK_MONOTONIC); clock_ns(CLOCK_MONOTONIC); }
        }
        const auto cpu_start = clock_ns(CLOCK_THREAD_CPUTIME_ID);
        const auto start = clock_ns(CLOCK_MONOTONIC);
        for (auto& reading : readings) {
            reading.before = clock_ns(CLOCK_MONOTONIC);
            if (remote) reading.status = coz_signal_target_cpu(0, &reading.target);
            reading.after = clock_ns(CLOCK_MONOTONIC);
        }
        elapsed = clock_ns(CLOCK_MONOTONIC) - start;
        controller_cpu = clock_ns(CLOCK_THREAD_CPUTIME_ID) - cpu_start;
        final_state = thread_state(tid);
    } catch (const std::exception& failure) { error = failure.what(); }
    stop.store(true);
    stop.notify_all();
    if (target.joinable()) target.join();
    if (target_error) {
        try { std::rethrow_exception(target_error); }
        catch (const std::exception& failure) { error += std::string(" target: ") + failure.what(); }
    }
    if (installed) {
        const auto status = coz_signal_restore();
        if (status != 0) error += " restore: " + std::to_string(status);
    }
    std::ofstream rows(prefix + ".csv");
    rows << "before,after,status,target_before,target_cpu,target_after,resolution,registration\n";
    for (const auto& reading : readings) {
        const auto& value = reading.target;
        rows << reading.before << ',' << reading.after << ',' << reading.status << ',' << value.before_ns << ','
             << value.cpu_ns << ',' << value.after_ns << ',' << value.resolution_ns << ',' << value.registration << '\n';
    }
    rows.close();
    if (!rows) return 3;
    std::ofstream summary(prefix + ".json");
    summary << "{\"activity\":\"" << activity << "\",\"capture\":\"" << capture << "\",\"count\":" << count
            << ",\"warmup\":" << warmup << ",\"elapsedNanoseconds\":" << elapsed
            << ",\"controllerCpuNanoseconds\":" << controller_cpu << ",\"targetSelfBeforeNanoseconds\":" << self_before
            << ",\"targetSelfAfterNanoseconds\":" << self_after << ",\"checksum\":" << checksum
            << ",\"initialState\":\"" << initial_state << "\",\"finalState\":\"" << final_state
            << "\",\"error\":\"" << error << "\"}\n";
    summary.close();
    return summary && error.empty() ? 0 : 1;
}
