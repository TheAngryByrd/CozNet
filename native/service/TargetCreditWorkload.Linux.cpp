#include "TargetSampleSession.Linux.h"
#include <algorithm>
#include <barrier>
#include <cstdio>
#include <string_view>
#include <sys/utsname.h>
#include <thread>
#include <vector>

using namespace coz::service;
extern "C" char __start_coz_selected[], __stop_coz_selected[], __start_coz_peer[], __stop_coz_peer[];
namespace {
constexpr uint64_t period = 1000000, work_ns = 300000000, calibration_rounds = 2000;
using Sink = TargetCreditSink<LinuxMonotonicClock, LinuxSpinWaiter>;
using Session = LinuxTargetSession<Sink>;

uint64_t threadCpu() {
    timespec now{};
    clock_gettime(CLOCK_THREAD_CPUTIME_ID, &now);
    return uint64_t(now.tv_sec) * 1000000000ULL + uint64_t(now.tv_nsec);
}

// Work is a fixed iteration count, so payment spin adds wall time instead of replacing work.
__attribute__((noinline, section("coz_selected"))) void selectedWork(uint64_t rounds) {
    volatile uint64_t accumulator = 0;
    for (uint64_t round = 0; round < rounds; ++round)
        for (int index = 0; index < 20000; ++index) accumulator = accumulator + uint64_t(index);
}

__attribute__((noinline, section("coz_peer"))) void peerWork(uint64_t rounds) {
    volatile uint64_t accumulator = 0;
    for (uint64_t round = 0; round < rounds; ++round)
        for (int index = 0; index < 20000; ++index) accumulator = accumulator + uint64_t(index);
}

struct Worker {
    std::vector<LedgerEvent> events = std::vector<LedgerEvent>(65536);
    std::vector<TargetDispatchLogEntry> log = std::vector<TargetDispatchLogEntry>(16384);
    std::optional<ThreadDelayAccount> account;
    LinuxMonotonicClock clock;
    LinuxSpinWaiter waiter;
    std::optional<Sink> sink;
    int create{}, start{}, lifecycle{};
    DispatchResult closure;
    TargetEpochRecord epoch;
    TargetRetirement retirement;
    uint64_t wall_started{}, wall_finished{}, cpu_started{}, cpu_finished{}, deliveries{}, max_handler{}, settlements{};
    size_t dispatches{};
    bool selected{};
};

void run(Worker& worker, ExperimentDelay& experiment, const LedgerPlan& plan, std::barrier<>& ready, uint64_t rounds) {
    worker.account.emplace(experiment, plan, worker.events);
    worker.sink.emplace(*worker.account, worker.clock, worker.waiter);
    Session::Handle session;
    worker.create = Session::create({period, 8, *DispatchBudget::parse(8, 250000)}, *worker.sink, worker.log, session);
    ready.arrive_and_wait();
    if (worker.create != 0) return;
    worker.wall_started = worker.clock.now();
    worker.cpu_started = threadCpu();
    worker.start = session->start();
    if (worker.start == 0) {
        if (worker.selected) selectedWork(rounds);
        else peerWork(rounds);
    }
    worker.closure = session->closeEpoch(*DispatchBudget::parse(64, 1000000), 4096);
    worker.sink->settle(0, worker.clock.now());
    worker.wall_finished = worker.clock.now();
    worker.cpu_finished = threadCpu();
    worker.epoch = session->dispatcher().epoch();
    worker.lifecycle = session->lifecycleError();
    worker.deliveries = session->deliveries();
    worker.settlements = worker.sink->settlements();
    for (const auto& entry : session->log().entries()) {
        const auto duration = entry.handler_finished - entry.handler_started;
        if (duration > worker.max_handler) worker.max_handler = duration;
        ++worker.dispatches;
    }
    worker.retirement = session->retire();
}

struct Replay {
    bool valid{true};
    uint64_t global{}, selected_samples{}, credited{}, children{}, child_max{}, measured{}, deferred{}, publishes{}, failures{};
    uint64_t max_debt{};
};

Replay replay(const std::vector<const Worker*>& workers, const LedgerPlan& plan, uint64_t total) {
    Replay result;
    std::vector<const LedgerEvent*> ordered(total + 1);
    std::vector<size_t> owner(total + 1);
    for (size_t index = 0; index < workers.size(); ++index)
        for (const auto& event : workers[index]->account->events()) {
            if (event.sequence == 0 || event.sequence > total || ordered[event.sequence]) return {false};
            ordered[event.sequence] = &event;
            owner[event.sequence] = index;
        }
    std::vector<uint64_t> local(workers.size());
    std::vector<uint64_t> published{0};
    for (uint64_t sequence = 1; sequence <= total; ++sequence) {
        if (!ordered[sequence]) return {false};
        if (ordered[sequence]->kind == LedgerEventKind::publish) published.push_back(ordered[sequence]->amount);
    }
    for (uint64_t sequence = 1; sequence <= total; ++sequence) {
        const auto* event = ordered[sequence];
        auto& value = local[owner[sequence]];
        bool known = false;
        for (const auto candidate : published) known = known || candidate == event->observed_global;
        if (event->local_before != value || !known) result.valid = false;
        switch (event->kind) {
        case LedgerEventKind::sample:
            if (event->amount != (plan.selected(event->ip) ? plan.credit() : 0)) result.valid = false;
            result.selected_samples += event->detail;
            result.credited += event->amount;
            value += event->amount;
            break;
        case LedgerEventKind::publish:
            if (event->amount != value || value <= event->observed_global) result.valid = false;
            if (value > result.global) result.global = value;
            ++result.publishes;
            break;
        case LedgerEventKind::child:
            if (event->requested == 0 || event->requested > ledger_child_limit_ns || !plan.eligible(event->ip)
                || event->observed_global <= event->local_before
                || event->requested > event->observed_global - event->local_before) result.valid = false;
            if (event->requested > result.child_max) result.child_max = event->requested;
            value += event->amount;
            result.measured += event->amount;
            ++result.children;
            break;
        case LedgerEventKind::deferred:
            ++result.deferred;
            break;
        case LedgerEventKind::failure:
            ++result.failures;
            break;
        }
        if (event->observed_global > event->local_before && event->observed_global - event->local_before > result.max_debt)
            result.max_debt = event->observed_global - event->local_before;
    }
    std::vector<std::pair<uint64_t, uint64_t>> chain;
    for (uint64_t sequence = 1; sequence <= total; ++sequence)
        if (ordered[sequence]->kind == LedgerEventKind::publish)
            chain.push_back({ordered[sequence]->amount, ordered[sequence]->observed_global});
    std::sort(chain.begin(), chain.end());
    uint64_t predecessor = 0;
    for (const auto& [value, observed] : chain) {
        if (observed != predecessor || value <= observed) result.valid = false;
        predecessor = value;
    }
    return result;
}

bool finish(std::FILE* output) {
    const bool written = std::ferror(output) == 0;
    return std::fclose(output) == 0 && written;
}
}

int main(int argc, char** argv) {
    uint32_t speedup = 101;
    if (argc == 3 && std::string_view(argv[1]) == "zero") speedup = 0;
    if (argc == 3 && std::string_view(argv[1]) == "half") speedup = 50;
    if (speedup > 100) {
        std::fprintf(stderr, "usage: TargetCreditWorkload zero|half <evidence.json>\n");
        return 64;
    }
    std::FILE* output = std::fopen(argv[2], "w");
    if (!output) return 65;
    utsname host{};
    uname(&host);
    const InstructionSpan selected[] = {{uint64_t(__start_coz_selected), uint64_t(__stop_coz_selected)}};
    const InstructionSpan eligible[] = {{uint64_t(__start_coz_selected), uint64_t(__stop_coz_selected)},
        {uint64_t(__start_coz_peer), uint64_t(__stop_coz_peer)}};
    const auto plan = LedgerPlan::parse(speedup, period, selected, eligible);
    std::fprintf(output, "{\"mode\":\"%s\",\"kernel\":\"%s\",\"speedup\":%u,\"period\":%llu,\"workNs\":%llu",
        argv[1], host.release, speedup, (unsigned long long)period, (unsigned long long)work_ns);
    if (!plan || Session::install() != 0) {
        std::fprintf(output, ",\"setup\":false,\"passed\":false}\n");
        finish(output);
        return 1;
    }
    const auto calibration_started = threadCpu();
    peerWork(calibration_rounds);
    const auto calibration_ns = threadCpu() - calibration_started;
    const uint64_t rounds = calibration_ns == 0 ? 0 : calibration_rounds * work_ns / calibration_ns;
    ExperimentDelay experiment;
    Worker selected_worker, peer_worker;
    selected_worker.selected = true;
    std::barrier<> ready(2);
    std::thread selected_thread(run, std::ref(selected_worker), std::ref(experiment), std::cref(*plan), std::ref(ready), rounds);
    std::thread peer_thread(run, std::ref(peer_worker), std::ref(experiment), std::cref(*plan), std::ref(ready), rounds);
    selected_thread.join();
    peer_thread.join();
    const int restored = Session::restore();
    const auto total = experiment.sequences();
    const auto checked = replay({&selected_worker, &peer_worker}, *plan, total);
    const auto global = experiment.global();

    bool passed = rounds > 0 && restored == 0 && checked.valid && checked.global == global && checked.failures == 0
        && experiment.failure() == LedgerFailure::none && checked.child_max <= ledger_child_limit_ns
        && Session::foreignSignals() == 0 && Session::sigioFallbacks() == 0;
    for (const auto* worker : {&selected_worker, &peer_worker})
        passed = passed && worker->create == 0 && worker->start == 0 && worker->lifecycle == 0 && worker->epoch.complete
            && worker->epoch.lost == 0 && worker->closure.outcome == DispatchOutcome::drained_snapshot
            && worker->retirement.error == 0 && worker->retirement.quiescent && worker->max_handler < ledger_handler_limit_ns
            && worker->dispatches > 0;
    const auto selected_local = selected_worker.account->local(), peer_local = peer_worker.account->local();
    if (speedup == 0)
        passed = passed && checked.credited == 0 && checked.children == 0 && global == 0;
    else
        passed = passed && checked.selected_samples > 0 && checked.credited == checked.selected_samples * plan->credit()
            && selected_local >= checked.credited && checked.children > 0 && global >= checked.credited
            && global - peer_local <= ledger_debt_limit_ns;

    std::fprintf(output, ",\"setup\":true,\"rounds\":%llu,\"calibrationNs\":%llu,\"restored\":%d,\"global\":%llu,\"sequences\":%llu,\"failure\":%u,"
        "\"replay\":{\"valid\":%s,\"global\":%llu,\"selectedSamples\":%llu,\"credited\":%llu,\"publishes\":%llu,"
        "\"children\":%llu,\"childMax\":%llu,\"measured\":%llu,\"deferred\":%llu,\"failures\":%llu,\"maxDebt\":%llu},"
        "\"foreignSignals\":%llu,\"sigioFallbacks\":%llu,\"workers\":[",
        (unsigned long long)rounds, (unsigned long long)calibration_ns, restored, (unsigned long long)global, (unsigned long long)total, unsigned(experiment.failure()),
        checked.valid ? "true" : "false", (unsigned long long)checked.global, (unsigned long long)checked.selected_samples,
        (unsigned long long)checked.credited, (unsigned long long)checked.publishes, (unsigned long long)checked.children,
        (unsigned long long)checked.child_max, (unsigned long long)checked.measured, (unsigned long long)checked.deferred,
        (unsigned long long)checked.failures, (unsigned long long)checked.max_debt,
        (unsigned long long)Session::foreignSignals(), (unsigned long long)Session::sigioFallbacks());
    for (const auto* worker : {&selected_worker, &peer_worker}) {
        std::fprintf(output, "%s{\"selected\":%s,\"create\":%d,\"start\":%d,\"lifecycle\":%d,\"local\":%llu,\"epochComplete\":%s,"
            "\"accepted\":%llu,\"lost\":%llu,\"closeSteps\":%llu,\"closeOutcome\":%d,\"wallNs\":%llu,\"cpuNs\":%llu,"
            "\"deliveries\":%llu,\"dispatches\":%zu,\"settlements\":%llu,\"maxHandlerNs\":%llu,\"retireError\":%d,\"quiescent\":%s,"
            "\"events\":%zu}", worker == &selected_worker ? "" : ",", worker->selected ? "true" : "false", worker->create,
            worker->start, worker->lifecycle, (unsigned long long)worker->account->local(),
            worker->epoch.complete ? "true" : "false", (unsigned long long)worker->epoch.accepted,
            (unsigned long long)worker->epoch.lost, (unsigned long long)worker->epoch.close_steps, int(worker->closure.outcome),
            (unsigned long long)(worker->wall_finished - worker->wall_started),
            (unsigned long long)(worker->cpu_finished - worker->cpu_started), (unsigned long long)worker->deliveries,
            worker->dispatches, (unsigned long long)worker->settlements, (unsigned long long)worker->max_handler,
            worker->retirement.error, worker->retirement.quiescent ? "true" : "false", worker->account->events().size());
    }
    std::fprintf(output, "],\"events\":[");
    bool first = true;
    for (const auto* worker : {&selected_worker, &peer_worker})
        for (const auto& event : worker->account->events()) {
            std::fprintf(output, "%s[%d,%llu,%u,%llu,%llu,%llu,%llu,%llu,%llu,%u]", first ? "" : ",", worker->selected ? 0 : 1,
                (unsigned long long)event.sequence, unsigned(event.kind), (unsigned long long)event.observed_global,
                (unsigned long long)event.local_before, (unsigned long long)event.amount, (unsigned long long)event.requested,
                (unsigned long long)event.time, (unsigned long long)event.ip, event.detail);
            first = false;
        }
    std::fprintf(output, "],\"eventFields\":[\"worker\",\"sequence\",\"kind\",\"observedGlobal\",\"localBefore\",\"amount\","
        "\"requested\",\"time\",\"ip\",\"detail\"],\"passed\":%s}\n", passed ? "true" : "false");
    return finish(output) && passed ? 0 : 1;
}
