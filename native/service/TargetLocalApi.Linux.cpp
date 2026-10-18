#include "TargetLocalApi.h"
#include "TargetSampleSession.Linux.h"
#include <atomic>
#include <cstddef>
#include <mutex>
#include <new>
#include <vector>

using namespace coz::service;
namespace {
static_assert(sizeof(CozTargetSpan) == 16);
static_assert(sizeof(CozTargetEvent) == 64 && offsetof(CozTargetEvent, kind) == 56);
static_assert(sizeof(CozTargetThreadResult) == 144);
static_assert(sizeof(CozTargetExperimentResult) == 40);
using Sink = TargetCreditSink<LinuxMonotonicClock, LinuxSpinWaiter>;
using Session = LinuxTargetSession<Sink>;
constexpr uint32_t max_journal = 1u << 22;
constexpr unsigned max_closing_settlements = 64;
std::mutex api_mutex;
}

struct CozTargetExperiment {
    LedgerPlan plan;
    ExperimentDelay delay;
    uint32_t threads{};
    bool started{};
    explicit CozTargetExperiment(LedgerPlan value) noexcept : plan(value) {}
};

struct CozTargetThread {
    CozTargetExperiment& experiment;
    std::vector<LedgerEvent> events;
    std::vector<TargetDispatchLogEntry> log;
    std::optional<ThreadDelayAccount> account;
    LinuxMonotonicClock clock;
    LinuxSpinWaiter waiter;
    std::optional<Sink> sink;
    Session::Handle session;
    pid_t owner{};
    uint64_t started{};
    bool closing{};
    std::atomic<bool> finished{};
    CozTargetThread(CozTargetExperiment& value, uint32_t capacity) : experiment(value), events(capacity), log(capacity) {}
};

extern "C" uint32_t coz_target_layout(uint32_t which) {
    switch (which) {
    case 0: return sizeof(CozTargetSpan);
    case 1: return sizeof(CozTargetEvent);
    case 2: return sizeof(CozTargetThreadResult);
    case 3: return sizeof(CozTargetExperimentResult);
    default: return 0;
    }
}

extern "C" int32_t coz_target_open(uint32_t speedup, uint64_t period, const CozTargetSpan* selected, uint32_t selected_count,
    const CozTargetSpan* eligible, uint32_t eligible_count, CozTargetExperiment** experiment) {
    if (!experiment) return EINVAL;
    *experiment = nullptr;
    if ((selected_count != 0 && !selected) || !eligible || selected_count > InstructionSet::capacity
        || eligible_count > InstructionSet::capacity) return EINVAL;
    InstructionSpan selected_spans[InstructionSet::capacity]{}, eligible_spans[InstructionSet::capacity]{};
    for (uint32_t index = 0; index < selected_count; ++index) selected_spans[index] = {selected[index].begin, selected[index].end};
    for (uint32_t index = 0; index < eligible_count; ++index) eligible_spans[index] = {eligible[index].begin, eligible[index].end};
    auto plan = LedgerPlan::parse(speedup, period, std::span(selected_spans, selected_count), std::span(eligible_spans, eligible_count));
    if (!plan) return EINVAL;
    std::lock_guard lock(api_mutex);
    if (const int error = Session::install(); error != 0) return error;
    auto* value = new (std::nothrow) CozTargetExperiment(*plan);
    if (!value) {
        Session::restore();
        return ENOMEM;
    }
    *experiment = value;
    return 0;
}

extern "C" int32_t coz_target_register(CozTargetExperiment* experiment, uint32_t journal_capacity, CozTargetThread** thread) {
    if (!experiment || !thread || journal_capacity <= ThreadDelayAccount::settle_reserve || journal_capacity > max_journal) return EINVAL;
    *thread = nullptr;
    std::lock_guard lock(api_mutex);
    if (experiment->started) return EBUSY;
    CozTargetThread* value = nullptr;
    try {
        value = new CozTargetThread(*experiment, journal_capacity);
    } catch (...) {
        return ENOMEM;
    }
    value->account.emplace(experiment->delay, experiment->plan, value->events);
    value->sink.emplace(*value->account, value->clock, value->waiter);
    value->owner = gettid();
    const int error = Session::create({experiment->plan.period(), 8, *DispatchBudget::parse(8, 250000)}, *value->sink, value->log, value->session);
    if (error != 0) {
        delete value;
        return error;
    }
    ++experiment->threads;
    *thread = value;
    return 0;
}

extern "C" int32_t coz_target_start(CozTargetThread* thread) {
    if (!thread || !thread->session || thread->owner != gettid() || thread->closing) return EINVAL;
    {
        std::lock_guard lock(api_mutex);
        thread->experiment.started = true;
    }
    thread->started = thread->clock.now();
    return thread->session->start();
}

extern "C" int32_t coz_target_finish(CozTargetThread* thread, uint64_t payment_ip, CozTargetThreadResult* result) {
    if (!thread || !result || !thread->session || thread->owner != gettid() || thread->closing) return EINVAL;
    *result = {};
    thread->closing = true;
    auto& session = *thread->session;
    const auto closure = session.closeEpoch(*DispatchBudget::parse(64, 1000000), 4096);
    for (unsigned step = 0; step < max_closing_settlements; ++step) {
        thread->sink->settle(payment_ip, thread->clock.now());
        if (thread->sink->last().outcome != SettleOutcome::budget_deferred) break;
    }
    const auto finished = thread->clock.now();
    const auto& epoch = session.dispatcher().epoch();
    uint64_t max_handler = 0, total_handler = 0, dispatches = 0;
    for (const auto& entry : session.log().entries()) {
        const auto duration = entry.handler_finished - entry.handler_started;
        if (duration > max_handler) max_handler = duration;
        total_handler += duration;
        ++dispatches;
    }
    *result = {epoch.generation, epoch.accepted, epoch.last_cumulative_cpu, epoch.value, epoch.lost, epoch.close_steps,
        session.deliveries(), dispatches, max_handler, total_handler, thread->account->local(), thread->account->events().size(),
        thread->started, finished, thread->sink->last().debt_after, epoch.complete ? 1 : 0, int32_t(epoch.failure), int32_t(closure.outcome),
        session.lifecycleError(), 0, 0};
    const auto& retirement = session.retire();
    result->retire_error = retirement.error;
    result->quiescent = retirement.quiescent ? 1 : 0;
    thread->finished.store(true, std::memory_order_release);
    return epoch.complete && retirement.error == 0 && session.log().dropped() == 0 ? 0 : EIO;
}

extern "C" int32_t coz_target_events(CozTargetThread* thread, CozTargetEvent* events, uint32_t capacity, uint32_t* count) {
    if (!thread || !count || (capacity != 0 && !events) || !thread->finished.load(std::memory_order_acquire)) return EINVAL;
    const auto recorded = thread->account->events();
    *count = uint32_t(recorded.size());
    if (recorded.size() > capacity) return ENOBUFS;
    for (size_t index = 0; index < recorded.size(); ++index) {
        const auto& event = recorded[index];
        events[index] = {event.sequence, event.observed_global, event.local_before, event.amount, event.requested, event.time,
            event.ip, uint32_t(event.kind), event.detail};
    }
    return 0;
}

extern "C" int32_t coz_target_release(CozTargetThread* thread) {
    if (!thread || !thread->finished.load(std::memory_order_acquire)) return EINVAL;
    auto& experiment = thread->experiment;
    // A handler on another thread can still hold this session and its sink.
    if (!thread->session->retire().quiescent) return EBUSY;
    thread->session.reset();
    delete thread;
    std::lock_guard lock(api_mutex);
    --experiment.threads;
    return 0;
}

extern "C" int32_t coz_target_close(CozTargetExperiment* experiment, CozTargetExperimentResult* result) {
    if (!experiment || !result) return EINVAL;
    std::lock_guard lock(api_mutex);
    if (experiment->threads != 0) return EBUSY;
    *result = {experiment->delay.global(), experiment->delay.sequences(), Session::foreignSignals(), Session::sigioFallbacks(),
        uint32_t(experiment->delay.failure()), Session::restore()};
    if (result->restore_error != 0) return result->restore_error;
    delete experiment;
    return 0;
}
