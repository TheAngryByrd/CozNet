#include "Signal.h"
#include <atomic>
#include <cerrno>
#include <chrono>
#include <cstdlib>
#include <cstring>
#include <ctime>
#include <thread>

static void require(bool condition, int code) {
    if (!condition) std::_Exit(code);
}

static uint64_t self_cpu() {
    timespec value{};
    require(clock_gettime(CLOCK_THREAD_CPUTIME_ID, &value) == 0, 30);
    return static_cast<uint64_t>(value.tv_sec) * 1000000000ULL + value.tv_nsec;
}

int main(int argc, char** argv) {
    require(argc == 2, 1);
    const bool enabled = std::strcmp(argv[1], "enabled") == 0;
    require(enabled || std::strcmp(argv[1], "disabled") == 0, 2);
    require(coz_signal_install() == 0, 3);
    SignalTargetCpu before{}, after{};
    require(coz_signal_target_cpu(0, &before) == EINVAL, 4);
    std::atomic<int> phase{0};
    uint64_t self_before{}, self_after{};
    std::thread target([&] {
        require((enabled ? coz_signal_register_with_cpu(0, 1, 1) : coz_signal_register(0, 1, 1)) == 0, 5);
        SignalTargetCpu invalid{};
        require(coz_signal_target_cpu(0, &invalid) == EINVAL, 6);
        phase.store(1);
        while (phase.load() == 1) std::this_thread::yield();
        self_before = self_cpu();
        while (self_cpu() - self_before < 10000000) std::atomic_signal_fence(std::memory_order_seq_cst);
        self_after = self_cpu();
        phase.store(3);
        while (phase.load() == 3) std::this_thread::yield();
        require(coz_signal_retire(0) == 0, 7);
    });
    while (phase.load() != 1) std::this_thread::yield();
    require(coz_signal_target_cpu(-1, &before) == EINVAL, 8);
    require(coz_signal_target_cpu(0, nullptr) == EINVAL, 9);
    require(coz_signal_target_cpu(0, &before) == (enabled ? 0 : ENOTSUP), 10);
    phase.store(2);
    while (phase.load() != 3) std::this_thread::yield();
    require(coz_signal_target_cpu(0, &after) == (enabled ? 0 : ENOTSUP), 11);
    if (enabled) {
        require(before.before_ns > 0 && before.before_ns <= before.after_ns && before.after_ns <= after.before_ns
            && after.before_ns <= after.after_ns, 12);
        require(before.cpu_ns <= self_before && after.cpu_ns >= self_after, 13);
        require(after.cpu_ns - before.cpu_ns >= 10000000, 14);
        require(before.registration == 1 && after.registration == before.registration && before.resolution_ns > 0
            && after.resolution_ns == before.resolution_ns, 15);
        uint64_t sequence{};
        require(coz_signal_request(0, 0, &sequence) == 0, 16);
        require(coz_signal_target_cpu(0, &after) == 0, 17);
        SignalResult result{};
        int status;
        do { status = coz_signal_read(0, sequence, &result); } while (status == EAGAIN);
        require(status == 0 && result.sequence == sequence && result.outcome == 1, 18);
        require(coz_signal_target_cpu(0, &after) == 0, 19);
    }
    phase.store(4);
    target.join();
    require(coz_signal_target_cpu(0, &after) == EINVAL, 20);
    require(coz_signal_restore() == 0, 21);
    require(coz_signal_target_cpu(0, &after) == EINVAL, 22);
    return 0;
}
