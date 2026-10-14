#include "TimerApi.h"
#include <array>
#include <atomic>
#include <cerrno>
#include <chrono>
#include <csignal>
#include <cstddef>
#include <cstdlib>
#include <fstream>
#include <string_view>
#include <thread>

extern "C" void coz_timer_workload(std::atomic<uint32_t>*, std::atomic<uint32_t>*, std::atomic<uint64_t>*);
extern "C" char coz_timer_workload_begin, coz_timer_workload_end;
static_assert(sizeof(CozTimerChild) == 72 && offsetof(CozTimerChild, valid) == 64);
static_assert(sizeof(CozTimerSnapshot) == 96 && offsetof(CozTimerSnapshot, backend_error) == 88);
namespace {
int failure{};
void require(bool value, int code) {
    if (!value) { failure = code; throw code; }
}
template<class Predicate> void await(Predicate predicate) {
    const auto deadline = std::chrono::steady_clock::now() + std::chrono::seconds(2);
    while (!predicate()) {
        require(std::chrono::steady_clock::now() < deadline, 90);
        std::this_thread::yield();
    }
}
}
int main(int argc, char** argv) {
    if (argc != 3 || (std::string_view(argv[1]) != "chain" && std::string_view(argv[1]) != "deferred")) return 2;
    const bool chain = std::string_view(argv[1]) == "chain";
    CozTimerSession* session{};
    std::array<std::atomic<uint32_t>, 2> stop{}, entered{}, ready{};
    std::array<std::atomic<uint64_t>, 2> progress{};
    std::array<std::atomic<int>, 2> target_errors{};
    std::array<std::thread, 2> targets;
    std::array<CozTimerSnapshot, 4> headers{};
    std::array<std::array<CozTimerChild, 64>, 4> children{};
    std::array<uint64_t, 4> receipts{};
    std::atomic<bool> prepare_retirement{}, retirement_blocked{}, permit_retirement{};
    uint32_t completed{};
    bool closed{};
    try {
        uint64_t clock = 9;
        require(coz_timer_clock(nullptr, &clock) == EINVAL && clock == 0, 1);
        require(coz_timer_open(0, 1, 2, 64, &session) == EINVAL && !session, 2);
        require(coz_timer_open(1, 1, 0, 64, &session) == EINVAL && !session, 3);
        require(coz_timer_open(1, 1, 65, 64, &session) == EINVAL && !session, 4);
        require(coz_timer_open(1, 1, 1, 0, &session) == EINVAL && !session, 5);
        const auto begin = chain ? reinterpret_cast<uint64_t>(&coz_timer_workload_begin) : 1;
        const auto size = chain ? reinterpret_cast<uint64_t>(&coz_timer_workload_end) - begin : 1;
        require(coz_timer_open(begin, size, 2, 64, &session) == 0 && session, 6);
        CozTimerSession* duplicate{};
        require(coz_timer_open(begin, size, 2, 64, &duplicate) == EALREADY && !duplicate, 7);
        require(coz_timer_register(session, 2) == EINVAL && coz_timer_register(session, 0) == EPERM, 8);
        for (uint32_t slot = 0; slot < 2; ++slot) targets[slot] = std::thread([&, slot] {
            int error = coz_timer_register(session, slot);
            if (error == 0 && coz_timer_register(session, slot) != EALREADY) error = EPROTO;
            uint64_t bad_generation = 99, bad_clock = 99;
            if (error == 0 && (coz_timer_submit(session, slot, 1, 1, &bad_generation) != EPERM || bad_generation != 0
                || coz_timer_clock(session, &bad_clock) != EPERM || bad_clock != 0)) error = EPROTO;
            target_errors[slot].store(error, std::memory_order_release);
            ready[slot].store(1, std::memory_order_release);
            if (error == 0) coz_timer_workload(&stop[slot], &entered[slot], &progress[slot]);
            if (slot == 1 && prepare_retirement.load(std::memory_order_acquire)) {
                sigset_t mask;
                sigemptyset(&mask);
                sigaddset(&mask, SIGRTMIN + 7);
                const auto blocked = pthread_sigmask(SIG_BLOCK, &mask, nullptr);
                if (blocked != 0) target_errors[slot].store(blocked, std::memory_order_release);
                retirement_blocked.store(true, std::memory_order_release);
                while (!permit_retirement.load(std::memory_order_acquire)) std::this_thread::yield();
            }
            const auto retired = coz_timer_retire(session, slot);
            if (retired != 0) target_errors[slot].store(retired, std::memory_order_release);
        });
        await([&] { return ready[0].load(std::memory_order_acquire) && ready[1].load(std::memory_order_acquire); });
        require(target_errors[0].load() == 0 && target_errors[1].load() == 0, 9);
        await([&] { return entered[0].load(std::memory_order_acquire) && entered[1].load(std::memory_order_acquire); });
        require(coz_timer_close(session) == EBUSY && coz_timer_retire(session, 0) == EPERM, 10);
        for (uint32_t command = 1; command <= 3; ++command) {
            const uint32_t slot = command == 2 ? 1 : 0;
            const uint64_t budget = chain ? 500000 : 100000;
            uint64_t generation{}, rejected = 99;
            require(coz_timer_submit(session, slot, 0, 50000, &rejected) == EINVAL && rejected == 0, 11);
            require(coz_timer_submit(session, slot, command, 2000001, &rejected) == EINVAL && rejected == 0, 12);
            require(coz_timer_submit(session, slot, command, budget, &generation) == 0, 13);
            require(generation == (command == 3 ? 2u : 1u), 14);
            require(coz_timer_submit(session, slot, 77, 50000, &rejected) == EBUSY && rejected == 0, 15);
            require(coz_timer_release(session, slot, generation) == EBUSY, 16);
            require(coz_timer_cancel(session, slot, generation + 1) == EINVAL, 17);
            if (command == 3) require(coz_timer_cancel(session, slot, generation) == 0, 30);
            auto& header = headers[command - 1];
            auto& journal = children[command - 1];
            require(coz_timer_snapshot(session, slot, generation, &header, nullptr, 1) == EINVAL, 31);
            require(coz_timer_snapshot(session, slot, generation + 1, &header, journal.data(), journal.size()) == EINVAL, 18);
            await([&] {
                const int result = coz_timer_snapshot(session, slot, generation, &header, nullptr, 0);
                require(result == 0 || result == ENOBUFS, 19);
                return header.visibility == 3;
            });
            require(header.children > 0 && header.children <= journal.size() && header.captured_ns == 0, 20);
            require(coz_timer_release(session, slot, generation) == EBUSY, 21);
            require(coz_timer_snapshot(session, slot, generation, &header, journal.data(), journal.size()) == 0, 22);
            require(coz_timer_clock(session, &receipts[command - 1]) == 0, 23);
            require(header.generation == generation && header.command == command && header.visibility == 3
                && header.error == 0 && header.backend_error == 0 && header.may_have_delivery == 0
                && header.stop == (command == 3 ? 3u : chain ? 1u : 2u)
                && (command == 3 || (chain ? header.measured >= budget && header.children > 1 : header.measured == 0)), 24);
            uint64_t measured{};
            for (uint64_t index = 0; index < header.children; ++index) {
                const auto& child = journal[index];
                require(child.ordinal == index + 1 && child.requested > 0 && child.requested <= 50000 && child.valid == 1
                    && child.wait_status == 0 && child.started >= child.armed && child.finished >= child.started
                    && child.finished - child.started <= 5000000 && child.waited <= child.finished - child.started
                    && header.captured_ns >= child.finished && receipts[command - 1] >= header.captured_ns
                    && receipts[command - 1] - child.armed <= 100000000, 25);
                measured += child.waited;
            }
            require(measured == header.measured, 26);
            require(coz_timer_release(session, slot, generation) == 0 && coz_timer_release(session, slot, generation) == EINVAL, 27);
            ++completed;
        }
        uint64_t pending{};
        prepare_retirement.store(true, std::memory_order_release);
        stop[1].store(1, std::memory_order_release);
        await([&] { return retirement_blocked.load(std::memory_order_acquire); });
        require(target_errors[1].load(std::memory_order_acquire) == 0, 38);
        require(coz_timer_submit(session, 1, 4, 500000, &pending) == 0 && pending == 2, 32);
        permit_retirement.store(true, std::memory_order_release);
        targets[1].join();
        require(target_errors[1].load() == 0, 33);
        require(coz_timer_snapshot(session, 1, pending, &headers[3], children[3].data(), children[3].size()) == 0, 34);
        require(headers[3].visibility == 3 && headers[3].stop == 3 && headers[3].children == 0
            && headers[3].measured == 0 && headers[3].backend_error == 0 && headers[3].error == 0, 35);
        require(coz_timer_clock(session, &receipts[3]) == 0 && receipts[3] >= headers[3].captured_ns, 36);
        require(coz_timer_release(session, 1, pending) == 0 && coz_timer_close(session) == EBUSY, 37);
        ++completed;
    } catch (...) { if (failure == 0) failure = 99; }
    permit_retirement.store(true, std::memory_order_release);
    for (auto& item : stop) item.store(1, std::memory_order_release);
    for (auto& target : targets) if (target.joinable()) target.join();
    if (session) {
        closed = coz_timer_close(session) == 0;
        if (failure == 0 && (!closed || target_errors[0].load() != 0 || target_errors[1].load() != 0)) failure = 28;
        if (closed) {
            uint64_t value = 99;
            if (coz_timer_clock(session, &value) != EINVAL || value != 0 || coz_timer_close(session) != EINVAL) failure = 29;
        }
    }
    std::ofstream output(argv[2]);
    output << "{\"passed\":" << (failure == 0 ? "true" : "false") << ",\"failure\":" << failure
        << ",\"completed\":" << completed << ",\"closed\":" << (closed ? "true" : "false")
        << ",\"retirementBlocked\":" << (retirement_blocked.load() ? "true" : "false") << ",\"observations\":[";
    for (uint32_t index = 0; index < headers.size(); ++index) {
        const auto& header = headers[index];
        if (index) output << ',';
        output << "{\"generation\":" << header.generation << ",\"command\":" << header.command << ",\"children\":" << header.children
            << ",\"measured\":" << header.measured << ",\"visibility\":" << header.visibility << ",\"stop\":" << header.stop
            << ",\"error\":" << header.error << ",\"backendError\":" << header.backend_error << ",\"captured\":" << header.captured_ns
            << ",\"receipt\":" << receipts[index] << ",\"journal\":[";
        for (uint64_t child = 0; child < header.children && child < children[index].size(); ++child) {
            const auto& item = children[index][child];
            if (child) output << ',';
            output << "{\"ordinal\":" << item.ordinal << ",\"armed\":" << item.armed << ",\"started\":" << item.started
                << ",\"finished\":" << item.finished << ",\"waited\":" << item.waited << ",\"requested\":" << item.requested
                << ",\"ip\":" << item.ip << ",\"waitStatus\":" << item.wait_status << ",\"valid\":" << item.valid << '}';
        }
        output << "]}";
    }
    output << "]}\n";
    output.flush();
    return failure == 0 && output.good() ? 0 : 1;
}
