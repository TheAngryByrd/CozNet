#pragma once
#include <stdint.h>
#if defined(_WIN32)
#define COZ_COOPERATIVE_CALL __cdecl
#if defined(COZ_COOPERATIVE_BUILD)
#define COZ_COOPERATIVE_EXPORT __declspec(dllexport)
#else
#define COZ_COOPERATIVE_EXPORT __declspec(dllimport)
#endif
#else
#define COZ_COOPERATIVE_CALL
#define COZ_COOPERATIVE_EXPORT __attribute__((visibility("default")))
#endif
#ifdef __cplusplus
extern "C" {
#endif
typedef struct CozCooperativeSession CozCooperativeSession;
enum CozCooperativeStatus {
    COZ_COOPERATIVE_OK = 0,
    COZ_COOPERATIVE_INVALID = 1,
    COZ_COOPERATIVE_ROLE = 2,
    COZ_COOPERATIVE_BUSY = 3,
    COZ_COOPERATIVE_BUFFER = 4,
    COZ_COOPERATIVE_CLOCK = 5,
    COZ_COOPERATIVE_MEMORY = 6,
    COZ_COOPERATIVE_FAILURE = 7
};
enum CozCooperativePollOutcome {
    COZ_COOPERATIVE_IDLE = 0,
    COZ_COOPERATIVE_SERVICED = 1,
    COZ_COOPERATIVE_REJECTED = 2,
    COZ_COOPERATIVE_FAILED = 3
};
typedef struct {
    uint64_t ordinal, requested, armed, started, finished, waited;
    uint64_t worker, code_version, site, wait_status, valid;
} CozCooperativeChild;
typedef struct {
    uint64_t generation, command, budget, children, measured, visibility, stop, error;
    int64_t transport_error;
    uint64_t may_have_delivery, captured_ns;
} CozCooperativeSnapshot;
enum CozCooperativeCpuStatus {
    COZ_COOPERATIVE_CPU_UNSUPPORTED = 0,
    COZ_COOPERATIVE_CPU_OBSERVED = 1,
    COZ_COOPERATIVE_CPU_FAILED = 2
};

enum CozCooperativeCpuClock {
    COZ_COOPERATIVE_CPU_CLOCK_NONE = 0,
    COZ_COOPERATIVE_CPU_CLOCK_LINUX_THREAD = 1
};

typedef struct {
    uint64_t status, clock, nanoseconds, resolution;
    int64_t error;
} CozCooperativeCpuReading;

typedef struct {
    uint64_t generation, command, ordinal, worker, code_version, site, requested;
    CozCooperativeCpuReading before, after;
} CozCooperativeCpuChild;

COZ_COOPERATIVE_EXPORT int32_t COZ_COOPERATIVE_CALL coz_cooperative_register_cpu(CozCooperativeSession* session,
    uint32_t slot, uint64_t worker, uint64_t site);
COZ_COOPERATIVE_EXPORT int32_t COZ_COOPERATIVE_CALL coz_cooperative_cpu_snapshot(CozCooperativeSession* session,
    uint32_t slot, uint64_t generation, CozCooperativeCpuChild* children, uint32_t capacity, uint32_t* required);

COZ_COOPERATIVE_EXPORT int32_t COZ_COOPERATIVE_CALL coz_cooperative_open(uint64_t code_version, uint32_t targets, uint32_t capacity, CozCooperativeSession** session);
COZ_COOPERATIVE_EXPORT int32_t COZ_COOPERATIVE_CALL coz_cooperative_register(CozCooperativeSession* session, uint32_t slot, uint64_t worker, uint64_t site);
COZ_COOPERATIVE_EXPORT int32_t COZ_COOPERATIVE_CALL coz_cooperative_poll(CozCooperativeSession* session, uint32_t slot, uint64_t worker, uint64_t code_version, uint64_t site, uint32_t* result);
COZ_COOPERATIVE_EXPORT int32_t COZ_COOPERATIVE_CALL coz_cooperative_submit(CozCooperativeSession* session, uint32_t slot, uint64_t command, uint64_t budget, uint64_t* generation);
COZ_COOPERATIVE_EXPORT int32_t COZ_COOPERATIVE_CALL coz_cooperative_cancel(CozCooperativeSession* session, uint32_t slot, uint64_t generation);
COZ_COOPERATIVE_EXPORT int32_t COZ_COOPERATIVE_CALL coz_cooperative_snapshot(CozCooperativeSession* session, uint32_t slot, uint64_t generation, CozCooperativeSnapshot* snapshot, CozCooperativeChild* children, uint32_t capacity);
COZ_COOPERATIVE_EXPORT int32_t COZ_COOPERATIVE_CALL coz_cooperative_release(CozCooperativeSession* session, uint32_t slot, uint64_t generation);
COZ_COOPERATIVE_EXPORT int32_t COZ_COOPERATIVE_CALL coz_cooperative_retire(CozCooperativeSession* session, uint32_t slot);
COZ_COOPERATIVE_EXPORT int32_t COZ_COOPERATIVE_CALL coz_cooperative_revoke(CozCooperativeSession* session);
COZ_COOPERATIVE_EXPORT int32_t COZ_COOPERATIVE_CALL coz_cooperative_clock(CozCooperativeSession* session, uint64_t* nanoseconds);
COZ_COOPERATIVE_EXPORT int32_t COZ_COOPERATIVE_CALL coz_cooperative_close(CozCooperativeSession* session);
#ifdef __cplusplus
}
#endif
