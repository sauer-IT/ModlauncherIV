#include "GameVersion.h"

#include <windows.h>

#include <vector>

#pragma comment(lib, "version.lib")

namespace
{
    /// Builds "1.0.7.0" from the four fields of the version resource.
    std::wstring Format(const VS_FIXEDFILEINFO& info)
    {
        wchar_t buffer[32];
        swprintf_s(
            buffer, L"%u.%u.%u.%u",
            HIWORD(info.dwFileVersionMS), LOWORD(info.dwFileVersionMS),
            HIWORD(info.dwFileVersionLS), LOWORD(info.dwFileVersionLS));
        return buffer;
    }
}

namespace mliv
{
    GameInfo DetectGame()
    {
        GameInfo info;

        wchar_t path[MAX_PATH]{};
        if (GetModuleFileNameW(nullptr, path, MAX_PATH) == 0)
        {
            return info;
        }

        info.exePath = path;

        DWORD ignored = 0;
        const DWORD size = GetFileVersionInfoSizeW(path, &ignored);
        if (size == 0)
        {
            return info;
        }

        std::vector<BYTE> block(size);
        if (!GetFileVersionInfoW(path, 0, size, block.data()))
        {
            return info;
        }

        VS_FIXEDFILEINFO* fixed = nullptr;
        UINT length = 0;
        if (!VerQueryValueW(block.data(), L"\\", reinterpret_cast<LPVOID*>(&fixed), &length) ||
            fixed == nullptr)
        {
            return info;
        }

        info.raw = Format(*fixed);

        // Compare by numbers, not by text: the spelling of the version resource
        // varies, the numbers do not. That is exactly what the launcher tripped
        // over once already.
        const WORD major = HIWORD(fixed->dwFileVersionMS);
        const WORD minor = LOWORD(fixed->dwFileVersionMS);
        const WORD build = HIWORD(fixed->dwFileVersionLS);
        const WORD revision = LOWORD(fixed->dwFileVersionLS);

        if (major == 1 && minor == 0 && build == 7 && revision == 0)
        {
            info.version = GameVersion::V1070;
        }
        else if (major == 1 && minor == 0 && build == 8 && revision == 0)
        {
            info.version = GameVersion::V1080;
        }
        else if (major == 1 && minor == 0 && build == 4 && revision == 0)
        {
            info.version = GameVersion::V1040;
        }
        else if (major == 1 && minor == 2)
        {
            info.version = GameVersion::CompleteEdition;
        }

        return info;
    }

    bool IsSupported(const GameVersion version)
    {
        // For now only 1.0.7.0. The Complete Edition has different memory
        // layouts and different native hashes; running there would mean writing
        // to wrong addresses. A trainer that refuses to start is infinitely
        // better than one that wrecks save games.
        return version == GameVersion::V1070;
    }

    const char* Describe(const GameVersion version)
    {
        switch (version)
        {
            case GameVersion::V1040:           return "1.0.4.0";
            case GameVersion::V1070:           return "1.0.7.0";
            case GameVersion::V1080:           return "1.0.8.0";
            case GameVersion::CompleteEdition: return "Complete Edition (1.2.0.x)";
            default:                           return "unbekannt";
        }
    }
}
