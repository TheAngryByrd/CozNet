#include "SnapshotBackend.h"

#ifndef _WIN32
#error This backend requires Windows. Select a backend for the target platform.
#endif

class WindowsSnapshotBackend final : public SnapshotBackend {
    ICorProfilerInfo2* info;
public:
    explicit WindowsSnapshotBackend(ICorProfilerInfo2* runtime) : info(runtime) { info->AddRef(); }
    ~WindowsSnapshotBackend() override { info->Release(); }
    HRESULT Capture(ThreadID target, StackSnapshotCallback* callback, void* context) override {
        return info->DoStackSnapshot(target, callback, COR_PRF_SNAPSHOT_DEFAULT, context, nullptr, 0);
    }
};

std::unique_ptr<SnapshotBackend> CreateSnapshotBackend(ICorProfilerInfo2* runtime) {
    return std::make_unique<WindowsSnapshotBackend>(runtime);
}
