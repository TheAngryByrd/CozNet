#include "../ProbeLifetime.h"
#include <cstdlib>
#include <array>
#include <thread>

using coz::ProbeLifetime;
using coz::ProbeLifetimeState;
static void require(bool condition, int code) { if (!condition) std::_Exit(code); }

static void competingCodePublishers() {
    for (int iteration = 0; iteration < 1024; ++iteration) {
        ProbeLifetime life;
        require(life.begin(10, 20, 30) && life.rewritten(2), 42);
        std::atomic<int> arrived{};
        std::array<bool, 2> published{};
        auto publish = [&](int index) {
            arrived.fetch_add(1, std::memory_order_acq_rel);
            while (arrived.load(std::memory_order_acquire) != 2) std::this_thread::yield();
            published[index] = life.finish(20, true, 1, 1000 + index * 1000, 100 + index * 100);
        };
        std::thread first(publish, 0);
        std::thread second(publish, 1);
        first.join();
        second.join();
        require(published[0] != published[1], 43);
        const uintptr_t winner = published[0] ? 0 : 1;
        uintptr_t start = 0, size = 0;
        require(life.readCode({1, 10, 20, 30, 2}, start, size)
            && start == 1000 + winner * 1000 && size == 100 + winner * 100, 44);
        require(!life.finish(20, true, 1, 3000, 300)
            && life.readCode({1, 10, 20, 30, 2}, start, size)
            && start == 1000 + winner * 1000 && size == 100 + winner * 100, 45);
    }
}

static void codeRangePublication() {
    for (uint32_t regions : std::array<uint32_t, 3>{0, 2, UINT32_MAX}) {
        ProbeLifetime invalid;
        require(invalid.begin(10, 20, 30) && invalid.rewritten(2), 40);
        require(!invalid.finish(20, true, regions, 1000, 100) && invalid.state() == ProbeLifetimeState::compilationFailed, 41);
    }
    ProbeLifetime life;
    coz::ProbeBinding binding{1, 10, 20, 30, 2};
    uintptr_t start = 1, size = 1;
    require(!life.readCode(binding, start, size) && start == 0 && size == 0, 30);
    require(life.begin(10, 20, 30) && life.rewritten(2) && life.awaitingCode(20) && !life.awaitingCode(21), 31);
    require(!life.readCode(binding, start, size) && life.finish(20, true, 1, 1000, 100), 32);
    require(life.readCode(binding, start, size) && start == 1000 && size == 100, 33);
    require(!life.finish(20, true, 1, 2000, 200) && !life.awaitingCode(20), 34);
    require(life.readCode(binding, start, size) && start == 1000 && size == 100, 35);
    for (int field = 0; field < 5; ++field) {
        auto foreign = binding;
        switch (field) {
            case 0: ++foreign.generation; break;
            case 1: ++foreign.module; break;
            case 2: ++foreign.function; break;
            case 3: ++foreign.method; break;
            case 4: ++foreign.sites; break;
        }
        start = size = 1;
        require(!life.readCode(foreign, start, size) && start == 0 && size == 0, 36);
    }
    life.moduleUnloading(10);
    require(!life.readCode(binding, start, size) && start == 0 && size == 0, 37);
    for (auto range : std::array<std::array<uintptr_t, 2>, 4>{{{0, 1}, {1000, 0}, {UINTPTR_MAX, 1}, {UINTPTR_MAX - 1, 2}}}) {
        ProbeLifetime invalid;
        require(invalid.begin(10, 20, 30) && invalid.rewritten(2), 38);
        require(!invalid.finish(20, true, 1, range[0], range[1]) && invalid.state() == ProbeLifetimeState::compilationFailed
            && !invalid.readCode(binding, start, size), 39);
    }
}

int main() {
    competingCodePublishers();
    codeRangePublication();
    coz::ProbeBinding binding{};
    ProbeLifetime life;
    require(!life.read(binding) && !life.current(1, 1), 1);
    require(life.begin(10, 20, 30) && !life.read(binding), 2);
    life.moduleUnloading(11);
    require(life.rewritten(2) && !life.current(1, 1), 3);
    require(!life.finish(21, true, 1, 1000, 100) && life.finish(20, true, 1, 1000, 100), 4);
    require(life.read(binding) && binding.generation == 1 && binding.module == 10 && binding.function == 20
        && binding.method == 30 && binding.sites == 2, 5);
    require(life.current(1, 1) && life.current(1, 2) && !life.current(0, 1) && !life.current(2, 1)
        && !life.current(1, 0) && !life.current(1, 3), 6);
    require(!life.rewritten(3) && !life.current(1, 3) && life.current(1, 2), 18);
    life.moduleUnloading(11);
    require(life.current(1, 1), 7);
    life.moduleUnloading(10);
    require(!life.read(binding) && binding.generation == 0 && !life.current(1, 1), 8);
    require(!life.begin(10, 20, 30) && !life.rewritten(2) && !life.finish(20, true, 1, 1000, 100)
        && life.state() == ProbeLifetimeState::moduleUnloaded, 9);
    life.invalidate(ProbeLifetimeState::shutdown);
    require(life.state() == ProbeLifetimeState::moduleUnloaded, 19);
    for (int phase : std::array{0, 1, 2}) {
        ProbeLifetime duplicate;
        require(duplicate.begin(10, 20, 30), 10);
        if (phase >= 1) require(duplicate.rewritten(2), 20);
        if (phase == 2) require(duplicate.finish(20, true, 1, 1000, 100), 11);
        require(!duplicate.begin(10, 20, 30) && !duplicate.finish(20, true, 1, 1000, 100)
            && duplicate.state() == ProbeLifetimeState::duplicateCompilation, 12);
    }
    ProbeLifetime failed;
    require(failed.begin(10, 20, 30) && failed.rewritten(2) && !failed.finish(20, false, 1, 1000, 100)
        && !failed.finish(20, true, 1, 1000, 100) && failed.state() == ProbeLifetimeState::compilationFailed, 13);
    for (int field : std::array{0, 1, 2}) {
        ProbeLifetime invalid;
        require(!invalid.begin(field == 0 ? 0 : 10, field == 1 ? 0 : 20, field == 2 ? 0 : 30)
            && !invalid.begin(10, 20, 30) && invalid.state() == ProbeLifetimeState::invalidIdentity, 14);
    }
    ProbeLifetime noSites;
    require(noSites.begin(10, 20, 30) && !noSites.rewritten(0) && !noSites.finish(20, true, 1, 1000, 100)
        && noSites.state() == ProbeLifetimeState::invalidIdentity, 21);
    ProbeLifetime early;
    require(!early.rewritten(2) && early.begin(10, 20, 30) && !early.finish(20, true, 1, 1000, 100), 22);
    early.moduleUnloading(10);
    require(!early.rewritten(2) && !early.finish(20, true, 1, 1000, 100) && early.state() == ProbeLifetimeState::moduleUnloaded, 23);
    for (int iteration = 0; iteration < 100; ++iteration) {
        ProbeLifetime raced;
        require(raced.begin(10, 20, 30) && raced.rewritten(2), 15);
        std::atomic<bool> start{};
        std::thread publisher([&] { while (!start.load(std::memory_order_acquire)) std::this_thread::yield(); raced.finish(20, true, 1, 1000, 100); });
        start.store(true, std::memory_order_release);
        raced.moduleUnloading(10);
        publisher.join();
        require(raced.state() == ProbeLifetimeState::moduleUnloaded && !raced.current(1, 1), 16);
    }
    ProbeLifetime stopped;
    stopped.invalidate(ProbeLifetimeState::shutdown);
    require(!stopped.begin(10, 20, 30) && stopped.state() == ProbeLifetimeState::shutdown, 17);
}
