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
        // 1.0.7.0 and 1.0.8.0, and deliberately nothing else.
        //
        // Not our choice but the SDK's: it carries an address for both versions
        // at every call site - AddressSetter::Get(addr1070, addr1080) - and picks
        // by the same version resource this file reads. Everything below that is
        // native calls, which the game resolves by hash and which therefore do
        // not care about the version at all.
        //
        // On anything else the SDK hooks nothing and gameStartupEvent is never
        // reached, so this check is the second line rather than the first. It
        // stays because a check that only exists further down is one refactor
        // away from not existing: a trainer that refuses to start is infinitely
        // better than one writing to wrong addresses.
        //
        // Measured on 1.0.7.0. 1.0.8.0 rests on the SDK's address set, not on a
        // run of our own.
        return version == GameVersion::V1070 || version == GameVersion::V1080;
    }

    const char* Describe(const GameVersion version)
    {
        switch (version)
        {
            case GameVersion::V1040:           return "1.0.4.0";
            case GameVersion::V1070:           return "1.0.7.0";
            case GameVersion::V1080:           return "1.0.8.0";
            case GameVersion::CompleteEdition: return "Complete Edition (1.2.0.x)";
            default:                           return "unknown";
        }
    }
}
