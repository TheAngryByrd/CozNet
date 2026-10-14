#include "TimerJournalSnapshot.h"
#include <array>
#include <cstdlib>

using namespace coz::service;
static void require(bool condition, int code) { if (!condition) std::_Exit(code); }

static void coreRetirementCannotAuthorizeMailboxReuse() {
    LeaseMailbox mailbox;
    const auto submission = mailbox.begin();
    require(submission && mailbox.publish(*submission), 1);
    const auto execution = mailbox.claim(submission->generation());
    require(execution.has_value(), 2);
    std::array<TargetLeaseChild, 2> journal{}, copy{};
    const auto plan = TargetLeasePlan::parse(1, 1, 50000, 100, 20);
    require(plan.has_value(), 3);
    TargetLease lease(*plan, journal);
    require(lease.start(TimerArmResult::accepted(1000)) && lease.cancelPending()
        && lease.quiesced() && lease.retire(), 4);
    std::size_t copied = 99;
    auto reader = mailbox.read(1);
    require(reader && !reader->quiescent(), 5);
    const auto raw = lease.snapshot(copy, copied);
    require(raw && raw->visibility == TargetLeaseVisibility::retired, 6);
    const auto pending = captureTimerJournal(lease, *reader, copy, copied);
    require(pending && pending->visibility == TargetLeaseVisibility::quiesced
        && pending->generation == 1 && pending->children == 0 && !pending->may_have_delivery, 7);
    reader.reset();
    require(!mailbox.begin(), 8);
    require(mailbox.quiesce(*execution), 9);
    auto retiredReader = mailbox.read(1);
    require(retiredReader && retiredReader->quiescent(), 10);
    const auto retired = captureTimerJournal(lease, *retiredReader, copy, copied);
    require(retired && retired->visibility == TargetLeaseVisibility::retired && !mailbox.begin(), 11);
    retiredReader.reset();
    const auto next = mailbox.begin();
    require(next && next->generation() == 2, 12);
}

static void readerMustMatchJournalGeneration() {
    LeaseMailbox mailbox;
    const auto submission = mailbox.begin();
    require(submission && mailbox.publish(*submission), 13);
    auto reader = mailbox.read(1);
    require(reader.has_value(), 14);
    std::array<TargetLeaseChild, 2> journal{}, copy{};
    const auto plan = TargetLeasePlan::parse(2, 1, 50000, 100, 20);
    require(plan.has_value(), 15);
    TargetLease lease(*plan, journal);
    require(lease.start(TimerArmResult::accepted(1000)), 16);
    std::size_t copied = 0;
    require(!captureTimerJournal(lease, *reader, copy, copied), 17);
}

int main() {
    coreRetirementCannotAuthorizeMailboxReuse();
    readerMustMatchJournalGeneration();
    return 0;
}
