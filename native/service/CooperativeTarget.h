#pragma once
#include "TargetLease.h"
#include "LeaseMailbox.h"
#include <memory>
#include <shared_mutex>
#include <thread>
#include <vector>

namespace coz::service {
class CooperativeCodeLifetime {
public:
    explicit CooperativeCodeLifetime(CooperativePollSite site) noexcept : version_(site.codeVersion()) {}
    bool tryRevoke();
private:
    friend class CooperativeTarget;
    const uint64_t version_;
    std::shared_mutex gate_;
    bool active_{true};
};

class CooperativeTiming {
public:
    virtual ~CooperativeTiming() = default;
    virtual uint64_t now() noexcept = 0;
    virtual TargetWaitStatus wait(uint64_t requested) noexcept = 0;
};

enum class CooperativePollResult { idle, serviced, rejected, failed };

class CooperativeTarget {
public:
    static std::unique_ptr<CooperativeTarget> registerCurrentThread(CooperativePollSite site,
        std::shared_ptr<CooperativeCodeLifetime> code, std::thread::id controller,
        std::size_t capacity, CooperativeTiming& timing);
    CooperativeTarget(const CooperativeTarget&) = delete;
    CooperativeTarget& operator=(const CooperativeTarget&) = delete;
    std::optional<uint32_t> submit(uint64_t command, uint64_t budget);
    CooperativePollResult poll(CooperativePollSite observed);
    bool cancel(uint32_t generation) noexcept;
    std::optional<TargetLeaseSnapshot> snapshot(uint32_t generation, std::span<TargetLeaseChild> destination,
        std::size_t& required) noexcept;
    bool release(uint32_t generation) noexcept;
    bool retire() noexcept;
    bool retired() const noexcept { return retired_.load(std::memory_order_acquire); }
private:
    CooperativeTarget(CooperativePollSite site, std::shared_ptr<CooperativeCodeLifetime> code,
        std::thread::id controller, std::size_t capacity, CooperativeTiming& timing);
    bool finish() noexcept;
    bool controller() const noexcept { return std::this_thread::get_id() == controller_; }
    const CooperativePollSite site_;
    const std::shared_ptr<CooperativeCodeLifetime> code_;
    const std::thread::id controller_, owner_;
    CooperativeTiming& timing_;
    LeaseMailbox mailbox_;
    std::vector<TargetLeaseChild> journal_;
    std::optional<TargetLease> lease_;
    std::optional<LeaseMailbox::Execution> execution_;
    std::atomic<uint32_t> announced_{}, outstanding_{};
    std::atomic<bool> retired_{};
    bool observed_retired_{};
    bool polling_{};
};
}
