#include "Signal.h"
#include <atomic>
#include <cerrno>
#include <chrono>
#include <csignal>
#include <cstdlib>
#include <cstring>
#include <thread>

static void require(bool condition, int code) { if (!condition) std::_Exit(code); }

static int pending_shutdown(const sigset_t& mask, bool exit_with_pending) {
    std::atomic<int> phase{0};
    std::thread target([&] {
        require(coz_signal_register(0, 1, 1) == 0, 40);
        require(pthread_sigmask(SIG_BLOCK, &mask, nullptr) == 0, 41);
        phase.store(1);
        while (phase.load() == 1) std::this_thread::yield();
        if (exit_with_pending) return;
        require(coz_signal_retire(0) == EBUSY, 42);
        phase.store(3);
        while (phase.load() == 3) std::this_thread::yield();
        require(pthread_sigmask(SIG_UNBLOCK, &mask, nullptr) == 0, 43);
        phase.store(5);
        while (phase.load() == 5) std::this_thread::yield();
        require(coz_signal_retire(0) == 0, 44);
    });
    while (phase.load() == 0) std::this_thread::yield();
    uint64_t sequence{};
    SignalResult result{};
    require(coz_signal_request(0, 50000, &sequence) == 0, 45);
    require(coz_signal_read(0, sequence, &result) == EAGAIN, 46);
    require(coz_signal_restore() == EBUSY, 47);
    phase.store(2);
    if (exit_with_pending) {
        target.join();
        require(coz_signal_read(0, sequence, &result) == EAGAIN, 48);
        require(coz_signal_restore() == EBUSY, 49);
        require(coz_signal_request(0, 0, &sequence) == EBUSY, 50);
        return 0;
    }
    while (phase.load() != 3) std::this_thread::yield();
    require(coz_signal_read(0, sequence, &result) == EAGAIN, 51);
    phase.store(4);
    while (phase.load() != 5) std::this_thread::yield();
    auto deadline = std::chrono::steady_clock::now() + std::chrono::seconds(1);
    int status;
    do {
        status = coz_signal_read(0, sequence, &result);
        if (status == EAGAIN) std::this_thread::yield();
    } while (status == EAGAIN && std::chrono::steady_clock::now() < deadline);
    require(status == 0 && result.sequence == sequence && result.outcome == 1 && result.wait_ns == 0, 52);
    phase.store(6);
    target.join();
    require(coz_signal_restore() == 0, 53);
    return 0;
}

int main(int argc, char** argv) {
    require(argc == 2 || argc == 3, 2);
    require(argc == 2 || std::strcmp(argv[2], "sleep") == 0 || std::strcmp(argv[2], "spin") == 0, 68);
    const int mode = argc == 3 && std::strcmp(argv[2], "spin") == 0 ? 1 : 0;
    sigset_t mask;
    sigemptyset(&mask);
    sigaddset(&mask, SIGRTMIN + 6);
    if (std::strcmp(argv[1], "wait-modes") == 0) {
        require(coz_signal_install_with_wait(-1) == EINVAL, 61);
        require(coz_signal_install_with_wait(2) == EINVAL, 62);
        require(coz_signal_install_with_wait(1) == 0, 63);
        require(coz_signal_install_with_wait(0) == EALREADY, 64);
        require(coz_signal_install() == EALREADY, 65);
        require(coz_signal_restore() == 0, 66);
        require(coz_signal_install_with_wait(1) == EALREADY, 67);
        return 0;
    }
    if (std::strcmp(argv[1], "occupied") == 0) {
        struct sigaction action{};
        action.sa_handler = SIG_IGN;
        sigemptyset(&action.sa_mask);
        require(sigaction(SIGRTMIN + 6, &action, nullptr) == 0, 3);
        require((argc == 2 ? coz_signal_install() : coz_signal_install_with_wait(mode)) == EBUSY, 4);
        struct sigaction current{};
        require(sigaction(SIGRTMIN + 6, nullptr, &current) == 0 && current.sa_handler == SIG_IGN, 5);
        return 0;
    }
    require((argc == 2 ? coz_signal_install() : coz_signal_install_with_wait(mode)) == 0, 6);
    if (std::strcmp(argv[1], "pending-shutdown") == 0) return pending_shutdown(mask, false);
    if (std::strcmp(argv[1], "exit-with-pending") == 0) return pending_shutdown(mask, true);
    if (std::strcmp(argv[1], "blocked") == 0) {
        require(pthread_sigmask(SIG_BLOCK, &mask, nullptr) == 0, 7);
        require(coz_signal_register(0, 1, 1) == EACCES, 8);
        require(coz_signal_restore() == 0, 9);
        return 0;
    }
    require(std::strcmp(argv[1], "lifetime") == 0, 10);
    require(coz_signal_register(-1, 1, 1) == EINVAL, 11);
    require(coz_signal_register(2, 1, 1) == EINVAL, 12);
    require(coz_signal_register(0, UINT64_MAX, 1) == EINVAL, 13);
    require(coz_signal_register(0, 1, 0) == EINVAL, 14);
    std::atomic<int> phase{0};
    std::thread target([&] {
        require(coz_signal_register(0, 1, 1) == 0, 15);
        require(pthread_sigmask(SIG_BLOCK, &mask, nullptr) == 0, 16);
        phase.store(1);
        while (phase.load() == 1) std::this_thread::yield();
        require(pthread_sigmask(SIG_UNBLOCK, &mask, nullptr) == 0, 17);
        while (phase.load() == 2) std::this_thread::yield();
        require(coz_signal_retire(0) == 0, 18);
    });
    while (phase.load() == 0) std::this_thread::yield();
    uint64_t sequence{}, duplicate{};
    require(coz_signal_request(0, 50001, &sequence) == EINVAL, 19);
    require(coz_signal_request(0, 50000, &sequence) == 0 && sequence != 0, 20);
    require(coz_signal_request(0, 0, &duplicate) == EBUSY, 21);
    SignalResult result{};
    require(coz_signal_read(0, sequence + 1, &result) == EINVAL, 22);
    require(coz_signal_read(0, sequence, &result) == EAGAIN, 23);
    SignalTiming timing{};
    require(coz_signal_read_timed(0, sequence, &result, nullptr) == EINVAL, 54);
    require(coz_signal_read_timed(0, sequence + 1, &result, &timing) == EINVAL, 55);
    require(coz_signal_read_timed(0, sequence, &result, &timing) == EAGAIN, 56);
    require(coz_signal_restore() == EBUSY, 24);
    phase.store(2);
    int status;
    auto deadline = std::chrono::steady_clock::now() + std::chrono::seconds(1);
    do {
        status = coz_signal_read_timed(0, sequence, &result, &timing);
        if (status == EAGAIN) std::this_thread::yield();
    } while (status == EAGAIN && std::chrono::steady_clock::now() < deadline);
    require(status == 0 && result.sequence == sequence && result.outcome == 1 && result.wait_ns == 0, 25);
    require(timing.request_started_ns > 0 && timing.queue_returned_ns >= timing.request_started_ns, 57);
    require(timing.handler_started_ns >= timing.queue_returned_ns && timing.handler_finished_ns >= timing.handler_started_ns, 58);
    require(timing.observed_ns >= timing.handler_finished_ns && timing.handler_finished_ns - timing.handler_started_ns == result.residence_ns, 59);
    require(coz_signal_read_timed(0, sequence, &result, &timing) == EINVAL, 60);
    require(coz_signal_read(0, sequence, &result) == EINVAL, 26);
    require(coz_signal_request(0, 0, &duplicate) == 0 && duplicate > sequence, 27);
    deadline = std::chrono::steady_clock::now() + std::chrono::seconds(1);
    do {
        status = coz_signal_read(0, duplicate, &result);
        if (status == EAGAIN) std::this_thread::yield();
    } while (status == EAGAIN && std::chrono::steady_clock::now() < deadline);
    require(status == 0 && result.sequence == duplicate && result.outcome == 1 && result.wait_ns == 0, 28);
    phase.store(3);
    target.join();
    require(coz_signal_request(0, 0, &sequence) == EBUSY, 29);
    require(coz_signal_restore() == 0, 30);
    require(coz_signal_install() == EALREADY, 31);
    return 0;
}
