#pragma once
#include <cor.h>
#include <corprof.h>
#include <memory>

class SnapshotBackend {
public:
    virtual ~SnapshotBackend() = default;
    virtual HRESULT Capture(ThreadID target, StackSnapshotCallback* callback, void* context) = 0;
};

std::unique_ptr<SnapshotBackend> CreateSnapshotBackend(ICorProfilerInfo2* runtime);
