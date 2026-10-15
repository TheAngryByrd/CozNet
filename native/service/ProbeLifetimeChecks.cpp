#include "../ProbeLifetime.h"
#include <cstdlib>
#include <array>
#include <thread>

using coz::ProbeLifetime;
using coz::ProbeLifetimeState;
static void require(bool condition, int code) { if (!condition) std::_Exit(code); }

int main() {
    coz::ProbeBinding binding{};
    ProbeLifetime life;
    require(!life.read(binding) && !life.current(1, 1), 1);
    require(life.begin(10, 20, 30) && !life.read(binding), 2);
    life.moduleUnloading(11);
    require(life.rewritten(2) && !life.current(1, 1), 3);
    require(!life.finish(21, true) && life.finish(20, true), 4);
    require(life.read(binding) && binding.generation == 1 && binding.module == 10 && binding.function == 20
        && binding.method == 30 && binding.sites == 2, 5);
    require(life.current(1, 1) && life.current(1, 2) && !life.current(0, 1) && !life.current(2, 1)
        && !life.current(1, 0) && !life.current(1, 3), 6);
    require(!life.rewritten(3) && !life.current(1, 3) && life.current(1, 2), 18);
    life.moduleUnloading(11);
    require(life.current(1, 1), 7);
    life.moduleUnloading(10);
    require(!life.read(binding) && binding.generation == 0 && !life.current(1, 1), 8);
    require(!life.begin(10, 20, 30) && !life.rewritten(2) && !life.finish(20, true)
        && life.state() == ProbeLifetimeState::moduleUnloaded, 9);
    life.invalidate(ProbeLifetimeState::shutdown);
    require(life.state() == ProbeLifetimeState::moduleUnloaded, 19);
    for (int phase : std::array{0, 1, 2}) {
        ProbeLifetime duplicate;
        require(duplicate.begin(10, 20, 30), 10);
        if (phase >= 1) require(duplicate.rewritten(2), 20);
        if (phase == 2) require(duplicate.finish(20, true), 11);
        require(!duplicate.begin(10, 20, 30) && !duplicate.finish(20, true)
            && duplicate.state() == ProbeLifetimeState::duplicateCompilation, 12);
    }
    ProbeLifetime failed;
    require(failed.begin(10, 20, 30) && failed.rewritten(2) && !failed.finish(20, false)
        && !failed.finish(20, true) && failed.state() == ProbeLifetimeState::compilationFailed, 13);
    for (int field : std::array{0, 1, 2}) {
        ProbeLifetime invalid;
        require(!invalid.begin(field == 0 ? 0 : 10, field == 1 ? 0 : 20, field == 2 ? 0 : 30)
            && !invalid.begin(10, 20, 30) && invalid.state() == ProbeLifetimeState::invalidIdentity, 14);
    }
    ProbeLifetime noSites;
    require(noSites.begin(10, 20, 30) && !noSites.rewritten(0) && !noSites.finish(20, true)
        && noSites.state() == ProbeLifetimeState::invalidIdentity, 21);
    ProbeLifetime early;
    require(!early.rewritten(2) && early.begin(10, 20, 30) && !early.finish(20, true), 22);
    early.moduleUnloading(10);
    require(!early.rewritten(2) && !early.finish(20, true) && early.state() == ProbeLifetimeState::moduleUnloaded, 23);
    for (int iteration = 0; iteration < 100; ++iteration) {
        ProbeLifetime raced;
        require(raced.begin(10, 20, 30) && raced.rewritten(2), 15);
        std::atomic<bool> start{};
        std::thread publisher([&] { while (!start.load(std::memory_order_acquire)) std::this_thread::yield(); raced.finish(20, true); });
        start.store(true, std::memory_order_release);
        raced.moduleUnloading(10);
        publisher.join();
        require(raced.state() == ProbeLifetimeState::moduleUnloaded && !raced.current(1, 1), 16);
    }
    ProbeLifetime stopped;
    stopped.invalidate(ProbeLifetimeState::shutdown);
    require(!stopped.begin(10, 20, 30) && stopped.state() == ProbeLifetimeState::shutdown, 17);
}
