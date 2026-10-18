#include "TargetSampleDispatch.h"
#include <array>
#include <functional>
#include <stdexcept>
#include <type_traits>
#include <utility>

using namespace coz::service;
namespace {
void require(bool condition, const char* message) {
    if (!condition) throw std::runtime_error(message);
}

struct Counts {
    uint64_t value{}, lost{};
};

struct Read {
    PerfSampleRingRead record;
    bool snapshot_complete{};
};

struct Source {
    uint64_t pending{}, produced{}, reads{}, enables{}, disables{}, fail_at{}, stall_at{};
    int enable_error{}, disable_error{};
    std::optional<Counts> counts{Counts{}};
    std::function<void()> during_read;
    Read read() noexcept {
        ++reads;
        if (during_read) during_read();
        if (fail_at != 0 && reads == fail_at) return {{std::nullopt, PerfSampleRingError::sample_time}, false};
        if (stall_at != 0 && reads == stall_at) return {{}, false};
        if (pending == 0) return {{}, true};
        --pending;
        ++produced;
        PerfCpuSample sample{0x1000 + produced, produced * 10, 1000000, produced * 1000000, 7, 14};
        return {{sample, PerfSampleRingError::none}, pending == 0};
    }
    int enable() noexcept { ++enables; return enable_error; }
    int disable() noexcept { ++disables; return disable_error; }
    std::optional<Counts> finalCount() noexcept {
        if (counts && counts->value == 0) return Counts{produced * 1000000, counts->lost};
        return counts;
    }
};

struct Clock {
    uint64_t value{100}, step{1}, calls{}, zero_at{}, back_at{};
    uint64_t now() noexcept {
        ++calls;
        if (zero_at != 0 && calls == zero_at) return 0;
        if (back_at != 0 && calls == back_at) return value - 1;
        value += step;
        return value;
    }
};

struct RejectingSink {
    size_t available() const noexcept { return 1; }
    bool accept(const PerfCpuSample&, uint64_t, uint64_t) noexcept { return false; }
};

template<size_t Capacity = 64>
struct Fixture {
    Source source;
    Clock clock;
    std::array<JournalSample, Capacity> storage{};
    SampleJournal journal{storage};
    TargetSampleDispatcher<Source, Clock, SampleJournal> dispatcher{source, clock, journal};
    uint64_t sequence{};
    DispatchResult notify(uint32_t records = 8, uint64_t elapsed = 1000000) {
        return dispatcher.notify({dispatcher.generation(), ++sequence}, *DispatchBudget::parse(records, elapsed));
    }
    DispatchResult close(uint32_t records = 8) {
        return dispatcher.continueClose(*DispatchBudget::parse(records, 1000000));
    }
};

template<class Dispatcher>
void requireTerminal(Dispatcher& dispatcher, uint64_t reads, const Source& source, const char* message) {
    auto budget = *DispatchBudget::parse(8, 1000000);
    require(dispatcher.phase() == TargetEpochPhase::terminal && !dispatcher.epoch().complete
        && dispatcher.notify({dispatcher.generation(), 1000}, budget).outcome == DispatchOutcome::terminal
        && dispatcher.beginClose().outcome == DispatchOutcome::terminal
        && dispatcher.continueClose(budget).outcome == DispatchOutcome::terminal
        && !dispatcher.resume() && source.reads == reads, message);
}

void budgetsAreBounded() {
    require(!DispatchBudget::parse(0, 1000) && !DispatchBudget::parse(4097, 1000), "A record budget outside 1-4096 was accepted.");
    require(!DispatchBudget::parse(1, 0) && !DispatchBudget::parse(1, 5000000), "An elapsed budget outside the handler bound was accepted.");
    require(DispatchBudget::parse(1, 1) && DispatchBudget::parse(4096, 4999999), "A valid budget was rejected.");
}

void snapshotsDrainWithinBudget() {
    Fixture fixture;
    require(fixture.dispatcher.start() && fixture.source.enables == 1 && fixture.dispatcher.generation() == 1, "Start must enable epoch 1.");
    fixture.source.pending = 3;
    auto drained = fixture.notify();
    require(drained.outcome == DispatchOutcome::drained_snapshot && drained.backlog == DispatchBacklog::none
        && drained.accepted == 3 && fixture.source.reads == 3, "A short backlog must drain as one snapshot.");
    auto empty = fixture.notify();
    require(empty.outcome == DispatchOutcome::drained_snapshot && empty.accepted == 0 && fixture.source.reads == 4,
        "An empty ring must drain after one read.");
    for (const auto& entry : fixture.journal.entries())
        require(entry.generation == 1 && entry.dispatch == 1, "Each journal entry must carry its epoch and dispatch.");
}

void recordBudgetsPreserveBacklog() {
    Fixture fixture;
    fixture.dispatcher.start();
    fixture.source.pending = 5;
    auto first = fixture.notify(2);
    require(first.outcome == DispatchOutcome::records_exhausted && first.backlog == DispatchBacklog::remaining
        && first.accepted == 2 && fixture.source.reads == 2 && fixture.source.pending == 3,
        "A record budget must stop before the next read and report remaining backlog.");
    auto second = fixture.notify();
    require(second.outcome == DispatchOutcome::drained_snapshot && second.accepted == 3 && fixture.journal.entries().size() == 5
        && fixture.journal.entries()[4].dispatch == 2, "The next dispatch must drain the retained backlog.");
}

void elapsedBudgetsPreserveBacklog() {
    Fixture fixture;
    fixture.dispatcher.start();
    fixture.source.pending = 100;
    fixture.clock.step = 10;
    auto partial = fixture.notify(100, 25);
    require(partial.outcome == DispatchOutcome::time_exhausted && partial.backlog == DispatchBacklog::remaining
        && partial.accepted == 2 && fixture.source.reads == 2 && partial.finished - partial.started >= 25,
        "An elapsed budget must stop before the next read.");
    auto none = fixture.notify(100, 1);
    require(none.outcome == DispatchOutcome::time_exhausted && none.backlog == DispatchBacklog::unknown
        && none.accepted == 0 && fixture.source.reads == 2, "A budget exhausted before any read must report unknown backlog.");
}

void fullSinksLeaveSamplesUnread() {
    Fixture<2> fixture;
    fixture.dispatcher.start();
    fixture.source.pending = 5;
    auto full = fixture.notify();
    require(full.outcome == DispatchOutcome::sink_full && full.backlog == DispatchBacklog::remaining
        && fixture.source.reads == 2 && fixture.source.pending == 3 && fixture.dispatcher.phase() == TargetEpochPhase::open,
        "A full sink must stop before a read that would publish an unheld sample.");
}

void notificationsRequireIdentity() {
    Fixture fixture;
    require(fixture.notify().outcome == DispatchOutcome::not_open && fixture.source.reads == 0, "An unstarted source was dispatched.");
    fixture.dispatcher.start();
    auto budget = *DispatchBudget::parse(8, 1000000);
    auto wrong = fixture.dispatcher.notify({2, 1}, budget);
    fixture.notify();
    auto repeated = fixture.dispatcher.notify({1, fixture.sequence}, budget);
    require(wrong.outcome == DispatchOutcome::stale_notification && repeated.outcome == DispatchOutcome::stale_notification
        && fixture.dispatcher.stale() == 2 && fixture.source.reads == 1, "A stale or repeated notification was dispatched.");
    DispatchResult nested{};
    fixture.source.pending = 1;
    fixture.source.during_read = [&] { nested = fixture.dispatcher.notify({1, 99}, budget); };
    fixture.notify();
    require(nested.outcome == DispatchOutcome::reentered && fixture.dispatcher.reentered() == 1,
        "A nested notification must be rejected as reentry.");
    DispatchResult nested_close{};
    fixture.source.during_read = [&] { nested_close = fixture.dispatcher.beginClose(); };
    fixture.notify();
    require(nested_close.outcome == DispatchOutcome::reentered && fixture.source.disables == 0,
        "Closure must not overlap an active dispatch.");
}

void cancellationStopsDispatchButNotClosure() {
    Fixture fixture;
    fixture.dispatcher.start();
    fixture.source.pending = 4;
    fixture.dispatcher.cancel();
    auto cancelled = fixture.notify();
    require(cancelled.outcome == DispatchOutcome::cancelled && fixture.source.reads == 0, "A cancelled dispatcher read samples.");
    require(fixture.dispatcher.beginClose().outcome == DispatchOutcome::drained_snapshot && fixture.source.disables == 1,
        "Closure must disable a cancelled source.");
    auto closed = fixture.close();
    require(closed.outcome == DispatchOutcome::drained_snapshot && closed.accepted == 4
        && fixture.dispatcher.phase() == TargetEpochPhase::closed_complete, "Closure must drain the backlog of a cancelled source.");
    require(!fixture.dispatcher.resume() && fixture.source.enables == 1, "A cancelled dispatcher resumed.");
}

void closureContinuesInBoundedSteps() {
    Fixture fixture;
    fixture.dispatcher.start();
    fixture.source.pending = 10;
    require(fixture.close().outcome == DispatchOutcome::not_open, "Closure continued before disable.");
    fixture.dispatcher.beginClose();
    require(fixture.dispatcher.beginClose().outcome == DispatchOutcome::not_open && fixture.source.disables == 1,
        "A second disable was accepted.");
    require(fixture.notify().outcome == DispatchOutcome::not_open, "A closing source accepted a notification.");
    for (int step = 0; step < 3; ++step) {
        auto partial = fixture.close(3);
        require(partial.outcome == DispatchOutcome::records_exhausted && partial.backlog == DispatchBacklog::remaining
            && fixture.dispatcher.phase() == TargetEpochPhase::closing, "Budget exhaustion during closure must not complete the epoch.");
    }
    auto last = fixture.close(3);
    const auto& epoch = fixture.dispatcher.epoch();
    require(last.outcome == DispatchOutcome::drained_snapshot && epoch.complete && epoch.close_steps == 4
        && epoch.accepted == 10 && epoch.value == 10000000 && epoch.lost == 0, "The final bounded step must complete the epoch.");
    require(fixture.dispatcher.resume() && fixture.dispatcher.generation() == 2 && fixture.source.enables == 2,
        "A complete epoch must resume with the next generation.");
    auto budget = *DispatchBudget::parse(8, 1000000);
    require(fixture.dispatcher.notify({1, ++fixture.sequence}, budget).outcome == DispatchOutcome::stale_notification,
        "A notification from a closed epoch was accepted.");
    fixture.source.pending = 1;
    fixture.notify();
    require(fixture.journal.entries().back().generation == 2 && fixture.dispatcher.epoch().accepted == 1,
        "Resumed samples must carry the new generation.");
}

void lossMakesClosureTerminal() {
    Fixture fixture;
    fixture.dispatcher.start();
    fixture.source.pending = 3;
    fixture.notify();
    fixture.source.counts = Counts{0, 2};
    fixture.dispatcher.beginClose();
    auto closed = fixture.close();
    const auto& epoch = fixture.dispatcher.epoch();
    require(closed.outcome == DispatchOutcome::source_failed && closed.backlog == DispatchBacklog::unknown
        && !epoch.complete && epoch.failure == EpochFailure::lost_samples
        && epoch.lost == 2 && epoch.accepted == 3 && fixture.dispatcher.phase() == TargetEpochPhase::terminal,
        "Final loss must make the epoch incomplete.");
    require(fixture.journal.entries().size() == 3 && fixture.journal.entries()[2].generation == 1,
        "Incomplete closure must keep accepted samples under their epoch.");
    require(!fixture.dispatcher.resume() && fixture.close().outcome == DispatchOutcome::terminal
        && fixture.notify().outcome == DispatchOutcome::terminal && fixture.source.enables == 1, "An incomplete epoch resumed.");
}

void closureFailuresAreTerminal() {
    {
        Fixture fixture;
        fixture.dispatcher.start();
        fixture.source.disable_error = 5;
        require(fixture.dispatcher.beginClose().outcome == DispatchOutcome::source_failed
            && fixture.dispatcher.epoch().failure == EpochFailure::disable_failed, "A failed disable must be terminal.");
        requireTerminal(fixture.dispatcher, 0, fixture.source, "A failed disable must block every later transition.");
    }
    {
        Fixture fixture;
        fixture.dispatcher.start();
        fixture.source.counts.reset();
        fixture.dispatcher.beginClose();
        auto closed = fixture.close();
        require(closed.outcome == DispatchOutcome::source_failed && closed.backlog == DispatchBacklog::unknown
            && fixture.dispatcher.epoch().failure == EpochFailure::count_read_failed, "A short count read must be terminal.");
        requireTerminal(fixture.dispatcher, 1, fixture.source, "A short count read must block every later transition.");
    }
    {
        Fixture fixture;
        fixture.dispatcher.start();
        fixture.source.pending = 2;
        fixture.notify();
        fixture.source.counts = Counts{1999999, 0};
        fixture.dispatcher.beginClose();
        require(fixture.close().outcome == DispatchOutcome::source_failed
            && fixture.dispatcher.epoch().failure == EpochFailure::count_regressed, "A count below the last sample must be terminal.");
        requireTerminal(fixture.dispatcher, 3, fixture.source, "A count regression must block every later transition.");
    }
    {
        Fixture fixture;
        fixture.source.enable_error = 1;
        require(!fixture.dispatcher.start() && fixture.dispatcher.phase() == TargetEpochPhase::terminal
            && fixture.dispatcher.epoch().failure == EpochFailure::enable_failed, "A failed enable must be terminal.");
        requireTerminal(fixture.dispatcher, 0, fixture.source, "A failed enable must block every later transition.");
        require(!fixture.dispatcher.start(), "A failed start was retried.");
    }
}

void firstFailureIsTerminal() {
    {
        Fixture fixture;
        fixture.dispatcher.start();
        fixture.source.pending = 5;
        fixture.source.fail_at = 2;
        auto failed = fixture.notify();
        require(failed.outcome == DispatchOutcome::source_failed && failed.source_error == PerfSampleRingError::sample_time
            && failed.accepted == 1 && fixture.dispatcher.epoch().failure == EpochFailure::source_failed, "A source error must be terminal.");
        fixture.source.fail_at = 0;
        require(fixture.notify().outcome == DispatchOutcome::terminal && fixture.source.reads == 2
            && fixture.dispatcher.beginClose().outcome == DispatchOutcome::terminal && !fixture.dispatcher.resume(),
            "A terminal dispatcher read after source repair.");
    }
    {
        Fixture fixture;
        fixture.dispatcher.start();
        fixture.source.pending = 5;
        fixture.source.stall_at = 1;
        require(fixture.notify().outcome == DispatchOutcome::source_failed, "A read without progress or completion was accepted.");
        requireTerminal(fixture.dispatcher, 1, fixture.source, "A stalled read must block every later transition.");
    }
    for (int mode = 0; mode < 2; ++mode) {
        Fixture fixture;
        fixture.dispatcher.start();
        fixture.source.pending = 5;
        if (mode == 0) fixture.clock.zero_at = 3;
        else fixture.clock.back_at = 3;
        auto failed = fixture.notify();
        require(failed.outcome == DispatchOutcome::clock_failed && fixture.dispatcher.phase() == TargetEpochPhase::terminal
            && fixture.source.reads == 1, "A failed or decreasing clock must stop the dispatch.");
        requireTerminal(fixture.dispatcher, 1, fixture.source, "A clock failure must block every later transition.");
    }
    Source source;
    Clock clock;
    RejectingSink sink;
    TargetSampleDispatcher<Source, Clock, RejectingSink> dispatcher(source, clock, sink);
    dispatcher.start();
    source.pending = 1;
    auto rejected = dispatcher.notify({1, 1}, *DispatchBudget::parse(8, 1000000));
    require(rejected.outcome == DispatchOutcome::sink_failed && dispatcher.epoch().failure == EpochFailure::sink_failed,
        "A sink failure must be terminal.");
    requireTerminal(dispatcher, 1, source, "A sink failure must block every later transition.");
}
}

static_assert(!std::is_copy_constructible_v<TargetSampleDispatcher<Source, Clock, SampleJournal>>);
static_assert(noexcept(std::declval<TargetSampleDispatcher<Source, Clock, SampleJournal>&>().notify({}, std::declval<DispatchBudget>())));

int main() {
    budgetsAreBounded();
    snapshotsDrainWithinBudget();
    recordBudgetsPreserveBacklog();
    elapsedBudgetsPreserveBacklog();
    fullSinksLeaveSamplesUnread();
    notificationsRequireIdentity();
    cancellationStopsDispatchButNotClosure();
    closureContinuesInBoundedSteps();
    lossMakesClosureTerminal();
    closureFailuresAreTerminal();
    firstFailureIsTerminal();
}
