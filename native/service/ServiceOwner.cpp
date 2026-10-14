#include "ServiceOwner.h"
#include <algorithm>
#include <cerrno>
#include <stdexcept>

namespace coz::service {
Owner::Owner(Transport& transport, std::size_t capacity) : transport_(transport), capacity_(capacity) {
    if (capacity == 0 || capacity > 100000) throw std::invalid_argument("Invalid service journal capacity.");
    for (auto& slot : slots_) {
        slot.value.children.reserve(capacity);
        slot.value.rejected.reserve(capacity);
    }
}

int Owner::start() {
    std::unique_lock lock(mutex_);
    if (used_) return EALREADY;
    used_ = true;
    thread_ = std::thread([this] { run(); });
    changed_.wait(lock, [this] { return ready_; });
    const int result = startup_error_;
    lock.unlock();
    if (result != 0) thread_.join();
    return result;
}

bool Owner::matches(int index, Identity identity) const {
    return index >= 0 && index < 2 && slots_[index].value.phase != Phase::idle && slots_[index].value.identity == identity;
}

bool Owner::submit(int index, Identity identity, uint64_t command, uint64_t budget) {
    std::lock_guard lock(mutex_);
    if (!installed_ || failed_ || shutdown_requested_ || index < 0 || index >= 2 || identity == Identity{} || command == 0
        || budget == 0 || budget > (index == 0 ? 50000ULL : 2000000ULL)) return false;
    auto& slot = slots_[index];
    if (slot.value.phase != Phase::idle || command <= slot.last_command) return false;
    auto& value = slot.value;
    value.identity = identity;
    value.command = command;
    value.budget = budget;
    value.measured = 0;
    value.phase = Phase::active;
    value.error = Error::none;
    value.transport_error = 0;
    value.cancellation = value.outstanding = false;
    value.children.clear();
    value.rejected.clear();
    slot.last_command = command;
    slot.sequence = slot.pending_since = 0;
    slot.observed_children = 0;
    slot.observed_terminal = false;
    changed_.notify_all();
    return true;
}

bool Owner::cancel(int index, Identity identity) {
    std::lock_guard lock(mutex_);
    if (!matches(index, identity)) return false;
    slots_[index].value.cancellation = true;
    changed_.notify_all();
    return true;
}

bool Owner::snapshot(int index, Identity identity, Snapshot& result) {
    std::lock_guard lock(mutex_);
    if (!matches(index, identity)) return false;
    auto& slot = slots_[index];
    result = slot.value;
    slot.observed_children = result.children.size();
    slot.observed_terminal = result.phase != Phase::active && !result.outstanding;
    return true;
}

bool Owner::release(int index, Identity identity) {
    std::lock_guard lock(mutex_);
    if (!matches(index, identity)) return false;
    auto& slot = slots_[index];
    if (!slot.observed_terminal || slot.value.outstanding || slot.observed_children != slot.value.children.size()) return false;
    slot.value.phase = Phase::idle;
    return true;
}

void Owner::fail(Slot& slot, Error error, int detail) {
    failed_ = true;
    slot.value.phase = Phase::failed;
    if (slot.value.error == Error::none) {
        slot.value.error = error;
        slot.value.transport_error = detail;
    }
}

void Owner::advance(int index, Slot& slot) {
    auto& value = slot.value;
    if (failed_ && value.phase == Phase::active) value.cancellation = true;
    if (value.outstanding) {
        const auto now = transport_.now();
        if (now == 0 || now < slot.pending_since) fail(slot, Error::clock_failed);
        else if (now - slot.pending_since > 100000000ULL) fail(slot, Error::acknowledgment_timeout);
        if (slot.sequence == 0 || value.rejected.size() == capacity_) return;
        const auto read = transport_.read(index, slot.sequence);
        if (read.state == ReadState::pending) return;
        if (read.state == ReadState::failed) { fail(slot, Error::read_failed, read.error); return; }
        const auto& ack = read.value;
        if (ack.sequence != slot.sequence) {
            value.rejected.push_back(ack);
            fail(slot, Error::invalid_sequence);
            return;
        }
        value.outstanding = false;
        const uint64_t requested = std::min<uint64_t>(50000, value.budget - value.measured);
        value.children.push_back({value.children.size() + 1, requested, ack});
        slot.last_sequence = ack.sequence;
        const bool ordered = ack.started > 0 && ack.started <= ack.handler_started
            && ack.handler_started <= ack.handler_finished && ack.handler_finished <= ack.observed
            && ack.started <= ack.queued && ack.queued <= ack.observed;
        const bool outcome = (ack.outcome == 1 && ack.wait_ns == 0 && ack.wait_result == 0)
            || (ack.outcome == 2 && ack.wait_result == 0 && transport_.eligible(ack.ip))
            || (ack.outcome == 3 && ack.wait_result == -EINTR && transport_.eligible(ack.ip));
        if (!ordered || !outcome || ack.handler_ns != ack.handler_finished - ack.handler_started
            || ack.wait_ns > ack.handler_ns || ack.handler_ns > 5000000ULL
            || ack.observed - ack.started > 100000000ULL) {
            fail(slot, Error::invalid_acknowledgment);
            return;
        }
        value.measured += ack.wait_ns;
        if (value.error != Error::none) return;
        if (value.cancellation) value.phase = Phase::cancelled;
        else if (index == 0 || ack.outcome != 2 || value.measured >= value.budget) value.phase = Phase::completed;
        return;
    }
    if (value.phase != Phase::active) return;
    if (value.cancellation) { value.phase = Phase::cancelled; return; }
    if (value.children.size() == capacity_) { fail(slot, Error::journal_full); return; }
    slot.pending_since = transport_.now();
    if (slot.pending_since == 0) { fail(slot, Error::clock_failed); return; }
    const auto sent = transport_.request(index, std::min<uint64_t>(50000, value.budget - value.measured));
    slot.sequence = sent.sequence;
    if (sent.state == SendState::rejected) { fail(slot, Error::send_rejected, sent.error); return; }
    value.outstanding = true;
    if (sent.sequence == 0 || sent.sequence <= slot.last_sequence) {
        slot.sequence = 0;
        fail(slot, Error::invalid_sequence, sent.error);
        return;
    }
    if (sent.state == SendState::ambiguous) fail(slot, Error::ambiguous_delivery, sent.error);
}

void Owner::run() {
    std::unique_lock lock(mutex_);
    startup_error_ = transport_.install();
    installed_ = startup_error_ == 0;
    ready_ = true;
    changed_.notify_all();
    if (!installed_) { exited_ = true; return; }
    for (;;) {
        bool active = false;
        for (int index = 0; index < 2; ++index) {
            advance(index, slots_[index]);
            active |= slots_[index].value.phase == Phase::active || slots_[index].value.outstanding;
        }
        if (shutdown_requested_) {
            shutdown_error_ = transport_.restore();
            if (shutdown_error_ != 0) failed_ = true;
            shutdown_requested_ = false;
            shutdown_finished_ = true;
            if (shutdown_error_ == 0) { installed_ = false; exited_ = true; }
            changed_.notify_all();
            if (exited_) return;
        }
        if (!active) changed_.wait(lock, [this] {
            return shutdown_requested_ || std::any_of(slots_.begin(), slots_.end(), [](const Slot& slot) {
                return slot.value.phase == Phase::active || slot.value.outstanding;
            });
        });
        else { lock.unlock(); std::this_thread::yield(); lock.lock(); }
    }
}

int Owner::shutdown() {
    std::unique_lock lock(mutex_);
    if (!installed_ || shutdown_requested_) return EINVAL;
    if (std::any_of(slots_.begin(), slots_.end(), [](const Slot& slot) { return slot.value.phase != Phase::idle; })) return EBUSY;
    shutdown_finished_ = false;
    shutdown_requested_ = true;
    changed_.notify_all();
    changed_.wait(lock, [this] { return shutdown_finished_; });
    const int result = shutdown_error_;
    lock.unlock();
    if (result == 0) thread_.join();
    return result;
}
}
