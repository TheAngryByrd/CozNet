#include "TargetDelayLedger.h"
#include <functional>
#include <stdexcept>
#include <vector>

using namespace coz::service;
namespace {
void require(bool condition, const char* message) {
    if (!condition) throw std::runtime_error(message);
}

constexpr InstructionSpan selected_span{0x1000, 0x2000}, eligible_span{0x1000, 0x3000};
constexpr uint64_t selected_ip = 0x1800, peer_ip = 0x2800, runtime_ip = 0x9000;

struct Clock {
    uint64_t value{1000}, step{10}, zero_at{}, calls{};
    uint64_t now() noexcept {
        ++calls;
        if (zero_at != 0 && calls == zero_at) return 0;
        value += step;
        return value;
    }
};

struct Waiter {
    uint64_t overshoot{}, fail_at{}, calls{};
    Clock* clock{};
    LedgerWait wait(uint64_t requested) noexcept {
        ++calls;
        if (clock) clock->value += requested + overshoot;
        if (fail_at != 0 && calls == fail_at) return {LedgerWaitStatus::failed, requested / 2};
        return {LedgerWaitStatus::completed, requested + overshoot};
    }
};

LedgerPlan plan(uint32_t speedup = 50) {
    const InstructionSpan selected[] = {selected_span};
    const InstructionSpan eligible[] = {eligible_span};
    return *LedgerPlan::parse(speedup, 1000000, selected, eligible);
}

PerfCpuSample sample(uint64_t ip, uint64_t time = 1) { return {ip, time, 1000000, time * 1000000, 7, 14}; }

struct Thread {
    std::vector<LedgerEvent> storage;
    ThreadDelayAccount account;
    Thread(ExperimentDelay& experiment, const LedgerPlan& plan, size_t capacity = 256)
        : storage(capacity), account(experiment, plan, storage) {}
};

void plansRejectInvalidInputs() {
    const InstructionSpan valid[] = {selected_span};
    const InstructionSpan overlapping[] = {{0x1000, 0x2000}, {0x1fff, 0x3000}};
    const InstructionSpan empty_span[] = {{0x1000, 0x1000}};
    const InstructionSpan zero[] = {{0, 0x10}};
    require(!LedgerPlan::parse(101, 1000000, valid, valid) && !LedgerPlan::parse(50, 0, valid, valid),
        "A speedup above 100 or a zero period was accepted.");
    require(!LedgerPlan::parse(50, 1000000, valid, {}) && !LedgerPlan::parse(50, 1000000, overlapping, valid)
        && !LedgerPlan::parse(50, 1000000, empty_span, valid) && !LedgerPlan::parse(50, 1000000, zero, valid),
        "An invalid instruction set was accepted.");
    require(plan().credit() == 500000 && plan(0).credit() == 0 && plan(100).credit() == 1000000,
        "Selected credit must be the nominal period times the speedup.");
}

void selectedSamplesCreditLocalDelay() {
    ExperimentDelay experiment;
    const auto ledger = plan();
    Thread thread(experiment, ledger);
    require(thread.account.accept(sample(selected_ip), 1, 1) && thread.account.local() == 500000,
        "A selected sample must add nominal credit.");
    require(thread.account.accept(sample(peer_ip, 2), 1, 1) && thread.account.local() == 500000,
        "An unselected sample must add no credit.");
    const auto events = thread.account.events();
    require(events.size() == 2 && events[0].kind == LedgerEventKind::sample && events[0].detail == 1 && events[0].amount == 500000
        && events[1].detail == 0 && events[1].amount == 0 && events[1].local_before == 500000 && experiment.global() == 0,
        "Sample events must record selection, amount, and the prior local delay without publication.");
}

void creditPublishesAndPeersPay() {
    ExperimentDelay experiment;
    const auto ledger = plan();
    Thread selected(experiment, ledger), peer(experiment, ledger);
    Clock clock;
    Waiter waiter{0, 0, 0, &clock};
    selected.account.accept(sample(selected_ip), 1, 1);
    auto published = selected.account.settle(clock, waiter, selected_ip, clock.now());
    require(published.outcome == SettleOutcome::published && experiment.global() == 500000
        && selected.account.events().back().kind == LedgerEventKind::publish && selected.account.events().back().observed_global == 0,
        "Local credit above global delay must publish.");
    auto idle = selected.account.settle(clock, waiter, selected_ip, clock.now());
    require(idle.outcome == SettleOutcome::caught_up && selected.account.events().size() == 2, "An equal local delay published again.");
    auto paid = peer.account.settle(clock, waiter, peer_ip, clock.now());
    require(paid.outcome == SettleOutcome::caught_up && paid.debt_before == 500000 && paid.debt_after == 0
        && paid.children == 10 && paid.requested == 500000 && peer.account.local() == 500000,
        "A peer must pay its whole debt in children of at most 50 us.");
    for (const auto& event : peer.account.events())
        require(event.kind == LedgerEventKind::child && event.requested <= ledger_child_limit_ns && event.amount == event.requested,
            "Each child must record its request and measured wait.");
}

void overshootPublishesMeasuredWait() {
    ExperimentDelay experiment;
    const auto ledger = plan();
    Thread selected(experiment, ledger), peer(experiment, ledger);
    Clock clock;
    Waiter waiter{700, 0, 0, &clock};
    selected.account.accept(sample(selected_ip), 1, 1);
    selected.account.settle(clock, waiter, selected_ip, clock.now());
    auto peer_result = peer.account.settle(clock, waiter, peer_ip, clock.now());
    require(peer_result.outcome == SettleOutcome::published && peer.account.local() == 500700 && experiment.global() == 500700,
        "Measured overshoot must stay in local delay and publish.");
    auto selected_result = selected.account.settle(clock, waiter, selected_ip, clock.now());
    require(selected_result.debt_before == 700 && selected.account.local() >= experiment.global(),
        "The selected thread must pay delay that overshoot published.");
}

void ineligibleInstructionsDeferPayment() {
    ExperimentDelay experiment;
    const auto ledger = plan();
    Thread selected(experiment, ledger), peer(experiment, ledger);
    Clock clock;
    Waiter waiter{0, 0, 0, &clock};
    selected.account.accept(sample(selected_ip), 1, 1);
    selected.account.settle(clock, waiter, selected_ip, clock.now());
    auto deferred = peer.account.settle(clock, waiter, runtime_ip, clock.now());
    require(deferred.outcome == SettleOutcome::ineligible_deferred && deferred.debt_after == 500000 && waiter.calls == 0
        && peer.account.local() == 0 && peer.account.events().back().kind == LedgerEventKind::deferred,
        "Payment at an ineligible instruction must defer without waiting.");
}

void excessDebtFailsWithoutForgiveness() {
    ExperimentDelay experiment;
    const auto ledger = plan(100);
    Thread selected(experiment, ledger), peer(experiment, ledger);
    Clock clock;
    Waiter waiter{0, 0, 0, &clock};
    for (uint64_t time = 1; time <= 3; ++time) selected.account.accept(sample(selected_ip, time), 1, 1);
    selected.account.settle(clock, waiter, selected_ip, clock.now());
    auto result = peer.account.settle(clock, waiter, peer_ip, clock.now());
    require(experiment.failure() == LedgerFailure::debt_exceeded && result.outcome == SettleOutcome::budget_deferred
        && result.requested == ledger_payment_limit_ns && result.children == 40 && result.debt_after == 1000000
        && peer.account.local() == 2000000, "Excess debt must fail the experiment and still pay one lease.");
    require(peer.account.events().front().kind == LedgerEventKind::failure, "The debt failure must be journaled first.");
    peer.account.settle(clock, waiter, peer_ip, clock.now());
    require(peer.account.local() == 3000000 && experiment.failure() == LedgerFailure::debt_exceeded,
        "A later dispatch must pay the remaining debt and keep the failure.");
}

void handlerBudgetDefersPayment() {
    ExperimentDelay experiment;
    const auto ledger = plan();
    Thread selected(experiment, ledger), peer(experiment, ledger);
    Clock clock;
    Waiter waiter{0, 0, 0, &clock};
    selected.account.accept(sample(selected_ip), 1, 1);
    selected.account.settle(clock, waiter, selected_ip, clock.now());
    const auto started = clock.now();
    clock.value += ledger_handler_limit_ns - ledger_handler_reserve_ns;
    auto result = peer.account.settle(clock, waiter, peer_ip, started);
    require(result.outcome == SettleOutcome::budget_deferred && result.children == 0 && waiter.calls == 0,
        "A child that would pass the handler reserve must not start.");
}

void failuresAreSticky() {
    {
        ExperimentDelay experiment;
        const auto ledger = plan();
        Thread selected(experiment, ledger), peer(experiment, ledger);
        Clock clock;
        Waiter waiter{0, 2, 0, &clock};
        selected.account.accept(sample(selected_ip), 1, 1);
        selected.account.settle(clock, waiter, selected_ip, clock.now());
        auto result = peer.account.settle(clock, waiter, peer_ip, clock.now());
        require(result.outcome == SettleOutcome::failed && experiment.failure() == LedgerFailure::wait_failed
            && peer.account.local() == 75000, "A failed wait must keep its measured part and fail the experiment.");
    }
    {
        ExperimentDelay experiment;
        const auto ledger = plan();
        Thread selected(experiment, ledger), peer(experiment, ledger);
        Clock clock;
        Waiter waiter{0, 0, 0, &clock};
        selected.account.accept(sample(selected_ip), 1, 1);
        selected.account.settle(clock, waiter, selected_ip, clock.now());
        const auto started = clock.now();
        clock.zero_at = clock.calls + 1;
        require(peer.account.settle(clock, waiter, peer_ip, started).outcome == SettleOutcome::failed
            && experiment.failure() == LedgerFailure::clock_failed, "A failed clock must fail the experiment.");
    }
    {
        ExperimentDelay experiment;
        const auto ledger = plan();
        Thread small(experiment, ledger, ThreadDelayAccount::settle_reserve);
        require(small.account.available() == 0, "A journal at its settle reserve must report no capacity.");
        require(!small.account.accept(sample(selected_ip), 1, 1) && experiment.failure() == LedgerFailure::journal_full
            && small.account.local() == 0, "A full journal must reject the sample and fail the experiment.");
    }
}

void eventClockFailureFailsTheExperiment() {
    ExperimentDelay experiment;
    const auto ledger = plan();
    Thread selected(experiment, ledger);
    Clock clock;
    Waiter waiter{0, 0, 0, &clock};
    selected.account.accept(sample(selected_ip), 1, 1);
    clock.zero_at = clock.calls + 1;
    selected.account.settle(clock, waiter, selected_ip, 1);
    const auto events = selected.account.events();
    require(experiment.failure() == LedgerFailure::clock_failed && events.back().kind == LedgerEventKind::failure
        && events.back().detail == uint32_t(LedgerFailure::clock_failed), "A failed event clock must fail the experiment and journal it.");
}

void childClockFailureKeepsLocalOrder() {
    ExperimentDelay experiment;
    const auto ledger = plan();
    Thread selected(experiment, ledger), peer(experiment, ledger);
    Clock clock;
    Waiter waiter{0, 0, 0, &clock};
    selected.account.accept(sample(selected_ip), 1, 1);
    selected.account.settle(clock, waiter, selected_ip, clock.now());
    const auto started = clock.now();
    clock.zero_at = clock.calls + 2;
    peer.account.settle(clock, waiter, peer_ip, started);
    const auto events = peer.account.events();
    require(events.size() >= 2 && events[0].kind == LedgerEventKind::child && events[1].kind == LedgerEventKind::failure
        && events[1].local_before == events[0].local_before + events[0].amount,
        "A clock failure after a child must record the local delay that includes the child.");
    selected.account.accept(sample(selected_ip, 2), 1, 1);
    selected.account.settle(clock, waiter, selected_ip, clock.now());
    const auto later = clock.now();
    clock.zero_at = clock.calls + 2;
    peer.account.settle(clock, waiter, peer_ip, later);
    size_t clock_events = 0;
    for (const auto& event : peer.account.events())
        clock_events += event.kind == LedgerEventKind::failure && event.detail == uint32_t(LedgerFailure::clock_failed);
    require(clock_events == 1, "Only the first clock failure must append a failure event.");
}


void publish(Thread& thread, Clock& clock, Waiter& waiter, uint64_t samples, uint64_t& time) {
    for (uint64_t index = 0; index < samples; ++index) thread.account.accept(sample(selected_ip, ++time), 1, 1);
    thread.account.settle(clock, waiter, selected_ip, clock.now());
}

void dependencyWakeSkipsOnlyToToken() {
    ExperimentDelay experiment;
    const auto ledger = plan();
    Thread selected(experiment, ledger), peer(experiment, ledger);
    Clock clock;
    Waiter waiter{0, 0, 0, &clock};
    uint64_t time = 0;
    require(peer.account.beginBlock(clock, peer_ip) && peer.account.blocked(), "A runnable thread must block.");
    publish(selected, clock, waiter, 2, time);
    const auto token = selected.account.post(clock, selected_ip);
    require(token && *token == 1000000 && experiment.global() == 1000000, "A caught-up post must return its local delay.");
    publish(selected, clock, waiter, 1, time);
    require(experiment.global() == 1500000, "Credit after the post must publish.");
    require(peer.account.settle(clock, waiter, peer_ip, clock.now()).outcome == SettleOutcome::blocked && waiter.calls == 0,
        "A blocked thread must not pay.");
    require(peer.account.endBlock(clock, peer_ip, *token, WakeReason::dependency) && peer.account.local() == 1000000,
        "A dependency wake must skip exactly to the token.");
    const auto paid = peer.account.settle(clock, waiter, peer_ip, clock.now());
    require(paid.debt_before == 500000 && paid.requested == 500000 && peer.account.local() == 1500000,
        "Credit published after the post must be paid, not skipped.");
    const auto events = peer.account.events();
    require(events[0].kind == LedgerEventKind::block && events[0].observed_global == 0
        && events[1].kind == LedgerEventKind::wake && events[1].amount == 1000000 && events[1].requested == 1000000
        && events[1].detail == uint32_t(WakeReason::dependency), "Block and wake events must record the skip and its token.");
    require(experiment.failure() == LedgerFailure::none, "A valid protocol must not fail.");
}

void otherWakesSkipNothing() {
    for (const auto reason : {WakeReason::timeout, WakeReason::cancelled, WakeReason::spurious}) {
        ExperimentDelay experiment;
        const auto ledger = plan();
        Thread selected(experiment, ledger), peer(experiment, ledger);
        Clock clock;
        Waiter waiter{0, 0, 0, &clock};
        uint64_t time = 0;
        peer.account.beginBlock(clock, peer_ip);
        publish(selected, clock, waiter, 2, time);
        const auto token = selected.account.post(clock, selected_ip);
        require(peer.account.endBlock(clock, peer_ip, *token, reason) && peer.account.local() == 0
            && peer.account.events().back().requested == 0, "A timeout, cancellation, or spurious wake must skip nothing.");
        require(peer.account.catchUp(clock, waiter, peer_ip) && peer.account.local() == 1000000,
            "The waiter must then pay the delay inserted while it waited.");
    }
}

void debtAtBlockStaysOwed() {
    ExperimentDelay experiment;
    const auto ledger = plan();
    Thread selected(experiment, ledger), peer(experiment, ledger);
    Clock clock;
    Waiter waiter{0, 0, 0, &clock};
    uint64_t time = 0;
    publish(selected, clock, waiter, 1, time);
    peer.account.beginBlock(clock, peer_ip);
    publish(selected, clock, waiter, 1, time);
    const auto token = selected.account.post(clock, selected_ip);
    peer.account.endBlock(clock, peer_ip, *token, WakeReason::dependency);
    require(peer.account.local() == 500000 && experiment.global() == 1000000,
        "A dependency wake must not forgive debt owed at the block.");
}

void postBehindGlobalIsValid() {
    ExperimentDelay experiment;
    const auto ledger = plan();
    Thread selected(experiment, ledger), other(experiment, ledger), peer(experiment, ledger);
    Clock clock;
    Waiter waiter{0, 0, 0, &clock};
    uint64_t time = 0;
    peer.account.beginBlock(clock, peer_ip);
    publish(selected, clock, waiter, 2, time);
    uint64_t other_time = 100;
    publish(other, clock, waiter, 4, other_time);
    const auto token = selected.account.post(clock, selected_ip);
    require(token && *token == 1000000 && experiment.global() == 2000000 && experiment.failure() == LedgerFailure::none,
        "A post behind later global credit must return its own caught-up delay.");
    require(peer.account.endBlock(clock, peer_ip, *token, WakeReason::dependency) && peer.account.local() == 1000000,
        "The waiter must skip only to the releaser's token.");
}

void wakeClockFailureKeepsLocalOrder() {
    ExperimentDelay experiment;
    const auto ledger = plan();
    Thread selected(experiment, ledger), peer(experiment, ledger);
    Clock clock;
    Waiter waiter{0, 0, 0, &clock};
    uint64_t time = 0;
    peer.account.beginBlock(clock, peer_ip);
    publish(selected, clock, waiter, 2, time);
    const auto token = selected.account.post(clock, selected_ip);
    clock.zero_at = clock.calls + 1;
    peer.account.endBlock(clock, peer_ip, *token, WakeReason::dependency);
    const auto events = peer.account.events();
    require(events.back().kind == LedgerEventKind::failure && events.back().local_before == 1000000,
        "A clock failure after a wake must record the local delay that includes the skip.");
}

void catchUpPaysAllDebt() {
    ExperimentDelay experiment;
    const auto ledger = plan();
    Thread selected(experiment, ledger), peer(experiment, ledger);
    Clock clock;
    Waiter waiter{0, 0, 0, &clock};
    uint64_t time = 0;
    publish(selected, clock, waiter, 3, time);
    require(peer.account.catchUp(clock, waiter, peer_ip) && peer.account.local() == 1500000,
        "Catch-up at an eligible address must pay all debt.");
    publish(selected, clock, waiter, 1, time);
    require(!peer.account.catchUp(clock, waiter, runtime_ip) && peer.account.local() == 1500000,
        "Catch-up at an ineligible address must not pay.");
}

void protocolViolationsFail() {
    const std::function<bool(Thread&, Thread&, Clock&, Waiter&)> cases[] = {
        [](Thread&, Thread& peer, Clock& clock, Waiter&) { peer.account.beginBlock(clock, peer_ip); return !peer.account.beginBlock(clock, peer_ip); },
        [](Thread&, Thread& peer, Clock& clock, Waiter&) { return !peer.account.endBlock(clock, peer_ip, 0, WakeReason::dependency); },
        [](Thread&, Thread& peer, Clock& clock, Waiter&) {
            peer.account.beginBlock(clock, peer_ip);
            return !peer.account.endBlock(clock, peer_ip, 1, WakeReason::dependency);
        },
        [](Thread& selected, Thread& peer, Clock& clock, Waiter& waiter) {
            uint64_t time = 0;
            publish(selected, clock, waiter, 2, time);
            selected.account.post(clock, selected_ip);
            peer.account.beginBlock(clock, peer_ip);
            return !peer.account.endBlock(clock, peer_ip, 1000001, WakeReason::dependency);
        },
        [](Thread&, Thread& peer, Clock& clock, Waiter&) { peer.account.beginBlock(clock, peer_ip); return !peer.account.post(clock, peer_ip); },
        [](Thread&, Thread& peer, Clock& clock, Waiter&) {
            peer.account.beginBlock(clock, peer_ip);
            return !peer.account.endBlock(clock, peer_ip, 0, WakeReason(9));
        },
    };
    for (const auto& violation : cases) {
        ExperimentDelay experiment;
        const auto ledger = plan();
        Thread selected(experiment, ledger), peer(experiment, ledger);
        Clock clock;
        Waiter waiter{0, 0, 0, &clock};
        require(violation(selected, peer, clock, waiter) && experiment.failure() == LedgerFailure::protocol
            && peer.account.events().back().kind == LedgerEventKind::failure, "A protocol violation must fail the experiment and journal it.");
    }
}

void sequencesAreUniqueAndContiguous() {
    ExperimentDelay experiment;
    const auto ledger = plan();
    Thread selected(experiment, ledger), peer(experiment, ledger);
    Clock clock;
    Waiter waiter{300, 0, 0, &clock};
    for (uint64_t time = 1; time <= 4; ++time) {
        selected.account.accept(sample(selected_ip, time), 1, 1);
        selected.account.settle(clock, waiter, selected_ip, clock.now());
        peer.account.accept(sample(peer_ip, time), 1, 1);
        peer.account.settle(clock, waiter, peer_ip, clock.now());
    }
    std::vector<bool> seen(experiment.sequences() + 1);
    for (const auto* account : {&selected.account, &peer.account}) {
        uint64_t prior = 0;
        for (const auto& event : account->events()) {
            require(event.sequence > prior && event.sequence <= experiment.sequences() && !seen[event.sequence],
                "Each event must take one unique increasing sequence.");
            seen[event.sequence] = true;
            prior = event.sequence;
        }
    }
    for (size_t index = 1; index < seen.size(); ++index) require(seen[index], "The event sequences must have no gap.");
}
}

int main() {
    plansRejectInvalidInputs();
    selectedSamplesCreditLocalDelay();
    creditPublishesAndPeersPay();
    overshootPublishesMeasuredWait();
    ineligibleInstructionsDeferPayment();
    excessDebtFailsWithoutForgiveness();
    handlerBudgetDefersPayment();
    failuresAreSticky();
    sequencesAreUniqueAndContiguous();
    eventClockFailureFailsTheExperiment();
    childClockFailureKeepsLocalOrder();
    dependencyWakeSkipsOnlyToToken();
    otherWakesSkipNothing();
    debtAtBlockStaysOwed();
    catchUpPaysAllDebt();
    protocolViolationsFail();
    postBehindGlobalIsValid();
    wakeClockFailureKeepsLocalOrder();
}
