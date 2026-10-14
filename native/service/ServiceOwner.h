#pragma once
#include "OwnerPollSchedule.h"
#include <array>
#include <condition_variable>
#include <cstddef>
#include <cstdint>
#include <mutex>
#include <thread>
#include <vector>

namespace coz::service {
struct Identity {
    uint64_t first{}, second{};
    bool operator==(const Identity&) const = default;
};
struct Acknowledgment {
    uint64_t sequence{}, outcome{}, ip{}, handler_ns{}, wait_ns{};
    int64_t wait_result{};
    uint64_t started{}, queued{}, handler_started{}, handler_finished{}, observed{};
};
enum class SendState { sent, rejected, ambiguous };
enum class ReadState { pending, completed, failed };
struct Send { SendState state; uint64_t sequence; int error; };
struct Read { ReadState state; Acknowledgment value; int error; };
class Transport {
public:
    virtual ~Transport() = default;
    virtual int install() noexcept = 0;
    virtual Send request(int slot, uint64_t requested) noexcept = 0;
    virtual Read read(int slot, uint64_t sequence) noexcept = 0;
    virtual bool eligible(uint64_t ip) const noexcept = 0;
    virtual uint64_t now() const noexcept = 0;
    virtual int restore() noexcept = 0;
};
enum class Phase { idle, active, completed, cancelled, failed };
enum class Error { none, send_rejected, ambiguous_delivery, invalid_sequence, read_failed, invalid_acknowledgment,
                   clock_failed, acknowledgment_timeout, journal_full };
struct Child {
    uint64_t ordinal{}, requested{};
    Acknowledgment acknowledgment{};
};
struct Snapshot {
    Identity identity{};
    uint64_t command{}, budget{}, measured{};
    Phase phase{Phase::idle};
    Error error{Error::none};
    int transport_error{};
    bool cancellation{}, outstanding{};
    std::vector<Child> children;
    std::vector<Acknowledgment> rejected;
};

struct SubmissionTiming { uint64_t entered{}, locked{}, finished{}; };

class Owner {
public:
    explicit Owner(Transport& transport, std::size_t capacity = 100000,
        OwnerPollSchedule polling = OwnerPollSchedule::yielding());
    Owner(const Owner&) = delete;
    Owner& operator=(const Owner&) = delete;
    int start();
    bool submit(int slot, Identity identity, uint64_t command, uint64_t budget, SubmissionTiming* timing = nullptr);
    bool cancel(int slot, Identity identity);
    bool snapshot(int slot, Identity identity, Snapshot& result);
    bool release(int slot, Identity identity);
    int shutdown();
private:
    struct Slot {
        Snapshot value;
        uint64_t last_command{}, last_sequence{}, sequence{}, pending_since{};
        std::size_t observed_children{};
        bool observed_terminal{};
    };
    Transport& transport_;
    const std::size_t capacity_;
    OwnerPollSchedule polling_;
    std::array<Slot, 2> slots_;
    std::mutex mutex_;
    std::condition_variable changed_;
    std::thread thread_;
    bool used_{}, ready_{}, installed_{}, failed_{}, shutdown_requested_{}, shutdown_finished_{}, exited_{};
    int startup_error_{}, shutdown_error_{};
    void run();
    void advance(int index, Slot& slot);
    void fail(Slot& slot, Error error, int detail = 0);
    bool matches(int index, Identity identity) const;
};
}
