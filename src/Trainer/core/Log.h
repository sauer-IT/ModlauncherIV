#pragma once

#include <string>

// For an ASI plugin the log file is the only reliable channel to the outside.
// A debugger on a fullscreen game is useless, console output there is none, and a
// crash takes every message still sitting in the buffer with it. That is why
// it is flushed after every line.
namespace mliv
{
    /// Creates the log file next to the DLL. Calling it repeatedly is harmless.
    void LogOpen(const std::wstring& dllPath);

    /// Writes one timestamped line. printf format.
    void LogLine(const char* format, ...);

    void LogClose();
}
