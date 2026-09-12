#include "Config.h"
#include "Pad.h"

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

        /// The keys that can be named.
        ///
        /// NUM* and NUMPAD* are both accepted for the same keys: people coming
        /// from other trainers are used to one or the other, and neither should
        /// have to guess.
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

            {"NUMPLUS", 0x6B},   {"NUMMINUS", 0x6D}, {"NUMMULTIPLY", 0x6A},
            {"NUMDIVIDE", 0x6F}, {"NUMDOT", 0x6E},

            {"UP", 0x26}, {"DOWN", 0x28}, {"LEFT", 0x25}, {"RIGHT", 0x27},

            {"ENTER", 0x0D},  {"RETURN", 0x0D},
            {"BACKSPACE", 0x08},
            {"SPACE", 0x20},
            {"TAB", 0x09},
            {"ESC", 0x1B},    {"ESCAPE", 0x1B},

            {"INSERT", 0x2D}, {"DELETE", 0x2E},
            {"HOME", 0x24},   {"END", 0x23},
            {"PAGEUP", 0x21}, {"PAGEDOWN", 0x22},

            {"SHIFT", 0x10},
            {"CTRL", 0x11},   {"CONTROL", 0x11},
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

        // Single letters and digits are not in the table: their virtual keys
        // are simply the ASCII values of the upper-case characters.
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
                    "Line " + std::to_string(number) + ": no equals sign, skipped.");

                continue;
            }

            const std::string name = Lower(Trim(line.substr(0, equals)));
            const std::string value = Trim(line.substr(equals + 1));

            if (name.empty())
            {
                problems_.push_back("Line " + std::to_string(number) + ": empty name, skipped.");
                continue;
            }

            if (section == "keys")
            {
                bind(name, value);
                continue;
            }

            // [Pad] holds button bindings, with one exception: "Enabled" is a
            // switch, not an action. Two names in one section beats a second
            // section for the same device, which nobody would find.
            if (section == "pad" && name != "enabled")
            {
                bindPad(name, value);
                continue;
            }

            const std::string key = section.empty() ? name : section + "." + name;

            // The last entry wins. A duplicate line is usually a leftover from
            // experimenting, and the lower one is the newer of the two.
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
                // Do not skip silently: somebody writing "NUM 8" with a space
                // would otherwise look for the bug in the game.
                problems_.push_back("Unknown key \"" + name + "\" for " + action + ".");
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

    void Config::bindPad(const std::string& action, const std::string& value)
    {
        std::vector<unsigned short> chords;
        std::istringstream parts(value);
        std::string part;

        while (std::getline(parts, part, ','))
        {
            const std::string text = Trim(part);
            if (text.empty())
            {
                continue;
            }

            std::vector<std::string> unknown;
            const unsigned short chord = PadChordFromNames(text, unknown);

            for (const std::string& name : unknown)
            {
                problems_.push_back("Unknown pad button \"" + name + "\" for " + action + ".");
            }

            // A chord with one name missing is not the chord that was meant.
            // Taking the rest would bind the menu to half a combination, and
            // that half might be a button the game already uses.
            if (chord != PadNone && unknown.empty())
            {
                chords.push_back(chord);
            }
        }

        for (PadBinding& binding : padBindings_)
        {
            if (binding.action == action)
            {
                binding.chords = chords;
                return;
            }
        }

        padBindings_.push_back({action, chords});
    }

    std::vector<unsigned short> Config::chords(const std::string& action) const
    {
        const std::string wanted = Lower(action);

        for (const PadBinding& binding : padBindings_)
        {
            if (binding.action == wanted)
            {
                return binding.chords;
            }
        }

        return {};
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

        if (text == "yes" || text == "on" || text == "1" || text == "true")
        {
            return true;
        }

        if (text == "no" || text == "off" || text == "0" || text == "false")
        {
            return false;
        }

        problems_.push_back(
            name + " is set to \"" + *value + "\" - expected yes or no.");

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

            // Trailing text means somebody meant something other than what was
            // read. "0,5" for instance would silently come out as 0.
            if (used != value->size())
            {
                problems_.push_back(
                    name + " is set to \"" + *value + "\" - expected a number with a dot.");

                return fallback;
            }

            return parsed;
        }
        catch (...)
        {
            problems_.push_back(
                name + " is set to \"" + *value + "\" - that is not a number.");

            return fallback;
        }
    }

    /// The template.
    ///
    /// Pure ASCII: the file lands next to the game and gets opened with whatever
    /// happens to be around. Editor, Notepad++ and the console each guess the
    /// encoding differently, and a wrongly guessed character looks like a broken
    /// trainer.
    std::string Config::DefaultText()
    {
        return
            "# sauer - the trainer that ships with Modlauncher IV\n"
            "#\n"
            "# This file was created on the first start. Changes take effect on\n"
            "# the next game start.\n"
            "#\n"
            "# Separate several keys per action with commas.\n"
            "# Allowed among others: F1 to F12, NUM0 to NUM9, UP, DOWN, LEFT,\n"
            "# RIGHT, ENTER, BACKSPACE, SPACE, DELETE, HOME, END, PAGEUP,\n"
            "# PAGEDOWN, ESC, plus single letters and digits. Anything not\n"
            "# recognised shows up as a message in the log file.\n"
            "\n"
            "[Keys]\n"
            "Menu    = F7\n"
            "Up      = NUM8, UP\n"
            "Down    = NUM2, DOWN\n"
            "Left    = NUM4, LEFT\n"
            "Right   = NUM6, RIGHT\n"
            "Select  = NUM5, ENTER\n"
            "Back    = NUM0, BACKSPACE\n"
            "\n"
            "# Flying (noclip). These keys only work while flying is on, and they\n"
            "# are held rather than tapped. One key each.\n"
            "FlyForward  = W\n"
            "FlyBack     = S\n"
            "FlyLeft     = A\n"
            "FlyRight    = D\n"
            "FlyUp       = SPACE\n"
            "FlyDown     = CTRL\n"
            "\n"
            "# Controller. Works alongside the keyboard, not instead of it.\n"
            "#\n"
            "# Button names: A, B, X, Y (or Cross, Circle, Square, Triangle),\n"
            "# DPadUp, DPadDown, DPadLeft, DPadRight, LB, RB, L3, R3, Start,\n"
            "# Back. Several buttons at once with +, alternatives with commas.\n"
            "#\n"
            "# Menu opens on L3+R3 - both sticks pressed in. A single button\n"
            "# would fire during play, because the game already uses them all.\n"
            "[Pad]\n"
            "Enabled = yes\n"
            "Menu    = L3+R3\n"
            "Up      = DPadUp\n"
            "Down    = DPadDown\n"
            "Left    = DPadLeft\n"
            "Right   = DPadRight\n"
            "Select  = A\n"
            "Back    = B\n"
            "\n"
            "# Position and size of the menu, as a fraction of the screen (0 to 1).\n"
            "# Meant for unusual aspect ratios and for anyone who finds the text\n"
            "# too small.\n"
            "[Menu]\n"
            "Left    = 0.025\n"
            "Top     = 0.12\n"
            "Width   = 0.235\n"
            "Scale   = 1.0\n"
            "\n"
            "[Log]\n"
            "# Set to no if no log file should be written.\n"
            "Enabled = yes\n";
    }
}
