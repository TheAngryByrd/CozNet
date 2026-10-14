#pragma once
#include <stdint.h>

#if defined(_WIN32)
#if defined(COZ_SERVICE_BUILD)
#define COZ_SERVICE_EXPORT __declspec(dllexport)
#else
#define COZ_SERVICE_EXPORT __declspec(dllimport)
#endif
#else
#define COZ_SERVICE_EXPORT __attribute__((visibility("default")))
#endif
#ifdef __cplusplus
extern "C" {
#endif

typedef struct CozServiceSession CozServiceSession;
typedef struct {
    uint64_t sequence, outcome, ip, handler_ns, wait_ns;
    int64_t wait_result;
    uint64_t started, queued, handler_started, handler_finished, observed;
} CozServiceAcknowledgment;
typedef struct {
    uint64_t ordinal, requested;
    CozServiceAcknowledgment acknowledgment;
} CozServiceChild;
typedef struct {
    uint64_t identity_first, identity_second, command, budget, measured, phase, error;
    int64_t transport_error;
    uint64_t cancellation, outstanding, children, rejected, captured_ns;
} CozServiceSnapshot;

typedef struct {
    uint64_t entered, api_locked, owner_entered, owner_locked, owner_finished, returned;
} CozServiceSubmissionTiming;

COZ_SERVICE_EXPORT int32_t coz_service_open(uint64_t begin, uint64_t size, int32_t mode, uint64_t capacity, CozServiceSession** session);
COZ_SERVICE_EXPORT int32_t coz_service_open_with_polling(uint64_t begin, uint64_t size, int32_t mode,
    uint64_t capacity, int32_t polling, CozServiceSession** session);
COZ_SERVICE_EXPORT int32_t coz_service_register(CozServiceSession* session, int32_t slot);
COZ_SERVICE_EXPORT int32_t coz_service_retire(CozServiceSession* session, int32_t slot);
COZ_SERVICE_EXPORT int32_t coz_service_submit(CozServiceSession* session, int32_t slot, uint64_t first, uint64_t second, uint64_t command, uint64_t budget);
COZ_SERVICE_EXPORT int32_t coz_service_submit_timed(CozServiceSession* session, int32_t slot, uint64_t first, uint64_t second, uint64_t command, uint64_t budget, CozServiceSubmissionTiming* timing);
COZ_SERVICE_EXPORT int32_t coz_service_cancel(CozServiceSession* session, int32_t slot, uint64_t first, uint64_t second);
COZ_SERVICE_EXPORT int32_t coz_service_snapshot(CozServiceSession* session, int32_t slot, uint64_t first, uint64_t second,
    CozServiceSnapshot* snapshot, CozServiceChild* children, uint64_t child_capacity,
    CozServiceAcknowledgment* rejected, uint64_t rejected_capacity);
COZ_SERVICE_EXPORT int32_t coz_service_release(CozServiceSession* session, int32_t slot, uint64_t first, uint64_t second);
COZ_SERVICE_EXPORT int32_t coz_service_clock(CozServiceSession* session, uint64_t* nanoseconds);
COZ_SERVICE_EXPORT int32_t coz_service_owner_thread(CozServiceSession* session, int32_t* thread_id);
COZ_SERVICE_EXPORT int32_t coz_service_close(CozServiceSession* session);

#ifdef __cplusplus
}
#endif
