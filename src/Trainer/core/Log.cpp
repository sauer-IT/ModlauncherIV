#include "Log.h"

#include <windows.h>

#include <cstdarg>
#include <cstdio>
#include <mutex>

namespace
{
    HANDLE g_file = INVALID_HANDLE_VALUE;
    std::mutex g_mutex;

    std::wstring DirectoryOf(const std::wstring& path)
    {
        const auto slash = path.find_last_of(L"\\/");
        return slash == std::wstring::npos ? L"." : path.substr(0, slash);
    }

    /// Creates the log file. FILE_SHARE_READ so it can be followed while
    /// playing. CREATE_ALWAYS because every game start should begin a fresh log
    /// - an ever-growing file would only be in the way when hunting for the last
    /// crash.
    bool TryOpen(const std::wstring& file)
    {
        const HANDLE handle = CreateFileW(
            file.c_str(),
            GENERIC_WRITE,
            FILE_SHARE_READ,
            nullptr,
            CREATE_ALWAYS,
            FILE_ATTRIBUTE_NORMAL,
            nullptr);

        if (handle == INVALID_HANDLE_VALUE)
        {
            return false;
        }

        g_file = handle;
        return true;
    }
}

namespace mliv
{
    void LogOpen(const std::wstring& dllPath)
    {
        std::lock_guard<std::mutex> lock(g_mutex);
        if (g_file != INVALID_HANDLE_VALUE)
        {
            return;
        }

        // Next to the DLL first: that is where people look for it, and where
        // anyone who has used an ASI plugin before expects it.
        //
        // The game often sits under Program Files though, and runs without
        // elevation. Creating it then fails - and a trainer without a log file
        // is just as silent about a problem as one that never loaded at all.
        // Hence the fallback under LOCALAPPDATA, where the launcher keeps its
        // state as well.
        if (TryOpen(DirectoryOf(dllPath) + L"\\ModlauncherIV-Trainer.log"))
        {
            return;
        }

        wchar_t appData[MAX_PATH]{};
        if (GetEnvironmentVariableW(L"LOCALAPPDATA", appData, MAX_PATH) != 0)
        {
            const std::wstring folder = std::wstring(appData) + L"\\ModlauncherIV";
            CreateDirectoryW(folder.c_str(), nullptr);
            TryOpen(folder + L"\\Trainer.log");
        }
    }

    void LogLine(const char* format, ...)
    {
        std::lock_guard<std::mutex> lock(g_mutex);
        if (g_file == INVALID_HANDLE_VALUE)
        {
            return;
        }

        SYSTEMTIME now{};
        GetLocalTime(&now);

        char line[1024];
        const int stamp = std::snprintf(
            line, sizeof(line), "[%02d:%02d:%02d.%03d] ",
            now.wHour, now.wMinute, now.wSecond, now.wMilliseconds);

        if (stamp < 0)
        {
            return;
        }

        va_list args;
        va_start(args, format);
        const int written = std::vsnprintf(
            line + stamp, sizeof(line) - static_cast<size_t>(stamp) - 2, format, args);
        va_end(args);

        if (written < 0)
        {
            return;
        }

        size_t length = static_cast<size_t>(stamp) + static_cast<size_t>(written);
        if (length > sizeof(line) - 3)
        {
            length = sizeof(line) - 3;
        }

        line[length++] = '\r';
        line[length++] = '\n';

        DWORD ignored = 0;
        WriteFile(g_file, line, static_cast<DWORD>(length), &ignored, nullptr);

        // Without this, the one line that would have explained the crash is
        // verraten haette, woran es lag.
        FlushFileBuffers(g_file);
    }

    void LogClose()
    {
        std::lock_guard<std::mutex> lock(g_mutex);
        if (g_file != INVALID_HANDLE_VALUE)
        {
            CloseHandle(g_file);
            g_file = INVALID_HANDLE_VALUE;
        }
    }
}
