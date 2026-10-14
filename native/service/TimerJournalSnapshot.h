#pragma once
#include "LeaseMailbox.h"
#include "TargetLease.h"

namespace coz::service {
inline std::optional<TargetLeaseSnapshot> captureTimerJournal(const TargetLease& lease,
    const LeaseMailbox::Reader& reader, std::span<TargetLeaseChild> destination, std::size_t& copied) noexcept {
    auto snapshot = lease.snapshot(destination, copied);
    if (!snapshot || snapshot->generation != reader.generation()) return std::nullopt;
    if (snapshot->visibility == TargetLeaseVisibility::retired && !reader.quiescent())
        snapshot->visibility = TargetLeaseVisibility::quiesced;
    return snapshot;
}
}
