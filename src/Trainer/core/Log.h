#pragma once

#include <string>

// Das Logfile ist bei einem ASI-Plugin der einzige verlaessliche Kanal nach
// aussen. Ein Debugger am Vollbildspiel ist unbrauchbar, Konsolenausgabe gibt
// es nicht, und ein Absturz nimmt jede Meldung mit, die noch im Puffer steht.
// Deshalb wird nach jeder Zeile geleert (flush).
namespace mliv
{
    /// Legt das Logfile neben der DLL an. Mehrfachaufrufe sind harmlos.
    void LogOpen(const std::wstring& dllPath);

    /// Schreibt eine Zeile mit Zeitstempel. printf-Format.
    void LogLine(const char* format, ...);

    void LogClose();
}
