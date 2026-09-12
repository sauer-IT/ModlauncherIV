#pragma once

#include <string>
#include <vector>

namespace mliv
{
    /// Wandelt einen Tastennamen aus der Konfiguration in einen Virtual-Key.
    /// 0, wenn der Name unbekannt ist.
    ///
    /// Die Zahlenwerte stehen hier ausgeschrieben statt aus Windows.h zu kommen.
    /// Das haelt dieses Modul frei von Plattformheadern und damit pruefbar ohne
    /// Spiel und ohne Windows-SDK - derselbe Grund, aus dem die Menuelogik das
    /// Spiel nicht kennt. Die Werte sind seit Windows 3.0 unveraendert.
    int KeyCodeFromName(const std::string& name);

    /// Rueckrichtung, fuer die selbst geschriebene Vorlage. Leer bei unbekanntem Code.
    std::string KeyNameFromCode(int code);

    /// Die Konfiguration des Trainers.
    ///
    /// Ein schlichtes INI-Format, weil man es ohne Werkzeug aendern kann und
    /// weil es in dieser Szene das Uebliche ist. Kein JSON: eine verlorene
    /// Klammer macht dort die ganze Datei unlesbar, und der Nutzer sitzt dann
    /// vor einem Trainer, der ohne Erklaerung auf Standardtasten zurueckfaellt.
    class Config
    {
    public:
        /// Liest Text ein. Mehrfaches Aufrufen ueberschreibt, was vorkommt, und
        /// laesst den Rest stehen.
        void parse(const std::string& text);

        /// Tastenliste zu einer Aktion. Leer, wenn nichts eingetragen ist -
        /// der Aufrufer nimmt dann seine Vorgabe.
        ///
        /// Mehrere Tasten pro Aktion sind Absicht: ein Notebook ohne Zehnerblock
        /// laesst sich sonst nicht bedienen.
        std::vector<int> keys(const std::string& action) const;

        bool flag(const std::string& name, bool fallback) const;

        float number(const std::string& name, float fallback) const;

        /// Was beim Lesen nicht aufgegangen ist, in der Sprache des Nutzers.
        ///
        /// Gesammelt statt gemeldet: eine unbekannte Taste darf den Trainer
        /// nicht am Starten hindern, aber stillschweigend uebergehen darf man
        /// sie auch nicht - sonst sucht jemand den Fehler im Spiel.
        const std::vector<std::string>& problems() const { return problems_; }

        /// Der Inhalt, den der Trainer anlegt, wenn noch keine Datei da ist.
        static std::string DefaultText();

    private:
        struct Entry
        {
            std::string key;    ///< "abschnitt.name", klein geschrieben
            std::string value;
        };

        struct Binding
        {
            std::string action; ///< klein geschrieben
            std::vector<int> codes;
        };

        const std::string* find(const std::string& key) const;

        /// Loest die Tastennamen eines [Tasten]-Eintrags auf und meldet, was
        /// nicht aufgeht. Passiert beim Einlesen, nicht beim Abfragen: sonst
        /// bliebe der Tippfehler in einer Aktion, nach der niemand fragt, fuer
        /// immer unbemerkt, und welche Meldungen vorliegen haenge davon ab,
        /// was der Aufrufer zufaellig schon gelesen hat.
        void bind(const std::string& action, const std::string& value);

        std::vector<Entry> entries_;
        std::vector<Binding> bindings_;

        /// mutable, weil es ein Protokoll ist und keine Aussage ueber den
        /// Inhalt: dass beim Lesen etwas nicht aufging, aendert nichts daran,
        /// dass die Abfrage selbst eine reine Auskunft bleibt.
        mutable std::vector<std::string> problems_;
    };
}
