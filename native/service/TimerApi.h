#pragma once
#include <stdint.h>
#if defined(_WIN32)
#if defined(COZ_TIMER_BUILD)
#define COZ_TIMER_EXPORT __declspec(dllexport)
#else
#define COZ_TIMER_EXPORT __declspec(dllimport)
#endif
#else
#define COZ_TIMER_EXPORT __attribute__((visibility("default")))
#endif
#ifdef __cplusplus
extern "C" {
#endif
typedef struct CozTimerSession CozTimerSession;
typedef struct {
    uint64_t ordinal, requested, armed, started, finished, waited, ip, wait_status, valid;
} CozTimerChild;
typedef struct {
    uint64_t generation, command, budget, children, measured, visibility, stop, error;
    int64_t transport_error;
    uint64_t may_have_delivery, captured_ns;
    int64_t backend_error;
} CozTimerSnapshot;
COZ_TIMER_EXPORT int32_t coz_timer_open(uint64_t begin, uint64_t size, uint32_t targets, uint32_t capacity, CozTimerSession** session);
COZ_TIMER_EXPORT int32_t coz_timer_register(CozTimerSession* session, uint32_t slot);
COZ_TIMER_EXPORT int32_t coz_timer_retire(CozTimerSession* session, uint32_t slot);
COZ_TIMER_EXPORT int32_t coz_timer_submit(CozTimerSession* session, uint32_t slot, uint64_t command, uint64_t budget, uint64_t* generation);
COZ_TIMER_EXPORT int32_t coz_timer_cancel(CozTimerSession* session, uint32_t slot, uint64_t generation);
COZ_TIMER_EXPORT int32_t coz_timer_snapshot(CozTimerSession* session, uint32_t slot, uint64_t generation,
    CozTimerSnapshot* snapshot, CozTimerChild* children, uint32_t capacity);
COZ_TIMER_EXPORT int32_t coz_timer_release(CozTimerSession* session, uint32_t slot, uint64_t generation);
COZ_TIMER_EXPORT int32_t coz_timer_clock(CozTimerSession* session, uint64_t* nanoseconds);
COZ_TIMER_EXPORT int32_t coz_timer_close(CozTimerSession* session);
#ifdef __cplusplus
}
#endif
