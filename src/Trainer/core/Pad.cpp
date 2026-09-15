#include "Pad.h"

#include <algorithm>
#include <cctype>

namespace mliv
{
    namespace
    {
        std::string Lower(std::string text)
        {
            std::transform(text.begin(), text.end(), text.begin(),
                           [](unsigned char c) { return static_cast<char>(std::tolower(c)); });

            return text;
        }

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

        struct Named
        {
            const char* name;
            unsigned button;
            bool canonical;   ///< the spelling we write into the template
        };

        /// Several names per button on purpose.
        ///
        /// Whoever holds a PlayStation pad reads Cross and Circle on it, and a
        /// configuration that only accepts A and B tells them their pad is not
        /// supported. It is the same bit either way.
        const Named kButtons[] = {
            {"dpadup",    PadUp,            true},
            {"up",        PadUp,            false},
            {"dpaddown",  PadDown,          true},
            {"down",      PadDown,          false},
            {"dpadleft",  PadLeft,          true},
            {"left",      PadLeft,          false},
            {"dpadright", PadRight,         true},
            {"right",     PadRight,         false},

            {"start",     PadStart,         true},
            {"menu",      PadStart,         false},
            {"back",      PadBack,          true},
            {"view",      PadBack,          false},
            {"select",    PadBack,          false},

            {"l3",        PadLeftStick,     true},
            {"leftstick", PadLeftStick,     false},
            {"ls",        PadLeftStick,     false},
            {"r3",        PadRightStick,    true},
            {"rightstick",PadRightStick,    false},
            {"rs",        PadRightStick,    false},

            {"lb",        PadLeftShoulder,  true},
            {"l1",        PadLeftShoulder,  false},
            {"rb",        PadRightShoulder, true},
            {"r1",        PadRightShoulder, false},

            {"a",         PadA,             true},
            {"cross",     PadA,             false},
            {"b",         PadB,             true},
            {"circle",    PadB,             false},
            {"x",         PadX,             true},
            {"square",    PadX,             false},
            {"y",         PadY,             true},
            {"triangle",  PadY,             false},

            // The sticks as directions. Written out in full because "LS" is
            // already the click, and a name that means two things in one file
            // is a name that gets used for the wrong one.
            {"lstickup",    PadLStickUp,    true},
            {"leftstickup", PadLStickUp,    false},
            {"lstickdown",  PadLStickDown,  true},
            {"leftstickdown", PadLStickDown, false},
            {"lstickleft",  PadLStickLeft,  true},
            {"leftstickleft", PadLStickLeft, false},
            {"lstickright", PadLStickRight, true},
            {"leftstickright", PadLStickRight, false},

            {"rstickup",    PadRStickUp,    true},
            {"rstickdown",  PadRStickDown,  true},
            {"rstickleft",  PadRStickLeft,  true},
            {"rstickright", PadRStickRight, true},
        };
    }

    unsigned PadButtonFromName(const std::string& name)
    {
        const std::string wanted = Lower(Trim(name));
        if (wanted.empty())
        {
            return PadNone;
        }

        for (const Named& entry : kButtons)
        {
            if (wanted == entry.name)
            {
                return entry.button;
            }
        }

        return PadNone;
    }

    std::string PadNameFromButton(const unsigned button)
    {
        for (const Named& entry : kButtons)
        {
            if (entry.canonical && entry.button == button)
            {
                return entry.name;
            }
        }

        return {};
    }

    unsigned PadChordFromNames(const std::string& text, std::vector<std::string>& unknown)
    {
        unsigned chord = PadNone;

        size_t start = 0;
        while (start <= text.size())
        {
            const size_t plus = text.find('+', start);
            const size_t end = plus == std::string::npos ? text.size() : plus;

            const std::string part = Trim(text.substr(start, end - start));

            if (!part.empty())
            {
                const unsigned button = PadButtonFromName(part);

                if (button == PadNone)
                {
                    unknown.push_back(part);
                }
                else
                {
                    chord |= button;
                }
            }

            if (plus == std::string::npos)
            {
                break;
            }

            start = plus + 1;
        }

        return chord;
    }

    unsigned StickDirections(const int x, const int y, const unsigned previous,
                             const unsigned upBit, const unsigned downBit,
                             const unsigned leftBit, const unsigned rightBit)
    {
        // Two thirds of the way out to count as pressed, one third to let go
        // again. XInput's own resting deadzone is far lower, but that one is
        // meant for walking a character, not for stepping through a list.
        const int press = 22000;
        const int release = 11000;

        auto held = [&](const int value, const int sign, const unsigned bit)
        {
            const int reach = value * sign;
            const bool was = (previous & bit) != 0;

            return reach >= (was ? release : press);
        };

        unsigned mask = 0;

        if (held(y, 1, upBit))     { mask |= upBit; }
        if (held(y, -1, downBit))  { mask |= downBit; }
        if (held(x, -1, leftBit))  { mask |= leftBit; }
        if (held(x, 1, rightBit))  { mask |= rightBit; }

        return mask;
    }

    PadShield ShieldFor(const std::vector<unsigned>& chords)
    {
        const unsigned leftDirections = PadLStickUp | PadLStickDown | PadLStickLeft | PadLStickRight;
        const unsigned rightDirections = PadRStickUp | PadRStickDown | PadRStickLeft | PadRStickRight;

        PadShield shield;

        for (const unsigned chord : chords)
        {
            // The low sixteen bits are XInput's own buttons; the stick
            // directions above them are ours and exist in no pad state.
            shield.buttons |= chord & 0xFFFFu;
            shield.leftStick = shield.leftStick || (chord & leftDirections) != 0;
            shield.rightStick = shield.rightStick || (chord & rightDirections) != 0;
        }

        return shield;
    }

    unsigned HideFromGame(const unsigned buttons, const unsigned shield, const bool open, unsigned& swallowed)
    {
        if (open)
        {
            swallowed = buttons & shield;
            return buttons & ~shield;
        }

        // Whatever has been let go since is the game's again.
        swallowed &= buttons;
        return buttons & ~swallowed;
    }
}
