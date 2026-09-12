// Modlauncher IV - Trainer, Stufe T0
//
// Zweck dieser Stufe: laden, sich melden, und sonst nichts. Damit beantwortet
// sie zwei Fragen, die sich ohne echtes Laden nicht beantworten lassen:
//
//   1. Nimmt der ASI-Loader das Plugin ueberhaupt an?
//   2. Laesst Smart App Control eine unsignierte DLL in GTAIV.exe zu?
//
// Erst wenn beides geklaert ist, lohnt sich Menue- und Feature-Arbeit.

#include <windows.h>

#include "core/Log.h"
#include "game/GameVersion.h"

namespace
{
    HMODULE g_self = nullptr;

    std::wstring SelfPath()
    {
        wchar_t path[MAX_PATH]{};
        GetModuleFileNameW(g_self, path, MAX_PATH);
        return path;
    }

    /// Laeuft als eigener Thread, nicht in DllMain.
    ///
    /// In DllMain haelt Windows die Loader-Sperre. Wer dort mehr tut als das
    /// Noetigste - Dateien oeffnen, andere DLLs anfassen, warten - riskiert
    /// einen Deadlock beim Spielstart, der sich als "haengt beim Laden"
    /// aeussert und sehr schwer zu finden ist.
    DWORD WINAPI Main(LPVOID)
    {
        const std::wstring self = SelfPath();
        mliv::LogOpen(self);

        mliv::LogLine("Modlauncher IV Trainer, Stufe T0");
        mliv::LogLine("Geladen aus: %ls", self.c_str());

        const mliv::GameInfo game = mliv::DetectGame();
        mliv::LogLine("Spiel:       %ls", game.exePath.c_str());
        mliv::LogLine("Version:     %ls (%s)",
                      game.raw.empty() ? L"(nicht lesbar)" : game.raw.c_str(),
                      mliv::Describe(game.version));

        if (!mliv::IsSupported(game.version))
        {
            mliv::LogLine("");
            mliv::LogLine("ABBRUCH: Diese Version wird nicht unterstuetzt.");
            mliv::LogLine("Der Trainer macht hier nichts. Das ist Absicht - auf einer");
            mliv::LogLine("anderen Version stimmen Native-Hashes und Speicheradressen");
            mliv::LogLine("nicht, und Schreiben an falschen Adressen faellt nicht sofort");
            mliv::LogLine("auf, sondern spaeter und an ganz anderer Stelle.");
            return 0;
        }

        mliv::LogLine("Version wird unterstuetzt. T0 endet hier - noch ohne Tick und Menue.");

        // Ab T1 folgt hier die Anbindung an den Script-Thread des Spiels.
        return 0;
    }
}

BOOL APIENTRY DllMain(const HMODULE module, const DWORD reason, LPVOID)
{
    switch (reason)
    {
        case DLL_PROCESS_ATTACH:
        {
            g_self = module;

            // Wir brauchen keine Thread-Benachrichtigungen und sparen uns so
            // Aufrufe bei jedem der rund fuenfzig Spiel-Threads.
            DisableThreadLibraryCalls(module);

            const HANDLE thread = CreateThread(nullptr, 0, Main, nullptr, 0, nullptr);
            if (thread != nullptr)
            {
                CloseHandle(thread);
            }

            break;
        }

        case DLL_PROCESS_DETACH:
            mliv::LogClose();
            break;

        default:
            break;
    }

    return TRUE;
}
