#include "LeaseMailbox.h"
#include "TargetLease.h"
#include <array>
#include <cerrno>
#include <chrono>
#include <csignal>
#include <fstream>
#include <thread>
#include <sys/syscall.h>
#include <ucontext.h>
#include <unistd.h>

extern "C" void coz_timer_workload(std::atomic<uint32_t>*, std::atomic<uint32_t>*, std::atomic<uint64_t>*);
extern "C" char coz_timer_workload_begin, coz_timer_workload_end;

namespace {
using namespace coz::service;
constexpr uint64_t cookie = 0x436f7a5265757365ULL, budget = 50000;
constexpr uint32_t generations = 16;
LeaseMailbox mailbox;
std::array<TargetLeaseChild, 4> journal{};
std::optional<TargetLease> slot;
std::atomic<uint32_t> stop{}, entered{}, ready{}, announced{};
std::atomic<uint64_t> progress{};
std::atomic<int> handler_error{};
int timer_id{}, signal_number{}, target_error{}, retired_drained{};
bool created{}, deleted{}, restored{};
struct DrainRecord { int signal{}, code{}, timer{}; uint64_t cookie{}; };
struct TransportObservation {
    int disarm_error{}, drain_error{}, drained{};
    std::array<DrainRecord, 8> records{};
};
std::array<TransportObservation, generations> transports{};
std::array<DrainRecord, 8> retirement_records{};

struct Observation {
    TargetLeaseSnapshot header{};
    std::array<TargetLeaseChild, 4> children{};
    uint64_t captured{}, receipt{}, progress{};
    bool copied{}, reuse_rejected{}, receipt_observed{};
};
std::array<Observation, generations> observations{};

inline long direct(long number, long a = 0, long b = 0, long c = 0, long d = 0) {
    register long fourth asm("r10") = d;
    long result;
    asm volatile("syscall" : "=a"(result) : "a"(number), "D"(a), "S"(b), "d"(c), "r"(fourth)
        : "rcx", "r11", "memory");
    return result;
}
uint64_t now() {
    timespec value{};
    if (direct(SYS_clock_gettime, CLOCK_MONOTONIC, reinterpret_cast<long>(&value)) != 0) return 0;
    return static_cast<uint64_t>(value.tv_sec) * 1000000000ULL + value.tv_nsec;
}
int drain(int& count, std::array<DrainRecord, 8>& records) {
    const uint64_t mask = uint64_t{1} << (signal_number - 1);
    timespec immediate{};
    for (unsigned attempt = 0; attempt < 8; ++attempt) {
        siginfo_t info{};
        const auto result = direct(SYS_rt_sigtimedwait, reinterpret_cast<long>(&mask),
            reinterpret_cast<long>(&info), reinterpret_cast<long>(&immediate), sizeof(mask));
        if (result == -EINTR) continue;
        if (result == -EAGAIN) return 0;
        if (result < 0) return static_cast<int>(-result);
        records[count++] = {static_cast<int>(result), info.si_code, info.si_timerid,
            reinterpret_cast<uint64_t>(info.si_value.sival_ptr)};
        if (result != signal_number || info.si_code != SI_TIMER || info.si_timerid != timer_id
            || reinterpret_cast<uint64_t>(info.si_value.sival_ptr) != cookie) return EPROTO;
    }
    return EOVERFLOW;
}
void handler(int number, siginfo_t* info, void* context) {
    const auto started = now();
    if (number != signal_number || info->si_code != SI_TIMER || info->si_timerid != timer_id
        || reinterpret_cast<uint64_t>(info->si_value.sival_ptr) != cookie) {
        handler_error.store(EPROTO, std::memory_order_release);
        return;
    }
    auto execution = mailbox.claim(announced.load(std::memory_order_acquire));
    if (!execution) { handler_error.store(EPROTO, std::memory_order_release); return; }
    auto& lease = *slot;
    const auto ip = static_cast<uint64_t>(static_cast<ucontext_t*>(context)->uc_mcontext.gregs[REG_RIP]);
    const auto admission = lease.arrive(execution->generation(), ip, started, mailbox.cancelled(*execution));
    if (!admission) { handler_error.store(EPROTO, std::memory_order_release); return; }
    const auto before = now();
    auto after = before;
    int error = before == 0 ? EIO : 0;
    for (unsigned attempt = 0; error == 0 && admission->shouldWait() && after - before < admission->requested(); ++attempt) {
        if (attempt == 4096) { error = ETIMEDOUT; break; }
        const auto next = now();
        if (next == 0 || next < after) { error = EIO; break; }
        after = next;
        asm volatile("pause");
    }
    const auto completion = lease.prepare(*admission, after >= before ? after - before : 0,
        error == 0 ? TargetWaitStatus::completed : TargetWaitStatus::failed, mailbox.cancelled(*execution));
    if (!completion) { handler_error.store(EPROTO, std::memory_order_release); return; }
    itimerspec disabled{};
    const auto disarm = direct(SYS_timer_settime, timer_id, 0, reinterpret_cast<long>(&disabled));
    auto& transport = transports[execution->generation() - 1];
    transport.disarm_error = static_cast<int>(-disarm);
    transport.drain_error = disarm == 0 ? drain(transport.drained, transport.records) : transport.disarm_error;
    if (transport.drain_error != 0 || transport.drained != 0 || completion->shouldRearm())
        error = transport.drain_error != 0 ? transport.drain_error : EPROTO;
    if (!lease.publish(*completion, now(), TimerArmResult::notRequested(), mailbox.cancelled(*execution))) error = EPROTO;
    if (lease.error() != TargetLeaseError::none && error == 0) error = EIO;
    if (error != 0) handler_error.store(error, std::memory_order_release);
    if (transport.drain_error != 0) return;
    if (!lease.quiesced() || !lease.retire()) {
        handler_error.store(EPROTO, std::memory_order_release);
        return;
    }
    static_cast<void>(mailbox.quiesce(*execution));
}
template<class Predicate> bool await(Predicate predicate) {
    const auto deadline = std::chrono::steady_clock::now() + std::chrono::seconds(2);
    while (!predicate()) {
        if (std::chrono::steady_clock::now() >= deadline) return false;
        std::this_thread::yield();
    }
    return true;
}
}

int main(int argc, char** argv) {
    if (argc != 2) return 2;
    signal_number = SIGRTMIN + 7;
    if (signal_number > SIGRTMAX || signal_number > 64) return 3;
    struct sigaction previous{}, action{};
    if (sigaction(signal_number, nullptr, &previous) != 0 || previous.sa_handler != SIG_DFL) return 4;
    action.sa_sigaction = handler;
    action.sa_flags = SA_SIGINFO;
    sigemptyset(&action.sa_mask);
    if (sigaction(signal_number, &action, nullptr) != 0) return 5;
    std::thread target([] {
        sigset_t mask;
        sigemptyset(&mask);
        sigaddset(&mask, signal_number);
        target_error = pthread_sigmask(SIG_BLOCK, &mask, nullptr);
        if (target_error != 0) { ready.store(1, std::memory_order_release); return; }
        sigevent event{};
        event.sigev_notify = SIGEV_THREAD_ID;
        event.sigev_signo = signal_number;
        event.sigev_value.sival_ptr = reinterpret_cast<void*>(cookie);
        event._sigev_un._tid = static_cast<pid_t>(syscall(SYS_gettid));
        if (syscall(SYS_timer_create, CLOCK_MONOTONIC, &event, &timer_id) != 0) target_error = errno;
        else created = true;
        ready.store(1, std::memory_order_release);
        if (!created) return;
        target_error = pthread_sigmask(SIG_UNBLOCK, &mask, nullptr);
        if (target_error == 0) coz_timer_workload(&stop, &entered, &progress);
        const auto blocked = pthread_sigmask(SIG_BLOCK, &mask, nullptr);
        if (blocked != 0) { target_error = blocked; return; }
        if (syscall(SYS_timer_delete, timer_id) != 0) { target_error = errno; return; }
        deleted = true;
        const auto drained = drain(retired_drained, retirement_records);
        if (drained != 0) { target_error = drained; return; }
        if (slot && slot->phase() != TargetLeasePhase::retired) {
            if (slot->phase() == TargetLeasePhase::waiting) slot->cancelPending();
            if (!slot->quiesced() || !slot->retire()) target_error = EPROTO;
        }
    });
    bool valid = await([] { return ready.load(std::memory_order_acquire) != 0; }) && created;
    if (valid) valid = await([] { return entered.load(std::memory_order_acquire) != 0; });
    uint32_t completed = 0;
    int arm_error = 0;
    for (uint32_t generation = 1; valid && generation <= generations; ++generation) {
        std::optional<LeaseMailbox::Submission> submission;
        valid = await([&] { submission = mailbox.begin(); return submission.has_value(); });
        if (!valid) break;
        if (submission->generation() != generation) { valid = false; break; }
        const auto plan = TargetLeasePlan::parse(submission->generation(), generation, budget,
            reinterpret_cast<uint64_t>(&coz_timer_workload_begin),
            reinterpret_cast<uint64_t>(&coz_timer_workload_end) - reinterpret_cast<uint64_t>(&coz_timer_workload_begin));
        if (!plan) { valid = false; break; }
        slot.emplace(*plan, journal);
        const auto armed = now();
        valid = armed != 0 && slot->start(TimerArmResult::accepted(armed));
        announced.store(generation, std::memory_order_release);
        valid = valid && mailbox.publish(*submission);
        if (!valid) break;
        itimerspec next{};
        next.it_value.tv_nsec = 1000000;
        arm_error = static_cast<int>(-direct(SYS_timer_settime, timer_id, 0, reinterpret_cast<long>(&next)));
        if (arm_error != 0) { valid = false; break; }
        auto reader = mailbox.read(generation);
        if (!reader) { valid = false; break; }
        auto& observation = observations[generation - 1];
        valid = await([&] {
            std::size_t count = 0;
            const auto snapshot = slot->snapshot(observation.children, count);
            const auto captured = now();
            if (!snapshot) return false;
            if (count != 0 && !observation.receipt_observed) {
                observation.receipt_observed = true;
                observation.captured = captured;
                observation.receipt = now();
            }
            if (snapshot->visibility != TargetLeaseVisibility::retired) return false;
            observation.header = *snapshot;
            observation.copied = true;
            return true;
        });
        observation.reuse_rejected = !mailbox.begin().has_value();
        observation.progress = progress.load(std::memory_order_relaxed);
        if (!valid) break;
        const auto& child = observation.children[0];
        valid = observation.reuse_rejected && observation.header.generation == generation
            && observation.header.command == generation && observation.header.children == 1
            && observation.header.measured >= budget && observation.header.measured == child.waited
            && !observation.header.may_have_delivery && observation.header.stop == TargetLeaseStop::completed
            && observation.header.error == TargetLeaseError::none && child.valid && child.ordinal == 1
            && child.requested == budget && child.finished >= child.started && child.started >= child.armed
            && child.finished - child.started <= 5000000 && child.waited <= child.finished - child.started
            && observation.receipt_observed && observation.captured != 0
            && observation.receipt >= observation.captured && observation.captured >= child.finished
            && observation.receipt - child.armed <= 100000000 && handler_error.load(std::memory_order_acquire) == 0;
        if (valid) ++completed;
    }
    const bool closed = valid && await([] { return mailbox.close(); });
    stop.store(1, std::memory_order_release);
    target.join();
    const auto last = announced.load(std::memory_order_acquire);
    if (slot && last != 0 && last <= generations && !observations[last - 1].copied) {
        auto& observation = observations[last - 1];
        std::size_t count = 0;
        const auto snapshot = slot->snapshot(observation.children, count);
        if (snapshot) {
            observation.header = *snapshot;
            observation.copied = true;
            if (count != 0 && !observation.receipt_observed) {
                observation.receipt_observed = true;
                observation.captured = now();
                observation.receipt = now();
            }
        }
    }
    if (!created || deleted) restored = sigaction(signal_number, &previous, nullptr) == 0;
    valid = valid && closed && completed == generations && target_error == 0 && arm_error == 0
        && handler_error.load(std::memory_order_acquire) == 0 && deleted && restored && retired_drained == 0;
    std::ofstream output(argv[1]);
    output << "{\"passed\":" << (valid ? "true" : "false") << ",\"completed\":" << completed
        << ",\"timerDeleted\":" << (deleted ? "true" : "false") << ",\"restored\":" << (restored ? "true" : "false")
        << ",\"closed\":" << (closed ? "true" : "false") << ",\"targetError\":" << target_error
        << ",\"handlerError\":" << handler_error.load() << ",\"armError\":" << arm_error
        << ",\"retiredDrained\":" << retired_drained << ",\"timerId\":" << timer_id << ",\"generations\":[";
    for (uint32_t index = 0; index < generations; ++index) {
        const auto& item = observations[index];
        const auto& child = item.children[0];
        if (index) output << ',';
        output << "{\"generation\":" << item.header.generation << ",\"command\":" << item.header.command
            << ",\"copied\":" << (item.copied ? "true" : "false") << ",\"reuseRejected\":" << (item.reuse_rejected ? "true" : "false")
            << ",\"children\":" << item.header.children << ",\"measured\":" << item.header.measured
            << ",\"visibility\":" << static_cast<uint64_t>(item.header.visibility)
            << ",\"stop\":" << static_cast<int>(item.header.stop) << ",\"error\":" << static_cast<int>(item.header.error)
            << ",\"armed\":" << child.armed << ",\"started\":" << child.started << ",\"finished\":" << child.finished
            << ",\"waited\":" << child.waited << ",\"ip\":" << child.ip << ",\"captured\":" << item.captured
            << ",\"firstNativeReceipt\":" << item.receipt << ",\"progress\":" << item.progress
            << ",\"disarmError\":" << transports[index].disarm_error << ",\"drainError\":" << transports[index].drain_error
            << ",\"drained\":" << transports[index].drained << ",\"drainRecords\":[";
        for (int record = 0; record < transports[index].drained; ++record) {
            const auto& raw = transports[index].records[record];
            if (record) output << ',';
            output << "{\"signal\":" << raw.signal << ",\"code\":" << raw.code
                << ",\"timer\":" << raw.timer << ",\"cookie\":" << raw.cookie << '}';
        }
        output << "]}";
    }
    output << "],\"retirementRecords\":[";
    for (int record = 0; record < retired_drained; ++record) {
        const auto& raw = retirement_records[record];
        if (record) output << ',';
        output << "{\"signal\":" << raw.signal << ",\"code\":" << raw.code
            << ",\"timer\":" << raw.timer << ",\"cookie\":" << raw.cookie << '}';
    }
    output << "]}\n";
    output.flush();
    return valid && output.good() ? 0 : 1;
}
