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

    /// Liest die Version der laufenden EXE aus ihrer Versionsressource.
    GameInfo DetectGame();

    /// Versionen, auf denen dieser Trainer laufen darf.
    bool IsSupported(GameVersion version);

    const char* Describe(GameVersion version);
}
