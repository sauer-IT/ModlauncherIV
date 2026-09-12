#pragma once

#include <string>

namespace mliv
{
    enum class GameVersion
    {
        Unknown,
        V1040,
        V1070,
        V1080,
        CompleteEdition,
    };

    struct GameInfo
    {
        GameVersion version = GameVersion::Unknown;
        std::wstring raw;       ///< Versionsangabe, wie die EXE sie meldet.
        std::wstring exePath;
    };

    /// Reads the running EXE's version from its version resource.
    GameInfo DetectGame();

    /// The versions this trainer is allowed to run on.
    bool IsSupported(GameVersion version);

    const char* Describe(GameVersion version);
}
