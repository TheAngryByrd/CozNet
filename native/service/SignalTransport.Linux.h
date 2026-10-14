#pragma once
#include "ServiceOwner.h"

namespace coz::service {
class InstructionRange {
public:
    InstructionRange(uint64_t begin, uint64_t size);
    uint64_t begin() const noexcept { return begin_; }
    uint64_t size() const noexcept { return size_; }
    bool contains(uint64_t ip) const noexcept { return ip >= begin_ && ip - begin_ < size_; }
private:
    uint64_t begin_, size_;
};

enum class SignalWaitMode { sleep, spin };

class LinuxSignalTransport final : public Transport {
public:
    LinuxSignalTransport(InstructionRange range, SignalWaitMode mode);
    int install() noexcept override;
    Send request(int slot, uint64_t requested) noexcept override;
    Read read(int slot, uint64_t sequence) noexcept override;
    bool eligible(uint64_t ip) const noexcept override;
    uint64_t now() const noexcept override;
    static uint64_t monotonicNow() noexcept;
    int restore() noexcept override;
    int registerTarget(int slot) noexcept;
    int retireTarget(int slot) noexcept;
    int32_t ownerThreadId() const noexcept { return owner_thread_id_; }
private:
    const InstructionRange range_;
    const int mode_;
    int32_t owner_thread_id_{};
};
}
