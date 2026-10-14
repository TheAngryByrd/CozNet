#include "Signal.h"
#include <atomic>
#include <cerrno>
#include <chrono>
#include <csignal>
#include <cstdlib>
#include <cstring>
#include <thread>

static void require(bool condition, int code) { if (!condition) std::_Exit(code); }

int main(int argc, char** argv) {
    require(argc == 2, 2);
    sigset_t mask;
    sigemptyset(&mask);
    sigaddset(&mask, SIGRTMIN + 6);
    if (std::strcmp(argv[1], "occupied") == 0) {
        struct sigaction action{};
        action.sa_handler = SIG_IGN;
        sigemptyset(&action.sa_mask);
        require(sigaction(SIGRTMIN + 6, &action, nullptr) == 0, 3);
        require(coz_signal_install() == EBUSY, 4);
        struct sigaction current{};
        require(sigaction(SIGRTMIN + 6, nullptr, &current) == 0 && current.sa_handler == SIG_IGN, 5);
        return 0;
    }
    require(coz_signal_install() == 0, 6);
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
    require(coz_signal_restore() == EBUSY, 24);
    phase.store(2);
    int status;
    auto deadline = std::chrono::steady_clock::now() + std::chrono::seconds(1);
    do {
        status = coz_signal_read(0, sequence, &result);
        if (status == EAGAIN) std::this_thread::yield();
    } while (status == EAGAIN && std::chrono::steady_clock::now() < deadline);
    require(status == 0 && result.sequence == sequence && result.outcome == 1 && result.wait_ns == 0, 25);
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
