#include "ServiceOwner.h"
#include <atomic>
#include <cerrno>
#include <chrono>
#include <cstdlib>
#include <string_view>

using namespace coz::service;
static void require(bool value, int code) { if (!value) std::_Exit(code); }
class Fake final : public Transport {
public:
    std::atomic<bool> held{false};
    std::atomic<int> restores{0};
    std::atomic<int> reads{0};
    std::atomic<bool> held_slot[2]{};
    std::atomic<bool> mismatch{false};
    std::atomic<uint64_t> clock{1000};
    std::atomic<int> restore_error{0};
    uint64_t overshoot{};
    uint64_t outcome{2};
    int startup{};
    bool reject{};
    bool ambiguous{}, reuse_second{}, zero_second{}, hold_on_mismatch{}, short_wait{};
    int invalid_ack{}, reject_slot{-1};
    std::thread::id owner;
    int install() noexcept override { owner = std::this_thread::get_id(); return startup; }
    Send request(int slot, uint64_t requested) noexcept override {
        require(std::this_thread::get_id() == owner, 90);
        if (reject || slot == reject_slot) return {SendState::rejected, 0, EBUSY};
        require(!pending[slot], 91);
        pending[slot] = ++sequence;
        if (sequence > 1 && reuse_second) pending[slot] = 1;
        if (sequence > 1 && zero_second) pending[slot] = 0;
        amounts[slot] = requested;
        return {ambiguous && sequence > 1 ? SendState::ambiguous : SendState::sent, pending[slot], 0};
    }
    Read read(int slot, uint64_t expected) noexcept override {
        require(std::this_thread::get_id() == owner && pending[slot] == expected, 92);
        ++reads;
        if (held.load() || held_slot[slot].load()) return {ReadState::pending, {}, 0};
        const auto wait = outcome == 1 ? 0 : (short_wait ? amounts[slot] / 2 : amounts[slot]) + overshoot;
        Acknowledgment ack{expected, outcome, 100, wait, wait, outcome == 3 ? -EINTR : 0,
                           1000, 1001, 1002, 1002 + wait, 1003 + wait};
        if (mismatch.exchange(false)) {
            if (hold_on_mismatch) held_slot[slot] = true;
            ack.sequence += 100;
            return {ReadState::completed, ack, 0};
        }
        switch (invalid_ack) {
        case 1: ack.handler_ns = 5000001; ack.handler_finished = ack.handler_started + ack.handler_ns; ack.observed = ack.handler_finished; break;
        case 2: ack.observed = ack.started + 100000001; break;
        case 3: ack.ip = 101; break;
        case 4: ack.handler_started = ack.started - 1; break;
        case 5: ++ack.handler_ns; break;
        case 6: ack.wait_ns = ack.handler_ns + 1; break;
        case 7: ack.outcome = 4; break;
        case 8: ack.wait_result = -EIO; break;
        default: break;
        }
        pending[slot] = 0;
        return {ReadState::completed, ack, 0};
    }
    bool eligible(uint64_t ip) const noexcept override { return ip == 100; }
    uint64_t now() const noexcept override { return clock.load(); }
    int restore() noexcept override {
        require(std::this_thread::get_id() == owner && pending[0] == 0 && pending[1] == 0, 93);
        ++restores;
        return restore_error.load();
    }
private:
    uint64_t sequence{}, pending[2]{}, amounts[2]{};
};

template<class Predicate>
static Snapshot until(Owner& owner, int slot, Identity identity, Predicate predicate) {
    const auto limit = std::chrono::steady_clock::now() + std::chrono::seconds(2);
    Snapshot value;
    do {
        require(owner.snapshot(slot, identity, value), 80);
        if (predicate(value)) return value;
        std::this_thread::yield();
    } while (std::chrono::steady_clock::now() < limit);
    std::_Exit(81);
}
static Snapshot terminal(Owner& owner, int slot, Identity identity) {
    return until(owner, slot, identity, [](const Snapshot& value) { return value.phase != Phase::active && !value.outstanding; });
}

static void failedClockRetainsPendingChild(uint64_t clock, Error error) {
    const Identity id{1, 2};
    Fake transport;
    transport.held = true;
    Owner owner(transport);
    require(owner.start() == 0 && owner.submit(1, id, 1, 110000), 30);
    until(owner, 1, id, [](const Snapshot& value) { return value.outstanding; });
    transport.clock = clock;
    const auto failed = until(owner, 1, id, [](const Snapshot& value) { return value.phase == Phase::failed; });
    require(failed.error == error && failed.outstanding && failed.children.empty(), 31);
    require(!owner.release(1, id) && owner.shutdown() == EBUSY && transport.restores == 0, 32);
    transport.held = false;
    const auto done = terminal(owner, 1, id);
    require(done.phase == Phase::failed && done.error == error && done.children.size() == 1 && done.measured == 50000, 33);
    require(owner.release(1, id) && !owner.submit(1, {3, 4}, 2, 1) && owner.shutdown() == 0, 34);
}

static void failedRestoreRejectsNewWork() {
    Fake transport;
    transport.restore_error = EBUSY;
    Owner owner(transport);
    require(owner.start() == 0 && owner.shutdown() == EBUSY && transport.restores == 1, 35);
    require(!owner.submit(1, {1, 2}, 1, 1), 36);
    transport.restore_error = 0;
    require(owner.shutdown() == 0 && transport.restores == 2, 37);
}

static void invalidAcknowledgmentRetainsRawChild(int invalid) {
    const Identity id{1, 2};
    Fake transport;
    transport.invalid_ack = invalid;
    Owner owner(transport);
    require(owner.start() == 0 && owner.submit(1, id, 1, 110000), 38);
    const auto done = terminal(owner, 1, id);
    require(done.phase == Phase::failed && done.error == Error::invalid_acknowledgment && done.measured == 0 && done.children.size() == 1, 39);
    const auto& ack = done.children[0].acknowledgment;
    require(ack.sequence == 1 && ack.started == 1000 && done.children[0].requested == 50000, 40);
    require(owner.release(1, id) && owner.shutdown() == 0, 41);
}

static void mismatchedAcknowledgmentRetainsEvidence() {
    const Identity id{1, 2};
    Fake transport;
    transport.mismatch = true;
    transport.hold_on_mismatch = true;
    Owner owner(transport);
    require(owner.start() == 0 && owner.submit(1, id, 1, 110000), 42);
    const auto failed = until(owner, 1, id, [](const Snapshot& value) { return !value.rejected.empty(); });
    require(failed.error == Error::invalid_sequence && failed.outstanding && failed.measured == 0 && failed.children.empty(), 43);
    require(failed.rejected[0].sequence == 101 && failed.rejected[0].wait_ns == 50000 && failed.rejected[0].started == 1000, 44);
    require(!owner.release(1, id) && owner.shutdown() == EBUSY, 45);
    transport.held_slot[1] = false;
    const auto done = terminal(owner, 1, id);
    require(done.phase == Phase::failed && done.error == Error::invalid_sequence && !done.rejected.empty() && done.children.size() == 1 && done.measured == 50000, 46);
    require(owner.release(1, id) && owner.shutdown() == 0, 47);
}

static void selectedSlotStopsAfterOneChild() {
    const Identity id{1, 2};
    Fake transport;
    transport.short_wait = true;
    Owner owner(transport);
    require(owner.start() == 0 && owner.submit(0, id, 1, 50000), 48);
    const auto done = terminal(owner, 0, id);
    require(done.phase == Phase::completed && done.children.size() == 1 && done.measured == 25000, 49);
    require(owner.release(0, id) && owner.shutdown() == 0, 50);
}

static void sessionFaultCancelsPendingPeer() {
    const Identity id{1, 2};
    Fake transport;
    transport.held_slot[1] = true;
    transport.reject_slot = 0;
    Owner owner(transport);
    require(owner.start() == 0 && owner.submit(1, id, 1, 110000), 51);
    until(owner, 1, id, [](const Snapshot& value) { return value.outstanding; });
    require(owner.submit(0, id, 1, 50000), 52);
    const auto selected = terminal(owner, 0, id);
    const auto peer = until(owner, 1, id, [](const Snapshot& value) { return value.cancellation; });
    require(selected.error == Error::send_rejected && peer.outstanding && !owner.release(1, id), 53);
    transport.held_slot[1] = false;
    const auto done = terminal(owner, 1, id);
    require(done.phase == Phase::cancelled && done.children.size() == 1 && done.measured == 50000, 54);
    require(owner.release(0, id) && owner.release(1, id) && owner.shutdown() == 0, 55);
}

static void ambiguousDeliveryResolvesOnlyMatchingChild() {
    const Identity id{1, 2};
    Fake transport;
    transport.ambiguous = true;
    Owner owner(transport);
    require(owner.start() == 0 && owner.submit(1, id, 1, 110000), 56);
    const auto done = terminal(owner, 1, id);
    require(done.error == Error::ambiguous_delivery && done.children.size() == 2 && done.measured == 100000, 57);
    require(owner.release(1, id) && owner.shutdown() == 0, 58);
}

[[noreturn]] static void invalidSendSequenceRemainsQuarantined(std::string_view mode) {
    const Identity id{1, 2};
    Fake transport;
    transport.ambiguous = mode != "sent-reused";
    transport.zero_second = mode == "ambiguous-zero";
    transport.reuse_second = !transport.zero_second;
    Owner owner(transport);
    require(owner.start() == 0 && owner.submit(1, id, 1, 110000), 59);
    const auto failed = until(owner, 1, id, [](const Snapshot& value) { return value.phase == Phase::failed; });
    require(failed.error == Error::invalid_sequence && failed.outstanding && failed.children.size() == 1 && failed.measured == 50000, 60);
    require(transport.reads == 1 && !owner.release(1, id) && owner.shutdown() == EBUSY && transport.restores == 0, 61);
    std::_Exit(0);
}

[[noreturn]] static void rejectedJournalLimitRetainsOwnership() {
    const Identity id{1, 2};
    Fake transport;
    transport.mismatch = true;
    Owner owner(transport, 1);
    require(owner.start() == 0 && owner.submit(1, id, 1, 110000), 62);
    const auto failed = until(owner, 1, id, [](const Snapshot& value) { return value.rejected.size() == 1; });
    require(failed.error == Error::invalid_sequence && failed.outstanding && failed.children.empty() && failed.measured == 0, 63);
    require(transport.reads == 1 && !owner.release(1, id) && owner.shutdown() == EBUSY && transport.restores == 0, 64);
    std::_Exit(0);
}

int main(int argc, char** argv) {
    if (argc == 2) {
        const std::string_view mode{argv[1]};
        if (mode == "rejected-limit") rejectedJournalLimitRetainsOwnership();
        require(mode == "ambiguous-reused" || mode == "ambiguous-zero" || mode == "sent-reused", 65);
        invalidSendSequenceRemainsQuarantined(mode);
    }
    require(argc == 1, 66);
    for (int invalid = 1; invalid <= 8; ++invalid) invalidAcknowledgmentRetainsRawChild(invalid);
    mismatchedAcknowledgmentRetainsEvidence();
    selectedSlotStopsAfterOneChild();
    sessionFaultCancelsPendingPeer();
    ambiguousDeliveryResolvesOnlyMatchingChild();
    failedClockRetainsPendingChild(0, Error::clock_failed);
    failedClockRetainsPendingChild(999, Error::clock_failed);
    failedClockRetainsPendingChild(100001001, Error::acknowledgment_timeout);
    failedRestoreRejectsNewWork();
    const Identity id{1, 2};
    {
        Fake transport;
        Owner owner(transport);
        require(owner.start() == 0, 1);
        require(transport.owner != std::this_thread::get_id(), 2);
        require(!owner.submit(1, {}, 1, 1) && !owner.submit(0, id, 1, 50001)
                && !owner.submit(1, id, 1, 2000001), 3);
        require(owner.submit(1, id, 1, 110000), 4);
        const auto done = terminal(owner, 1, id);
        require(done.phase == Phase::completed && done.measured == 110000 && done.children.size() == 3, 5);
        require(done.children[0].requested == 50000 && done.children[1].requested == 50000 && done.children[2].requested == 10000, 6);
        for (std::size_t i = 0; i < done.children.size(); ++i) {
            require(done.children[i].ordinal == i + 1 && done.children[i].acknowledgment.sequence == i + 1, 7);
        }
        require(owner.shutdown() == EBUSY && owner.release(1, id), 8);
        require(!owner.submit(1, {3, 4}, 1, 1), 9);
        require(owner.shutdown() == 0, 10);
    }
    {
        Fake transport;
        transport.held = true;
        Owner owner(transport);
        require(owner.start() == 0 && owner.submit(1, id, 1, 110000), 11);
        until(owner, 1, id, [](const Snapshot& value) { return value.outstanding; });
        require(!owner.cancel(1, {5, 6}) && owner.cancel(1, id) && owner.cancel(1, id), 12);
        Snapshot waiting;
        require(owner.snapshot(1, id, waiting) && waiting.outstanding && waiting.children.empty(), 13);
        require(!owner.release(1, id) && owner.shutdown() == EBUSY && transport.restores == 0, 14);
        transport.held = false;
        const auto done = terminal(owner, 1, id);
        require(done.phase == Phase::cancelled && done.children.size() == 1 && done.measured == 50000, 15);
        require(owner.release(1, id) && owner.shutdown() == 0, 16);
    }
    for (uint64_t outcome : {1ULL, 3ULL}) {
        Fake transport;
        transport.outcome = outcome;
        Owner owner(transport);
        require(owner.start() == 0 && owner.submit(1, id, 1, 110000), 17);
        const auto done = terminal(owner, 1, id);
        require(done.phase == Phase::completed && done.children.size() == 1
                && done.measured == (outcome == 1 ? 0 : 50000), 18);
        require(owner.release(1, id) && owner.shutdown() == 0, 19);
    }
    {
        Fake transport;
        transport.overshoot = 70000;
        Owner owner(transport);
        require(owner.start() == 0 && owner.submit(1, id, 1, 110000), 20);
        const auto done = terminal(owner, 1, id);
        require(done.phase == Phase::completed && done.children.size() == 1 && done.measured == 120000, 21);
        require(owner.release(1, id) && owner.shutdown() == 0, 22);
    }
    {
        Fake transport;
        Owner owner(transport, 1);
        require(owner.start() == 0 && owner.submit(1, id, 1, 110000), 23);
        const auto done = terminal(owner, 1, id);
        require(done.phase == Phase::failed && done.error == Error::journal_full && done.children.size() == 1, 24);
        require(owner.release(1, id) && !owner.submit(1, {3, 4}, 2, 1) && owner.shutdown() == 0, 25);
    }
    {
        Fake transport;
        transport.reject = true;
        Owner owner(transport);
        require(owner.start() == 0 && owner.submit(1, id, 1, 1), 26);
        const auto done = terminal(owner, 1, id);
        require(done.phase == Phase::failed && done.error == Error::send_rejected && done.children.empty(), 27);
        require(owner.release(1, id) && owner.shutdown() == 0, 28);
    }
    {
        Fake transport;
        transport.startup = EBUSY;
        Owner owner(transport);
        require(owner.start() == EBUSY && !owner.submit(1, id, 1, 1) && transport.restores == 0, 29);
    }
    return 0;
}
