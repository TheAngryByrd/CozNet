#pragma once
#include <cor.h>
#include <corprof.h>
#include <memory>
#include <cstdint>

class SnapshotBackend {
public:
    virtual ~SnapshotBackend() = default;
    virtual HRESULT Capture(ThreadID target, StackSnapshotCallback* callback, void* context) = 0;
    virtual HRESULT ReadCpuNanoseconds(std::uint64_t* value) = 0;
};

std::unique_ptr<SnapshotBackend> CreateSnapshotBackend(ICorProfilerInfo2* runtime);
