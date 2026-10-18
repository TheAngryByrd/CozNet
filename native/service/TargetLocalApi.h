#pragma once
#include <stdint.h>
#if defined(_WIN32)
#define COZ_TARGET_LOCAL_EXPORT
#else
#define COZ_TARGET_LOCAL_EXPORT __attribute__((visibility("default")))
#endif
#ifdef __cplusplus
extern "C" {
#endif
typedef struct CozTargetExperiment CozTargetExperiment;
typedef struct CozTargetThread CozTargetThread;
typedef struct {
    uint64_t begin, end;
} CozTargetSpan;
typedef struct {
    uint64_t sequence, observed_global, local_before, amount, requested, time, ip;
    uint32_t kind, detail;
} CozTargetEvent;
typedef struct {
    uint64_t generation, accepted, last_cumulative_cpu, value, lost, close_steps;
    uint64_t deliveries, dispatches, max_handler_ns, total_handler_ns, local, events, started_ns, finished_ns, final_debt;
    int32_t complete, failure, close_outcome, lifecycle_error, retire_error, quiescent;
} CozTargetThreadResult;
typedef struct {
    uint64_t global, sequences, foreign_signals, sigio_fallbacks;
    uint32_t failure;
    int32_t restore_error;
} CozTargetExperimentResult;
COZ_TARGET_LOCAL_EXPORT uint32_t coz_target_layout(uint32_t which);
COZ_TARGET_LOCAL_EXPORT int32_t coz_target_open(uint32_t speedup, uint64_t period, const CozTargetSpan* selected,
    uint32_t selected_count, const CozTargetSpan* eligible, uint32_t eligible_count, CozTargetExperiment** experiment);
COZ_TARGET_LOCAL_EXPORT int32_t coz_target_register(CozTargetExperiment* experiment, uint32_t journal_capacity, CozTargetThread** thread);
COZ_TARGET_LOCAL_EXPORT int32_t coz_target_start(CozTargetThread* thread);
COZ_TARGET_LOCAL_EXPORT int32_t coz_target_finish(CozTargetThread* thread, uint64_t payment_ip, CozTargetThreadResult* result);
COZ_TARGET_LOCAL_EXPORT int32_t coz_target_events(CozTargetThread* thread, CozTargetEvent* events, uint32_t capacity, uint32_t* count);
COZ_TARGET_LOCAL_EXPORT int32_t coz_target_release(CozTargetThread* thread);
COZ_TARGET_LOCAL_EXPORT int32_t coz_target_close(CozTargetExperiment* experiment, CozTargetExperimentResult* result);
#ifdef __cplusplus
}
#endif
