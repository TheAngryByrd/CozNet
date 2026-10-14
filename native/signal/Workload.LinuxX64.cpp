#include <atomic>
#include <cerrno>
#include <cstdint>
#include <ctime>
#include <linux/futex.h>
#include <sys/syscall.h>
#include <unistd.h>

#define EXPORT extern "C" __attribute__((visibility("default")))

namespace {
std::atomic<uint32_t> released{0};
std::atomic<int> wait_state{0};
static_assert(sizeof(released) == sizeof(uint32_t));
static_assert(std::atomic<uint32_t>::is_always_lock_free);
}

EXPORT int coz_fixture_wait_state() { return wait_state.load(std::memory_order_acquire); }

EXPORT int coz_fixture_wait() {
    int expected = 0;
    if (!wait_state.compare_exchange_strong(expected, 1)) return EALREADY;
    timespec deadline{};
    if (clock_gettime(CLOCK_MONOTONIC, &deadline) != 0) {
        wait_state.store(3, std::memory_order_release);
        return errno;
    }
    deadline.tv_sec += 5;
    while (released.load(std::memory_order_acquire) == 0) {
        if (syscall(SYS_futex, &released, FUTEX_WAIT_BITSET_PRIVATE, 0, &deadline, nullptr, FUTEX_BITSET_MATCH_ANY) != 0 && errno != EAGAIN && errno != EINTR) {
            const auto error = errno;
            wait_state.store(3, std::memory_order_release);
            return error;
        }
    }
    wait_state.store(2, std::memory_order_release);
    return 0;
}

EXPORT int coz_fixture_release() {
    released.store(1, std::memory_order_release);
    return syscall(SYS_futex, &released, FUTEX_WAKE_PRIVATE, 1, nullptr, nullptr, 0) < 0 ? errno : 0;
}

EXPORT int64_t coz_fixture_hop() {
    int64_t result;
    asm volatile("syscall" : "=a"(result) : "a"(SYS_gettid) : "rcx", "r11", "memory");
    return result;
}
