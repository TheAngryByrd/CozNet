#include "SignalTransport.Linux.h"
#include "../signal/Signal.h"
#include <cerrno>
#include <ctime>
#include <limits>
#include <stdexcept>

namespace coz::service {
InstructionRange::InstructionRange(uint64_t begin, uint64_t size) : begin_(begin), size_(size) {
    if (begin == 0 || size == 0 || size > std::numeric_limits<uint64_t>::max() - begin)
        throw std::invalid_argument("Invalid signal instruction range.");
}

static int nativeMode(SignalWaitMode mode) {
    switch (mode) {
    case SignalWaitMode::sleep: return 0;
    case SignalWaitMode::spin: return 1;
    }
    throw std::invalid_argument("Invalid signal wait mode.");
}

LinuxSignalTransport::LinuxSignalTransport(InstructionRange range, SignalWaitMode mode)
    : range_(range), mode_(nativeMode(mode)) {}

int LinuxSignalTransport::install() noexcept { return coz_signal_install_with_wait(mode_); }

Send LinuxSignalTransport::request(int slot, uint64_t requested) noexcept {
    uint64_t sequence{};
    const int error = coz_signal_request(slot, requested, &sequence);
    return {error == 0 ? SendState::sent : SendState::rejected, sequence, error};
}

Read LinuxSignalTransport::read(int slot, uint64_t sequence) noexcept {
    SignalResult result{};
    SignalTiming timing{};
    const int error = coz_signal_read_timed(slot, sequence, &result, &timing);
    if (error != 0) return {error == EAGAIN ? ReadState::pending : ReadState::failed, {}, error};
    return {ReadState::completed,
        {result.sequence, result.outcome, result.ip, result.residence_ns, result.wait_ns, result.wait_result,
         timing.request_started_ns, timing.queue_returned_ns, timing.handler_started_ns, timing.handler_finished_ns, timing.observed_ns},
        0};
}

bool LinuxSignalTransport::eligible(uint64_t ip) const noexcept { return range_.contains(ip); }

uint64_t LinuxSignalTransport::now() const noexcept {
    timespec time{};
    if (clock_gettime(CLOCK_MONOTONIC, &time) != 0 || time.tv_sec < 0 || time.tv_nsec < 0 || time.tv_nsec >= 1000000000)
        return 0;
    const auto seconds = static_cast<uint64_t>(time.tv_sec);
    const auto nanos = static_cast<uint64_t>(time.tv_nsec);
    if (seconds > (std::numeric_limits<uint64_t>::max() - nanos) / 1000000000ULL) return 0;
    return seconds * 1000000000ULL + nanos;
}

int LinuxSignalTransport::restore() noexcept { return coz_signal_restore(); }
int LinuxSignalTransport::registerTarget(int slot) noexcept { return coz_signal_register(slot, range_.begin(), range_.size()); }
int LinuxSignalTransport::retireTarget(int slot) noexcept { return coz_signal_retire(slot); }
}
