#pragma once
#include <cstdint>

#define EXPORT extern "C" __attribute__((visibility("default")))

struct SignalResult {
    uint64_t sequence, outcome, ip, residence_ns, wait_ns;
    int64_t wait_result;
};
EXPORT int coz_signal_install();
EXPORT int coz_signal_register(int index, uint64_t begin, uint64_t size);
EXPORT int coz_signal_request(int index, uint64_t delay_ns, uint64_t* sequence);
EXPORT int coz_signal_read(int index, uint64_t sequence, SignalResult* result);
EXPORT int coz_signal_retire(int index);
EXPORT int coz_signal_restore();
