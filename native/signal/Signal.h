#pragma once
#include <cstdint>

#define EXPORT extern "C" __attribute__((visibility("default")))

struct SignalResult {
    uint64_t sequence, outcome, ip, residence_ns, wait_ns;
    int64_t wait_result;
};
struct SignalTiming {
    uint64_t request_started_ns, queue_returned_ns, handler_started_ns, handler_finished_ns, observed_ns;
};
struct SignalTargetCpu {
    uint64_t before_ns, cpu_ns, after_ns, resolution_ns, registration;
};
EXPORT int coz_signal_install();
EXPORT int coz_signal_install_with_wait(int mode);
EXPORT int coz_signal_register(int index, uint64_t begin, uint64_t size);
EXPORT int coz_signal_register_with_cpu(int index, uint64_t begin, uint64_t size);
EXPORT int coz_signal_target_cpu(int index, SignalTargetCpu* observation);
EXPORT int coz_signal_request(int index, uint64_t delay_ns, uint64_t* sequence);
EXPORT int coz_signal_read(int index, uint64_t sequence, SignalResult* result);
EXPORT int coz_signal_read_timed(int index, uint64_t sequence, SignalResult* result, SignalTiming* timing);
EXPORT int coz_signal_retire(int index);
EXPORT int coz_signal_restore();
