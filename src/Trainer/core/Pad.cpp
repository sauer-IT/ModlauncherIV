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
            unsigned short button;
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
        };
    }

    unsigned short PadButtonFromName(const std::string& name)
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

    std::string PadNameFromButton(const unsigned short button)
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

    unsigned short PadChordFromNames(const std::string& text, std::vector<std::string>& unknown)
    {
        unsigned short chord = PadNone;

        size_t start = 0;
        while (start <= text.size())
        {
            const size_t plus = text.find('+', start);
            const size_t end = plus == std::string::npos ? text.size() : plus;

            const std::string part = Trim(text.substr(start, end - start));

            if (!part.empty())
            {
                const unsigned short button = PadButtonFromName(part);

                if (button == PadNone)
                {
                    unknown.push_back(part);
                }
                else
                {
                    chord = static_cast<unsigned short>(chord | button);
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
}
