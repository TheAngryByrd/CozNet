#include "SnapshotBackend.h"

class UnsupportedSnapshotBackend final : public SnapshotBackend {
public:
    HRESULT Capture(ThreadID, StackSnapshotCallback*, void*) override { return E_NOTIMPL; }
    HRESULT ReadCpuNanoseconds(std::uint64_t*) override { return E_NOTIMPL; }
};

std::unique_ptr<SnapshotBackend> CreateSnapshotBackend(ICorProfilerInfo2*) {
    return std::make_unique<UnsupportedSnapshotBackend>();
}
