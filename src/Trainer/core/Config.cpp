#include "Config.h"

#include <algorithm>
#include <cctype>
#include <sstream>

namespace mliv
{
    namespace
    {
        struct NamedKey
        {
            const char* name;
            int code;
        };

        /// Die Tasten, die sich benennen lassen.
        ///
        /// Deutsche Namen, weil der Rest der Oberflaeche deutsch ist, mit den
        /// englischen als Zweitname - wer aus anderen Trainern "ENTER" oder
        /// "NUMPAD8" gewohnt ist, soll nicht raten muessen.
        const NamedKey kKeys[] = {
            {"F1", 0x70},  {"F2", 0x71},  {"F3", 0x72},  {"F4", 0x73},
            {"F5", 0x74},  {"F6", 0x75},  {"F7", 0x76},  {"F8", 0x77},
            {"F9", 0x78},  {"F10", 0x79}, {"F11", 0x7A}, {"F12", 0x7B},

            {"NUM0", 0x60}, {"NUM1", 0x61}, {"NUM2", 0x62}, {"NUM3", 0x63},
            {"NUM4", 0x64}, {"NUM5", 0x65}, {"NUM6", 0x66}, {"NUM7", 0x67},
            {"NUM8", 0x68}, {"NUM9", 0x69},

            {"NUMPAD0", 0x60}, {"NUMPAD1", 0x61}, {"NUMPAD2", 0x62},
            {"NUMPAD3", 0x63}, {"NUMPAD4", 0x64}, {"NUMPAD5", 0x65},
            {"NUMPAD6", 0x66}, {"NUMPAD7", 0x67}, {"NUMPAD8", 0x68},
            {"NUMPAD9", 0x69},

            {"NUMPLUS", 0x6B},    {"NUMMINUS", 0x6D}, {"NUMMAL", 0x6A},
            {"NUMGETEILT", 0x6F}, {"NUMPUNKT", 0x6E},

            {"HOCH", 0x26},  {"RUNTER", 0x28}, {"LINKS", 0x25}, {"RECHTS", 0x27},
            {"UP", 0x26},    {"DOWN", 0x28},   {"LEFT", 0x25},  {"RIGHT", 0x27},

            {"EINGABE", 0x0D},    {"ENTER", 0x0D},
            {"RUECKTASTE", 0x08}, {"BACKSPACE", 0x08},
            {"LEERTASTE", 0x20},  {"SPACE", 0x20},
            {"TABULATOR", 0x09},  {"TAB", 0x09},
            {"ESC", 0x1B},        {"ESCAPE", 0x1B},

            {"EINFG", 0x2D}, {"INSERT", 0x2D}, {"ENTF", 0x2E},  {"DELETE", 0x2E},
            {"POS1", 0x24},  {"HOME", 0x24},   {"ENDE", 0x23},  {"END", 0x23},
            {"BILDHOCH", 0x21}, {"PAGEUP", 0x21},
            {"BILDRUNTER", 0x22}, {"PAGEDOWN", 0x22},

            {"UMSCHALT", 0x10}, {"SHIFT", 0x10},
            {"STRG", 0x11},     {"CTRL", 0x11},
            {"ALT", 0x12},
        };

        std::string Trim(const std::string& text)
        {
            const auto first = text.find_first_not_of(" \t\r\n");
            if (first == std::string::npos)
            {
                return {};
            }

            const auto last = text.find_last_not_of(" \t\r\n");
            return text.substr(first, last - first + 1);
        }

        std::string Upper(const std::string& text)
        {
            std::string out = text;
            std::transform(out.begin(), out.end(), out.begin(),
                           [](const unsigned char c) { return static_cast<char>(std::toupper(c)); });

            return out;
        }

        std::string Lower(const std::string& text)
        {
            std::string out = text;
            std::transform(out.begin(), out.end(), out.begin(),
                           [](const unsigned char c) { return static_cast<char>(std::tolower(c)); });

            return out;
        }
    }

    int KeyCodeFromName(const std::string& name)
    {
        const std::string wanted = Upper(Trim(name));
        if (wanted.empty())
        {
            return 0;
        }

        // Einzelne Buchstaben und Ziffern stehen nicht in der Tabelle: ihre
        // Virtual-Keys sind schlicht die ASCII-Werte der Grossbuchstaben.
        if (wanted.size() == 1)
        {
            const char c = wanted[0];
            if ((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9'))
            {
                return static_cast<int>(c);
            }
        }

        for (const NamedKey& entry : kKeys)
        {
            if (wanted == entry.name)
            {
                return entry.code;
            }
        }

        return 0;
    }

    std::string KeyNameFromCode(const int code)
    {
        for (const NamedKey& entry : kKeys)
        {
            if (entry.code == code)
            {
                return entry.name;
            }
        }

        if ((code >= 'A' && code <= 'Z') || (code >= '0' && code <= '9'))
        {
            return std::string(1, static_cast<char>(code));
        }

        return {};
    }

    void Config::parse(const std::string& text)
    {
        std::istringstream stream(text);
        std::string line;
        std::string section;
        int number = 0;

        while (std::getline(stream, line))
        {
            ++number;
            line = Trim(line);

            if (line.empty() || line[0] == '#' || line[0] == ';')
            {
                continue;
            }

            if (line.front() == '[' && line.back() == ']')
            {
                section = Lower(Trim(line.substr(1, line.size() - 2)));
                continue;
            }

            const auto equals = line.find('=');
            if (equals == std::string::npos)
            {
                problems_.push_back(
                    "Zeile " + std::to_string(number) + ": kein Gleichheitszeichen, uebergangen.");

                continue;
            }

            const std::string name = Lower(Trim(line.substr(0, equals)));
            const std::string value = Trim(line.substr(equals + 1));

            if (name.empty())
            {
                problems_.push_back("Zeile " + std::to_string(number) + ": leerer Name, uebergangen.");
                continue;
            }

            if (section == "tasten")
            {
                bind(name, value);
                continue;
            }

            const std::string key = section.empty() ? name : section + "." + name;

            // Der letzte Eintrag gewinnt. Eine doppelte Zeile ist meistens ein
            // Ueberbleibsel vom Ausprobieren, und die untere ist die neuere.
            bool replaced = false;
            for (Entry& entry : entries_)
            {
                if (entry.key == key)
                {
                    entry.value = value;
                    replaced = true;
                    break;
                }
            }

            if (!replaced)
            {
                entries_.push_back({key, value});
            }
        }
    }

    void Config::bind(const std::string& action, const std::string& value)
    {
        std::vector<int> codes;
        std::istringstream parts(value);
        std::string part;

        while (std::getline(parts, part, ','))
        {
            const std::string name = Trim(part);
            if (name.empty())
            {
                continue;
            }

            const int code = KeyCodeFromName(name);
            if (code == 0)
            {
                // Nicht stillschweigend uebergehen: wer "NUM 8" mit Leerzeichen
                // schreibt, sucht den Fehler sonst im Spiel.
                problems_.push_back("Unbekannte Taste \"" + name + "\" bei " + action + ".");
                continue;
            }

            codes.push_back(code);
        }

        for (Binding& binding : bindings_)
        {
            if (binding.action == action)
            {
                binding.codes = codes;
                return;
            }
        }

        bindings_.push_back({action, codes});
    }

    const std::string* Config::find(const std::string& key) const
    {
        for (const Entry& entry : entries_)
        {
            if (entry.key == key)
            {
                return &entry.value;
            }
        }

        return nullptr;
    }

    std::vector<int> Config::keys(const std::string& action) const
    {
        const std::string wanted = Lower(action);

        for (const Binding& binding : bindings_)
        {
            if (binding.action == wanted)
            {
                return binding.codes;
            }
        }

        return {};
    }

    bool Config::flag(const std::string& name, const bool fallback) const
    {
        const std::string* value = find(Lower(name));
        if (value == nullptr)
        {
            return fallback;
        }

        const std::string text = Lower(*value);

        if (text == "ja" || text == "an" || text == "1" || text == "true" || text == "wahr")
        {
            return true;
        }

        if (text == "nein" || text == "aus" || text == "0" || text == "false" || text == "falsch")
        {
            return false;
        }

        problems_.push_back(
            "Bei " + name + " steht \"" + *value + "\" - erwartet wird ja oder nein.");

        return fallback;
    }

    float Config::number(const std::string& name, const float fallback) const
    {
        const std::string* value = find(Lower(name));
        if (value == nullptr)
        {
            return fallback;
        }

        try
        {
            size_t used = 0;
            const float parsed = std::stof(*value, &used);

            // Nachgestellter Text heisst, dass jemand etwas anderes meinte als
            // das, was gelesen wurde. "0,5" etwa ergaebe stillschweigend 0.
            if (used != value->size())
            {
                problems_.push_back(
                    "Bei " + name + " steht \"" + *value + "\" - erwartet wird eine Zahl mit Punkt.");

                return fallback;
            }

            return parsed;
        }
        catch (...)
        {
            problems_.push_back(
                "Bei " + name + " steht \"" + *value + "\" - das ist keine Zahl.");

            return fallback;
        }
    }

    /// Die Vorlage.
    ///
    /// Reines ASCII, keine Umlaute: die Datei landet neben dem Spiel und wird
    /// mit dem geoeffnet, was gerade da ist. Editor, Notepad++ und die Konsole
    /// raten die Kodierung unterschiedlich, und ein falsch geratenes Umlaut
    /// sieht aus wie ein kaputter Trainer.
    std::string Config::DefaultText()
    {
        return
            "# Modlauncher IV Trainer\n"
            "#\n"
            "# Diese Datei wurde beim ersten Start angelegt. Aenderungen greifen\n"
            "# beim naechsten Spielstart.\n"
            "#\n"
            "# Mehrere Tasten pro Aktion durch Komma trennen.\n"
            "# Erlaubt sind unter anderem: F1 bis F12, NUM0 bis NUM9, HOCH,\n"
            "# RUNTER, LINKS, RECHTS, ENTER, RUECKTASTE, LEERTASTE, ENTF, POS1,\n"
            "# ENDE, BILDHOCH, BILDRUNTER, ESC, sowie einzelne Buchstaben und\n"
            "# Ziffern. Was nicht erkannt wird, steht als Meldung im Logfile.\n"
            "\n"
            "[Tasten]\n"
            "Menue   = F7\n"
            "Hoch    = NUM8, HOCH\n"
            "Runter  = NUM2, RUNTER\n"
            "Links   = NUM4, LINKS\n"
            "Rechts  = NUM6, RECHTS\n"
            "Waehlen = NUM5, ENTER\n"
            "Zurueck = NUM0, RUECKTASTE\n"
            "\n"
            "# Fliegen (Noclip). Diese Tasten wirken nur, solange Fliegen an ist,\n"
            "# und werden gehalten statt getippt. Jeweils nur eine Taste.\n"
            "FlugVor     = W\n"
            "FlugZurueck = S\n"
            "FlugLinks   = A\n"
            "FlugRechts  = D\n"
            "FlugHoch    = LEERTASTE\n"
            "FlugRunter  = STRG\n"
            "\n"
            "# Lage und Groesse des Menues, in Bildanteilen von 0 bis 1.\n"
            "# Gedacht fuer ungewoehnliche Seitenverhaeltnisse und fuer alle, denen\n"
            "# die Schrift zu klein ist.\n"
            "[Menue]\n"
            "Links   = 0.025\n"
            "Oben    = 0.12\n"
            "Breite  = 0.235\n"
            "Schrift = 1.0\n"
            "\n"
            "[Protokoll]\n"
            "# Auf nein stellen, wenn kein Logfile geschrieben werden soll.\n"
            "Aktiv = ja\n";
    }
}
