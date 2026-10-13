#pragma once

#ifdef _WIN32
#define COZ_EXPORT extern "C" __declspec(dllexport)
#define COZ_CLASS_EXPORT extern "C"
#else
#define COZ_EXPORT extern "C" __attribute__((visibility("default")))
#define COZ_CLASS_EXPORT COZ_EXPORT
#endif
