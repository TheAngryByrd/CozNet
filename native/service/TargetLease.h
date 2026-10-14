#pragma once
#include <cstddef>
#include <atomic>
#include <cstdint>
#include <optional>
#include <span>

namespace coz::service {
class TargetLeasePlan {
public:
    static std::optional<TargetLeasePlan> parse(uint64_t generation, uint64_t command,
        uint64_t budget, uint64_t begin, uint64_t size) noexcept;
    uint64_t generation() const noexcept { return generation_; }
    uint64_t command() const noexcept { return command_; }
    uint64_t budget() const noexcept { return budget_; }
    bool eligible(uint64_t ip) const noexcept { return ip >= begin_ && ip - begin_ < size_; }
private:
    TargetLeasePlan(uint64_t generation, uint64_t command, uint64_t budget, uint64_t begin, uint64_t size) noexcept
        : generation_(generation), command_(command), budget_(budget), begin_(begin), size_(size) {}
    uint64_t generation_, command_, budget_, begin_, size_;
};

class TimerArmResult {
public:
    static TimerArmResult accepted(uint64_t timestamp) noexcept { return {true, timestamp, 0}; }
    static TimerArmResult rejected(int error) noexcept { return {false, 0, error}; }
    static TimerArmResult notRequested() noexcept { return {false, 0, 0}; }
    bool pending() const noexcept { return pending_; }
    uint64_t timestamp() const noexcept { return timestamp_; }
    int error() const noexcept { return error_; }
private:
    TimerArmResult(bool pending, uint64_t timestamp, int error) noexcept
        : pending_(pending), timestamp_(timestamp), error_(error) {}
    bool pending_;
    uint64_t timestamp_;
    int error_;
};

enum class TargetLeasePhase { unarmed, waiting, executing, prepared, terminal, retired };
enum class TargetLeaseStop { none, completed, deferred, cancelled, interrupted, failed };
enum class TargetLeaseError { none, arm_failed, clock, invalid_wait, handler_bound, journal_full, unexpected_arm };
enum class TargetWaitStatus { completed, interrupted, failed };
struct TargetLeaseChild {
    uint64_t ordinal{}, requested{}, armed{}, started{}, finished{}, waited{}, ip{};
    TargetWaitStatus wait_status{TargetWaitStatus::completed};
    bool valid{};
    bool operator==(const TargetLeaseChild&) const = default;
};
enum class TargetLeaseVisibility : uint64_t { active, terminal, quiesced, retired };
struct TargetLeaseSnapshot {
    uint64_t generation{}, command{}, budget{}, children{}, measured{};
    TargetLeaseVisibility visibility{TargetLeaseVisibility::active};
    TargetLeaseStop stop{TargetLeaseStop::none};
    TargetLeaseError error{TargetLeaseError::none};
    int transport_error{};
    bool may_have_delivery{true};
};

class TargetLease {
public:
    class Admission {
    public:
        bool shouldWait() const noexcept { return wait_; }
        uint64_t requested() const noexcept { return requested_; }
    private:
        friend class TargetLease;
        Admission(const TargetLease* owner, const TargetLeasePlan& plan, uint64_t ordinal, uint64_t requested, bool wait) noexcept
            : owner_(owner), generation_(plan.generation()), command_(plan.command()), ordinal_(ordinal), requested_(requested), wait_(wait) {}
        const TargetLease* owner_;
        uint64_t generation_, command_, ordinal_, requested_;
        bool wait_;
    };
    class Completion {
    public:
        bool shouldRearm() const noexcept { return rearm_; }
    private:
        friend class TargetLease;
        Completion(const TargetLease* owner, const TargetLeasePlan& plan, uint64_t ordinal, bool rearm) noexcept
            : owner_(owner), generation_(plan.generation()), command_(plan.command()), ordinal_(ordinal), rearm_(rearm) {}
        const TargetLease* owner_;
        uint64_t generation_, command_, ordinal_;
        bool rearm_;
    };

    TargetLease(TargetLeasePlan plan, std::span<TargetLeaseChild> journal) noexcept;
    TargetLease(const TargetLease&) = delete;
    TargetLease& operator=(const TargetLease&) = delete;
    bool start(TimerArmResult result) noexcept;
    bool rejectInitialArm(int error) noexcept;
    std::optional<Admission> arrive(uint64_t generation, uint64_t ip, uint64_t timestamp, bool cancelled) noexcept;
    std::optional<Completion> prepare(const Admission& admission, uint64_t waited, TargetWaitStatus status, bool cancelled) noexcept;
    bool publish(const Completion& completion, uint64_t finished, TimerArmResult next, bool cancelled) noexcept;
    bool cancelPending() noexcept;
    bool quiesced() noexcept;
    bool retire() noexcept;
    std::optional<TargetLeaseSnapshot> snapshot(std::span<TargetLeaseChild> destination, std::size_t& required) const noexcept;
    TargetLeasePhase phase() const noexcept { return phase_; }
    TargetLeaseStop stop() const noexcept { return stop_; }
    TargetLeaseError error() const noexcept { return error_; }
    int transportError() const noexcept { return transport_error_; }
    bool deliveryPending() const noexcept { return pending_; }
    uint64_t measured() const noexcept { return measured_; }
    std::span<const TargetLeaseChild> journal() const noexcept { return journal_.first(count_); }
private:
    TargetLeasePlan plan_;
    std::span<TargetLeaseChild> journal_;
    std::size_t count_{};
    uint64_t measured_{}, armed_{};
    TargetLeasePhase phase_{TargetLeasePhase::unarmed};
    TargetLeaseStop stop_{TargetLeaseStop::none};
    TargetLeaseError error_{TargetLeaseError::none};
    int transport_error_{};
    bool pending_{}, quiesced_{};
    std::atomic<uint64_t> published_count_{};
    std::atomic<TargetLeaseVisibility> visibility_{TargetLeaseVisibility::active};
    TargetLeaseSnapshot terminal_snapshot_{};
    void fail(TargetLeaseError error, int transport = 0) noexcept;
    void publishVisibility() noexcept;
};
}
