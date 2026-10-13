#include "SnapshotBackend.h"
#include <limits>

#ifndef _WIN32
#error This backend requires Windows. Select a backend for the target platform.
#endif

class WindowsSnapshotBackend final : public SnapshotBackend {
    ICorProfilerInfo2* info;
    HANDLE cpuThread = nullptr;
    HRESULT clockStatus = S_OK;
public:
    explicit WindowsSnapshotBackend(ICorProfilerInfo2* runtime) : info(runtime) {
        info->AddRef();
        if (!DuplicateHandle(GetCurrentProcess(), GetCurrentThread(), GetCurrentProcess(),
            &cpuThread, THREAD_QUERY_LIMITED_INFORMATION, FALSE, 0))
            clockStatus = HRESULT_FROM_WIN32(GetLastError());
    }
    ~WindowsSnapshotBackend() override {
        if (cpuThread) CloseHandle(cpuThread);
        info->Release();
    }
    HRESULT Capture(ThreadID target, StackSnapshotCallback* callback, void* context) override {
        return info->DoStackSnapshot(target, callback, COR_PRF_SNAPSHOT_DEFAULT, context, nullptr, 0);
    }
    HRESULT ReadCpuNanoseconds(std::uint64_t* value) override {
        if (!value) return E_POINTER;
        if (FAILED(clockStatus)) return clockStatus;
        FILETIME creation, exit, kernel, user;
        if (!GetThreadTimes(cpuThread, &creation, &exit, &kernel, &user))
            return HRESULT_FROM_WIN32(GetLastError());
        auto ticks = [](FILETIME time) {
            return (static_cast<std::uint64_t>(time.dwHighDateTime) << 32) | time.dwLowDateTime;
        };
        auto kernelTicks = ticks(kernel);
        auto userTicks = ticks(user);
        auto limit = std::numeric_limits<std::uint64_t>::max() / 100;
        if (kernelTicks > limit || userTicks > limit - kernelTicks) return E_FAIL;
        *value = (kernelTicks + userTicks) * 100;
        return S_OK;
    }
};

std::unique_ptr<SnapshotBackend> CreateSnapshotBackend(ICorProfilerInfo2* runtime) {
    return std::make_unique<WindowsSnapshotBackend>(runtime);
}
