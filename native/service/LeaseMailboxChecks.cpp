#include "LeaseMailbox.h"
#include <cstdlib>
#include <thread>
using namespace coz::service;
static void require(bool value, int code) { if (!value) std::_Exit(code); }

static void cancellationAndIdentity() {
    LeaseMailbox mailbox, other;
    auto submission = mailbox.begin();
    require(submission && submission->generation() == 1, 1);
    require(!mailbox.begin() && !mailbox.read(1) && !mailbox.claim(1) && !mailbox.close(), 2);
    require(!other.publish(*submission) && !mailbox.cancel(0) && !mailbox.cancel(2), 3);
    require(mailbox.cancel(1) && mailbox.publish(*submission), 4);
    require(!mailbox.close(), 54);
    require(!mailbox.publish(*submission) && !mailbox.abandonConfiguration(*submission), 5);
    auto execution = mailbox.claim(1);
    require(execution && mailbox.cancelled(*execution) && !other.quiesce(*execution), 6);
    require(!mailbox.close(), 55);
    auto foreign_submission = other.begin();
    require(foreign_submission && other.publish(*foreign_submission), 56);
    auto foreign_execution = other.claim(1);
    require(foreign_execution && other.cancel(1) && other.cancelled(*foreign_execution), 57);
    require(!other.cancelled(*execution) && !mailbox.cancelled(*foreign_execution), 58);
    require(other.quiesce(*foreign_execution) && other.close(), 59);
    require(!mailbox.claim(1) && !mailbox.abandonUndelivered(*submission) && !mailbox.begin(), 7);
    require(mailbox.quiesce(*execution) && !mailbox.quiesce(*execution), 8);
    require(!mailbox.cancel(1), 9);
    auto next = mailbox.begin();
    require(next && next->generation() == 2 && mailbox.publish(*next), 10);
    require(!mailbox.publish(*submission) && !mailbox.claim(1) && !mailbox.read(1) && !mailbox.cancel(1), 11);
    auto second = mailbox.claim(2);
    require(second && !mailbox.cancelled(*second) && !mailbox.quiesce(*execution), 12);
    require(mailbox.cancel(2) && mailbox.cancelled(*second) && mailbox.quiesce(*second) && mailbox.close(), 13);
    require(!mailbox.begin() && !mailbox.read(2) && !mailbox.close(), 14);
}

static void readersExcludeReuse() {
    LeaseMailbox mailbox;
    auto submission = mailbox.begin();
    require(submission && mailbox.publish(*submission), 20);
    auto reader = mailbox.read(1);
    require(reader && reader->generation() == 1, 21);
    auto execution = mailbox.claim(1);
    auto second = mailbox.read(1);
    require(execution && second && mailbox.quiesce(*execution), 22);
    require(!mailbox.begin() && !mailbox.close(), 23);
    auto moved = std::move(*reader);
    reader.reset();
    second.reset();
    require(!mailbox.begin() && !mailbox.close() && moved.generation() == 1, 24);
}

static void readerReleaseAndFailures() {
    LeaseMailbox mailbox;
    auto submission = mailbox.begin();
    require(submission && mailbox.abandonConfiguration(*submission), 30);
    require(!mailbox.publish(*submission), 31);
    auto next = mailbox.begin();
    require(next && next->generation() == 2 && mailbox.publish(*next), 32);
    {
        auto reader = mailbox.read(2);
        require(reader && mailbox.abandonUndelivered(*next), 33);
        require(!mailbox.begin() && !mailbox.close(), 34);
    }
    auto third = mailbox.begin();
    require(third && third->generation() == 3, 35);
    require(!mailbox.abandonUndelivered(*next) && mailbox.abandonConfiguration(*third) && mailbox.close(), 36);
    LeaseMailbox exhausted(std::numeric_limits<uint32_t>::max());
    require(!exhausted.begin() && exhausted.close(), 37);
    LeaseMailbox last(std::numeric_limits<uint32_t>::max() - 1);
    auto final = last.begin();
    require(final && final->generation() == std::numeric_limits<uint32_t>::max(), 38);
    require(last.abandonConfiguration(*final) && !last.begin() && last.close(), 39);
}

static void concurrentReaderLifetime() {
    LeaseMailbox mailbox;
    uint64_t storage = 0;
    std::atomic<uint32_t> published{}, pinned{}, release{}, finished{};
    std::thread reader([&] {
        for (uint32_t generation = 1; generation <= 200; ++generation) {
            while (published.load(std::memory_order_acquire) != generation) std::this_thread::yield();
            {
                auto pin = mailbox.read(generation);
                require(pin.has_value() && storage == generation, 40);
                pinned.store(generation, std::memory_order_release);
                while (release.load(std::memory_order_acquire) != generation) std::this_thread::yield();
                require(storage == generation, 41);
            }
            finished.store(generation, std::memory_order_release);
        }
    });
    for (uint32_t generation = 1; generation <= 200; ++generation) {
        auto submission = mailbox.begin();
        require(submission && submission->generation() == generation, 42);
        storage = generation;
        require(mailbox.publish(*submission), 43);
        auto execution = mailbox.claim(generation);
        require(execution.has_value(), 44);
        published.store(generation, std::memory_order_release);
        while (pinned.load(std::memory_order_acquire) != generation) std::this_thread::yield();
        require(mailbox.quiesce(*execution) && !mailbox.begin() && !mailbox.close(), 45);
        release.store(generation, std::memory_order_release);
        while (finished.load(std::memory_order_acquire) != generation) std::this_thread::yield();
    }
    reader.join();
    require(mailbox.close(), 46);
}

static void racingReadAndReuse() {
    for (unsigned iteration = 0; iteration < 100; ++iteration) {
        LeaseMailbox mailbox;
        auto submission = mailbox.begin();
        require(submission && mailbox.abandonConfiguration(*submission), 50);
        std::atomic<bool> start{}, read_done{}, reuse_done{};
        bool read_acquired = false;
        std::thread reader([&] {
            while (!start.load(std::memory_order_acquire)) std::this_thread::yield();
            auto pin = mailbox.read(1);
            read_acquired = pin.has_value();
            read_done.store(true, std::memory_order_release);
            while (!reuse_done.load(std::memory_order_acquire)) std::this_thread::yield();
        });
        start.store(true, std::memory_order_release);
        auto next = mailbox.begin();
        while (!read_done.load(std::memory_order_acquire)) std::this_thread::yield();
        require(next.has_value() != read_acquired, 51);
        reuse_done.store(true, std::memory_order_release);
        reader.join();
        if (next) require(mailbox.abandonConfiguration(*next), 52);
        require(mailbox.close(), 53);
    }
}

int main() {
    cancellationAndIdentity();
    readersExcludeReuse();
    readerReleaseAndFailures();
    concurrentReaderLifetime();
    racingReadAndReuse();
    return 0;
}
