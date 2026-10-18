#include "TargetSampleSession.Linux.h"
#include <array>
#include <chrono>
#include <cstdio>
#include <string_view>
#include <sys/utsname.h>
#include <thread>
#include <vector>

using namespace coz::service;
namespace {
constexpr uint64_t period = 1000000;

const char* outcomeName(DispatchOutcome outcome) {
    constexpr const char* names[] = {"drained_snapshot", "records_exhausted", "time_exhausted", "sink_full", "cancelled",
        "source_failed", "sink_failed", "clock_failed", "reentered", "stale_notification", "not_open", "terminal"};
    static_assert(std::size(names) == size_t(DispatchOutcome::terminal) + 1);
    return names[size_t(outcome)];
}

const char* backlogName(DispatchBacklog backlog) {
    constexpr const char* names[] = {"none", "remaining", "unknown"};
    static_assert(std::size(names) == size_t(DispatchBacklog::unknown) + 1);
    return names[size_t(backlog)];
}

const char* failureName(EpochFailure failure) {
    constexpr const char* names[] = {"none", "enable_failed", "disable_failed", "source_failed", "sink_failed", "clock_failed",
        "count_read_failed", "lost_samples", "count_regressed"};
    static_assert(std::size(names) == size_t(EpochFailure::count_regressed) + 1);
    return names[size_t(failure)];
}

uint64_t threadCpu() {
    timespec now{};
    clock_gettime(CLOCK_THREAD_CPUTIME_ID, &now);
    return uint64_t(now.tv_sec) * 1000000000ULL + uint64_t(now.tv_nsec);
}

void burn(uint64_t nanoseconds) {
    volatile uint64_t accumulator = 0;
    const auto end = threadCpu() + nanoseconds;
    while (threadCpu() < end)
        for (int index = 0; index < 1000; ++index) accumulator = accumulator + uint64_t(index);
}

struct Step {
    uint64_t burn_ns;
    bool masked;
};

struct Mode {
    std::string_view name;
    size_t data_pages;
    uint32_t records;
    int epochs;
    std::array<Step, 4> steps;
    bool cancel;
};

constexpr uint64_t ms = 1000000;
constexpr Mode modes[] = {
    {"bounded", 2, 8, 3, {{{100 * ms, false}}}, false},
    {"backlog", 4, 1, 1, {{{60 * ms, false}, {30 * ms, true}, {30 * ms, false}, {40 * ms, true}}}, false},
    {"incomplete", 1, 1, 1, {{{20 * ms, false}, {300 * ms, true}, {20 * ms, false}}}, false},
    {"cancel", 4, 8, 1, {{{150 * ms, false}}}, true},
};

void run(const Mode& mode) {
    sigset_t mask;
    sigemptyset(&mask);
    sigaddset(&mask, LinuxTargetSampleSession::signalNumber());
    for (size_t index = 0; index < mode.steps.size() && mode.steps[index].burn_ns != 0; ++index) {
        const auto& step = mode.steps[index];
        const bool last = index + 1 == mode.steps.size() || mode.steps[index + 1].burn_ns == 0;
        if (step.masked) pthread_sigmask(SIG_BLOCK, &mask, nullptr);
        burn(step.burn_ns);
        // A final masked step leaves its queued notifications for closeEpoch to drain.
        if (step.masked && !last) pthread_sigmask(SIG_UNBLOCK, &mask, nullptr);
    }
}

bool finish(std::FILE* output) {
    const bool written = std::ferror(output) == 0;
    return std::fclose(output) == 0 && written;
}
}

int main(int argc, char** argv) {
    const Mode* mode = nullptr;
    if (argc == 3)
        for (const auto& candidate : modes) if (candidate.name == argv[1]) mode = &candidate;
    if (!mode) {
        std::fprintf(stderr, "usage: TargetDispatchWorkload bounded|backlog|incomplete|cancel <evidence.json>\n");
        return 64;
    }
    std::FILE* output = std::fopen(argv[2], "w");
    if (!output) return 65;
    utsname host{};
    uname(&host);
    const auto budget = *DispatchBudget::parse(mode->records, 250000);
    const auto close_budget = *DispatchBudget::parse(8, 250000);
    std::fprintf(output, "{\"mode\":\"%s\",\"kernel\":\"%s\",\"trigger\":\"fasync\",\"period\":%llu,\"dataPages\":%zu,"
        "\"dispatchRecords\":%u,\"dispatchElapsedNs\":%llu,\"closeRecords\":%u,\"steps\":[",
        argv[1], host.release, (unsigned long long)period, mode->data_pages,
        budget.records(), (unsigned long long)budget.elapsed(), close_budget.records());
    for (size_t index = 0; index < mode->steps.size() && mode->steps[index].burn_ns != 0; ++index)
        std::fprintf(output, "%s{\"burnNs\":%llu,\"masked\":%s}", index == 0 ? "" : ",",
            (unsigned long long)mode->steps[index].burn_ns, mode->steps[index].masked ? "true" : "false");
    std::fprintf(output, "]");
    if (const int error = LinuxTargetSampleSession::install(); error != 0) {
        std::fprintf(output, ",\"installError\":%d,\"passed\":false}\n", error);
        finish(output);
        return 1;
    }
    std::vector<JournalSample> journal(4096);
    std::vector<TargetDispatchLogEntry> log(4096);
    LinuxTargetSampleSession::Handle session;
    const int created = LinuxTargetSampleSession::create({period, mode->data_pages, budget}, journal, log, session);
    if (created != 0) {
        std::fprintf(output, ",\"createError\":%d,\"passed\":false}\n", created);
        finish(output);
        return created == EACCES || created == EPERM || created == ENOENT ? 2 : 1;
    }
    std::vector<JournalSample> second_journal(1);
    std::vector<TargetDispatchLogEntry> second_log(1);
    LinuxTargetSampleSession::Handle second;
    const int duplicate = LinuxTargetSampleSession::create({period, mode->data_pages, budget}, second_journal, second_log, second);
    std::vector<TargetEpochRecord> epochs;
    std::vector<DispatchResult> closures;
    int start_error = session->start(), resume_error = 0;
    std::thread canceller;
    if (start_error == 0 && mode->cancel)
        canceller = std::thread([&] {
            std::this_thread::sleep_for(std::chrono::milliseconds(50));
            session->cancel();
        });
    for (int epoch = 0; start_error == 0 && epoch < mode->epochs; ++epoch) {
        if (epoch > 0 && (resume_error = session->resume()) != 0) break;
        run(*mode);
        closures.push_back(session->closeEpoch(close_budget, 4096));
        epochs.push_back(session->dispatcher().epoch());
        if (!epochs.back().complete) break;
    }
    if (canceller.joinable()) canceller.join();
    const bool probe_resume = mode->cancel || mode->name == "incomplete";
    const int refused_resume = probe_resume && start_error == 0 && resume_error == 0 ? session->resume() : 0;
    const auto& dispatcher = session->dispatcher();
    const auto phase = dispatcher.phase();
    const auto stale = dispatcher.stale(), reentered = dispatcher.reentered(), deliveries = session->deliveries();
    const auto tail = session->acceptedTail();
    const auto data_size = session->dataSize();
    const auto lifecycle = session->lifecycleError();
    const auto& retirement = session->retire();
    const auto restored = LinuxTargetSampleSession::restore();
    const auto entries = session->log().entries();
    const auto samples = session->journal().entries();

    bool passed = duplicate == EBUSY && !second && start_error == 0 && resume_error == 0 && lifecycle == 0 && retirement.error == 0 && retirement.blocked
        && retirement.disabled && retirement.async_cleared && retirement.unmapped && retirement.closed && retirement.mask_restored && restored == 0
        && session->log().dropped() == 0 && LinuxTargetSampleSession::foreignSignals() == 0
        && LinuxTargetSampleSession::sigioFallbacks() == 0 && stale == 0 && reentered == 0
        && !entries.empty() && int(epochs.size()) >= 1;
    uint64_t prior_sequence = 0, max_handler = 0, exhausted_with_backlog = 0, cancelled = 0, accepted_after_cancel = 0;
    uint64_t accepted_before_cancel = 0, notify_accepted = 0;
    for (const auto& entry : entries) {
        passed = passed && entry.sequence > prior_sequence && entry.handler_finished >= entry.handler_started;
        prior_sequence = entry.sequence;
        const auto duration = entry.handler_finished - entry.handler_started;
        if (duration > max_handler) max_handler = duration;
        const auto& result = entry.result;
        passed = passed && result.accepted <= mode->records;
        notify_accepted += result.accepted;
        if ((result.outcome == DispatchOutcome::records_exhausted || result.outcome == DispatchOutcome::time_exhausted)
            && result.backlog == DispatchBacklog::remaining) ++exhausted_with_backlog;
        if (cancelled != 0) accepted_after_cancel += result.accepted;
        else accepted_before_cancel += result.accepted;
        if (result.outcome == DispatchOutcome::cancelled) ++cancelled;
    }
    passed = passed && max_handler < target_handler_limit_ns;
    uint64_t prior_value = 0, accepted_total = 0;
    for (size_t index = 0; index < epochs.size(); ++index) {
        uint64_t labelled = 0;
        for (const auto& sample : samples) labelled += sample.generation == epochs[index].generation;
        passed = passed && epochs[index].generation == index + 1 && labelled == epochs[index].accepted;
        accepted_total += epochs[index].accepted;
        if (epochs[index].complete) {
            passed = passed && epochs[index].lost == 0 && epochs[index].value > prior_value
                && epochs[index].value >= epochs[index].last_cumulative_cpu;
            prior_value = epochs[index].value;
        }
    }
    passed = passed && accepted_total == samples.size();
    const bool all_complete = int(epochs.size()) == mode->epochs && phase == TargetEpochPhase::closed_complete;
    if (mode->name == "bounded")
        passed = passed && all_complete && tail > data_size;
    if (mode->name == "backlog")
        passed = passed && all_complete && exhausted_with_backlog > 0 && epochs[0].close_steps > 1 && retirement.pending_drained > 0;
    if (mode->name == "incomplete")
        passed = passed && phase == TargetEpochPhase::terminal && !epochs[0].complete
            && (epochs[0].failure == EpochFailure::source_failed || epochs[0].failure == EpochFailure::lost_samples)
            && (epochs[0].lost > 0 || (retirement.raw_count_read && retirement.raw_lost > 0)) && refused_resume == EPROTO;
    if (mode->name == "cancel")
        passed = passed && all_complete && cancelled > 0 && accepted_after_cancel == 0 && accepted_before_cancel > 0
            && refused_resume == EPROTO;
    if (mode->name != "cancel" && mode->name != "incomplete") passed = passed && cancelled == 0;

    std::fprintf(output, ",\"duplicateCreate\":%d,\"startError\":%d,\"resumeError\":%d,\"refusedResume\":%d,\"lifecycleError\":%d,\"phase\":%d,"
        "\"deliveries\":%llu,\"stale\":%llu,\"reentered\":%llu,\"foreignSignals\":%llu,\"sigioFallbacks\":%llu,\"logDropped\":%llu,"
        "\"pendingDrained\":%llu,\"acceptedTail\":%llu,\"dataSize\":%zu,\"maxHandlerNs\":%llu,\"exhaustedWithBacklog\":%llu,"
        "\"cancelledDispatches\":%llu,\"notifyAccepted\":%llu,\"journalSamples\":%zu",
        duplicate, start_error, resume_error, refused_resume, lifecycle, int(phase), (unsigned long long)deliveries,
        (unsigned long long)stale, (unsigned long long)reentered, (unsigned long long)LinuxTargetSampleSession::foreignSignals(),
        (unsigned long long)LinuxTargetSampleSession::sigioFallbacks(), (unsigned long long)session->log().dropped(), (unsigned long long)session->pendingDrained(), (unsigned long long)tail,
        data_size, (unsigned long long)max_handler, (unsigned long long)exhausted_with_backlog, (unsigned long long)cancelled,
        (unsigned long long)notify_accepted, samples.size());
    std::fprintf(output, ",\"retirement\":{\"blocked\":%s,\"disabled\":%s,\"asyncCleared\":%s,\"pendingDrained\":%llu,\"unmapped\":%s,"
        "\"closed\":%s,\"maskRestored\":%s,\"rawCountRead\":%s,\"rawValue\":%llu,\"rawLost\":%llu,\"error\":%d,\"handlerRestored\":%d}",
        retirement.blocked ? "true" : "false", retirement.disabled ? "true" : "false", retirement.async_cleared ? "true" : "false",
        (unsigned long long)retirement.pending_drained, retirement.unmapped ? "true" : "false", retirement.closed ? "true" : "false",
        retirement.mask_restored ? "true" : "false", retirement.raw_count_read ? "true" : "false",
        (unsigned long long)retirement.raw_value, (unsigned long long)retirement.raw_lost, retirement.error, restored);
    std::fprintf(output, ",\"epochs\":[");
    for (size_t index = 0; index < epochs.size(); ++index) {
        const auto& epoch = epochs[index];
        const auto& closure = closures[index];
        std::fprintf(output, "%s{\"generation\":%llu,\"accepted\":%llu,\"lastCumulativeCpu\":%llu,\"value\":%llu,\"lost\":%llu,"
            "\"closeSteps\":%llu,\"complete\":%s,\"failure\":\"%s\",\"closeOutcome\":\"%s\",\"closeSourceError\":%d}",
            index == 0 ? "" : ",", (unsigned long long)epoch.generation, (unsigned long long)epoch.accepted,
            (unsigned long long)epoch.last_cumulative_cpu, (unsigned long long)epoch.value, (unsigned long long)epoch.lost,
            (unsigned long long)epoch.close_steps, epoch.complete ? "true" : "false", failureName(epoch.failure),
            outcomeName(closure.outcome), int(closure.source_error));
    }
    std::fprintf(output, "],\"dispatches\":[");
    for (size_t index = 0; index < entries.size(); ++index) {
        const auto& entry = entries[index];
        const auto& result = entry.result;
        std::fprintf(output, "%s[%llu,%llu,\"%s\",\"%s\",%u,%llu,%llu,%llu,%llu,%d]", index == 0 ? "" : ",",
            (unsigned long long)entry.sequence, (unsigned long long)result.generation,
            outcomeName(result.outcome), backlogName(result.backlog), result.accepted,
            (unsigned long long)entry.handler_started, (unsigned long long)entry.handler_finished,
            (unsigned long long)result.started, (unsigned long long)result.finished, int(result.source_error));
    }
    std::fprintf(output, "],\"dispatchFields\":[\"sequence\",\"generation\",\"outcome\",\"backlog\",\"accepted\","
        "\"handlerStarted\",\"handlerFinished\",\"dispatchStarted\",\"dispatchFinished\",\"sourceError\"],\"samples\":[");
    for (size_t index = 0; index < samples.size(); ++index) {
        const auto& entry = samples[index];
        std::fprintf(output, "%s[%llu,%llu,%llu,%llu,%llu]", index == 0 ? "" : ",", (unsigned long long)entry.generation,
            (unsigned long long)entry.dispatch, (unsigned long long)entry.sample.instruction_pointer,
            (unsigned long long)entry.sample.timestamp, (unsigned long long)entry.sample.cumulative_cpu);
    }
    std::fprintf(output, "],\"sampleFields\":[\"generation\",\"dispatch\",\"ip\",\"time\",\"cumulativeCpu\"],\"passed\":%s}\n",
        passed ? "true" : "false");
    return finish(output) && passed ? 0 : 1;
}
