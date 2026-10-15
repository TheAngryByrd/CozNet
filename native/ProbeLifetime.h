#pragma once
#include <atomic>
#include <cstdint>

namespace coz {
enum class ProbeLifetimeState : uint32_t {
    empty, compiling, rewritten, ready, invalidIdentity, duplicateCompilation,
    compilationFailed, moduleUnloaded, shutdown
};

struct ProbeBinding {
    uint64_t generation;
    uintptr_t module;
    uintptr_t function;
    uint32_t method;
    uint32_t sites;
};

class ProbeLifetime final {
    static_assert(std::atomic<ProbeLifetimeState>::is_always_lock_free);
    static_assert(std::atomic<uintptr_t>::is_always_lock_free);
    static_assert(std::atomic<uint32_t>::is_always_lock_free);
    std::atomic<ProbeLifetimeState> state_{ProbeLifetimeState::empty};
    std::atomic<uintptr_t> module_{}, function_{};
    std::atomic<uint32_t> method_{}, sites_{};
public:
    static constexpr uint64_t generation = 1;
    ProbeLifetimeState state() const noexcept { return state_.load(std::memory_order_acquire); }
    void invalidate(ProbeLifetimeState reason) noexcept {
        if (reason < ProbeLifetimeState::invalidIdentity) return;
        auto observed = state();
        while (observed < ProbeLifetimeState::invalidIdentity &&
            !state_.compare_exchange_weak(observed, reason, std::memory_order_acq_rel)) {}
    }
    bool begin(uintptr_t module, uintptr_t function, uint32_t method) noexcept {
        if (!module || !function || !method) {
            invalidate(ProbeLifetimeState::invalidIdentity);
            return false;
        }
        auto expected = ProbeLifetimeState::empty;
        if (!state_.compare_exchange_strong(expected, ProbeLifetimeState::compiling, std::memory_order_acq_rel)) {
            invalidate(ProbeLifetimeState::duplicateCompilation);
            return false;
        }
        module_.store(module, std::memory_order_release);
        function_.store(function, std::memory_order_release);
        method_.store(method, std::memory_order_release);
        return state() == ProbeLifetimeState::compiling;
    }
    bool rewritten(uint32_t sites) noexcept {
        if (!sites) { invalidate(ProbeLifetimeState::invalidIdentity); return false; }
        if (state() != ProbeLifetimeState::compiling) return false;
        uint32_t noSites = 0;
        if (!sites_.compare_exchange_strong(noSites, sites, std::memory_order_acq_rel)) return false;
        auto expected = ProbeLifetimeState::compiling;
        return state_.compare_exchange_strong(expected, ProbeLifetimeState::rewritten, std::memory_order_acq_rel);
    }
    bool finish(uintptr_t function, bool success) noexcept {
        if (!function || function_.load(std::memory_order_acquire) != function) return false;
        if (!success) { invalidate(ProbeLifetimeState::compilationFailed); return false; }
        auto expected = ProbeLifetimeState::rewritten;
        return state_.compare_exchange_strong(expected, ProbeLifetimeState::ready, std::memory_order_acq_rel);
    }
    void moduleUnloading(uintptr_t module) noexcept {
        const auto observed = state();
        const auto selected = module_.load(std::memory_order_acquire);
        if (module && (selected == module || (observed == ProbeLifetimeState::compiling && !selected)))
            invalidate(ProbeLifetimeState::moduleUnloaded);
    }
    bool read(ProbeBinding& binding) const noexcept {
        binding = {};
        if (state() != ProbeLifetimeState::ready) return false;
        const ProbeBinding candidate{generation, module_.load(std::memory_order_acquire),
            function_.load(std::memory_order_acquire), method_.load(std::memory_order_acquire), sites_.load(std::memory_order_acquire)};
        if (state() != ProbeLifetimeState::ready) return false;
        binding = candidate;
        return true;
    }
    bool current(uint64_t observedGeneration, uint32_t site) const noexcept {
        return observedGeneration == generation && site != 0 && site <= sites_.load(std::memory_order_acquire)
            && state() == ProbeLifetimeState::ready;
    }
};
}
