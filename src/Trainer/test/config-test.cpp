// Checks reading the configuration without the game.
//
// The configuration deliberately depends on no platform header - so it can be
// played through completely here. That pays off especially because the bugs in
// it are silent: a misspelled key shows up in the game as "the key does
// nothing", and then people go looking in the game instead of in the file.

#include <cstdio>
#include <string>
#include <vector>

#include "../core/Config.h"

namespace
{
    int g_passed = 0;
    int g_failed = 0;

    void Check(const bool condition, const char* label)
    {
        if (condition)
        {
            std::printf("  PASS  %s\n", label);
            ++g_passed;
        }
        else
        {
            std::printf("  FAIL  %s\n", label);
            ++g_failed;
        }
    }

    bool Has(const std::vector<int>& codes, const int code)
    {
        for (const int c : codes)
        {
            if (c == code)
            {
                return true;
            }
        }

        return false;
    }

    bool Mentions(const std::vector<std::string>& lines, const std::string& fragment)
    {
        for (const std::string& line : lines)
        {
            if (line.find(fragment) != std::string::npos)
            {
                return true;
            }
        }

        return false;
    }
}

int main()
{
    std::printf("\n== Key names ==\n");

    Check(mliv::KeyCodeFromName("F7") == 0x76, "F7 is recognised");
    Check(mliv::KeyCodeFromName("f7") == 0x76, "lower case is recognised");
    Check(mliv::KeyCodeFromName("  F7  ") == 0x76, "surrounding spaces do not matter");
    Check(mliv::KeyCodeFromName("NUM8") == 0x68, "NUM8 is recognised");
    Check(mliv::KeyCodeFromName("NUMPAD8") == 0x68, "the NUMPAD spelling works too");
    Check(mliv::KeyCodeFromName("UP") == 0x26, "UP is recognised");
    Check(mliv::KeyCodeFromName("K") == 'K', "single letters are their own code");
    Check(mliv::KeyCodeFromName("k") == 'K', "in lower case as well");
    Check(mliv::KeyCodeFromName("5") == '5', "single digits likewise");

    // The case that matters most: whatever is not recognised has to come back as
    // 0, so the caller keeps its default instead of binding a random key.
    Check(mliv::KeyCodeFromName("NUM 8") == 0, "NUM 8 with a space is not recognised");
    Check(mliv::KeyCodeFromName("Sandwich") == 0, "nonsense is not recognised");
    Check(mliv::KeyCodeFromName("") == 0, "an empty name is not recognised");

    std::printf("\n== Reading ==\n");
    {
        mliv::Config config;
        config.parse(
            "# a comment\n"
            "; another one\n"
            "\n"
            "[Keys]\n"
            "Menu = F8\n"
            "Up   = NUM8, UP\n"
            "\n"
            "[Menu]\n"
            "Left = 0.05\n"
            "\n"
            "[Log]\n"
            "Enabled = no\n");

        Check(Has(config.keys("Menu"), 0x77), "the menu key comes from the file");
        Check(config.keys("Up").size() == 2, "two keys for one action");
        Check(Has(config.keys("Up"), 0x68) && Has(config.keys("Up"), 0x26),
              "and both are the right ones");
        Check(config.keys("Down").empty(), "what is not in the file stays empty");

        Check(config.number("Menu.Left", 99.0f) > 0.049f &&
              config.number("Menu.Left", 99.0f) < 0.051f, "a number is read");
        Check(config.number("Menu.Top", 0.12f) > 0.119f, "a missing number falls back to the default");

        Check(config.flag("Log.Enabled", true) == false, "no is read as false");
        Check(config.flag("Log.Missing", true), "a missing switch falls back to the default");

        Check(config.problems().empty(), "a clean file has nothing to report");
    }

    // A byte order mark is invisible in every editor that writes one, and it
    // sits in front of the first character - so the first line stops being a
    // comment and starts being a complaint. Found when the trainer objected to
    // its own template after the file had been through a PowerShell one-liner.
    {
        mliv::Config config;
        config.parse(
            "\xEF\xBB\xBF# sauer\n"
            "[Log]\n"
            "Enabled = no\n");

        Check(config.problems().empty(), "a file with a byte order mark reports nothing");
        Check(config.flag("Log.Enabled", true) == false, "and is read like any other");
    }

    std::printf("\n== Upper and lower case ==\n");
    {
        mliv::Config config;
        config.parse("[KEYS]\nMENU = F9\n");

        Check(Has(config.keys("menu"), 0x78), "section and name are case-insensitive");
    }

    std::printf("\n== What can go wrong ==\n");
    {
        mliv::Config config;
        config.parse(
            "[Keys]\n"
            "Menu = Sandwich\n"
            "Up = NUM 8\n"
            "This is not an assignment\n"
            "= without a name\n"
            "[Menu]\n"
            "Left = 0,5\n"
            "Width = wide\n"
            "[Log]\n"
            "Enabled = maybe\n");

        Check(config.keys("Menu").empty(), "an unknown key is not bound");
        Check(config.keys("Up").empty(), "nor is the one with the space");

        // Both messages are there without anybody having queried the affected
        // action. Checked only on query, a typo in an action nobody asks about
        // would stay silent forever.
        Check(Mentions(config.problems(), "Sandwich"), "and it gets reported");
        Check(Mentions(config.problems(), "NUM 8"), "including the one with the space");
        Check(Mentions(config.problems(), "equals sign"), "a line without = is reported");
        Check(Mentions(config.problems(), "empty name"), "an assignment without a name is reported");

        // The comma as a decimal separator is the most likely typo on a European
        // system. Without the check "0,5" would silently come out as 0, and the
        // menu would stick to the left edge.
        Check(config.number("Menu.Left", 0.025f) > 0.024f &&
              config.number("Menu.Left", 0.025f) < 0.026f, "0,5 with a comma is not read as 0");
        Check(Mentions(config.problems(), "number with a dot"), "and the reason is given");

        Check(config.number("Menu.Width", 0.235f) > 0.234f, "text instead of a number falls back");
        Check(config.flag("Log.Enabled", true), "an unclear switch falls back to the default");
        Check(Mentions(config.problems(), "yes or no"), "and says what was expected");
    }

    std::printf("\n== Duplicate lines ==\n");
    {
        mliv::Config config;
        config.parse("[Keys]\nMenu = F7\nMenu = F9\n[Menu]\nLeft = 0.1\nLeft = 0.2\n");

        Check(Has(config.keys("Menu"), 0x78) && config.keys("Menu").size() == 1,
              "for keys the lower line wins");
        Check(config.number("Menu.Left", 0.0f) > 0.19f, "for numbers likewise");
    }

    std::printf("\n== The shipped template ==\n");
    {
        // The template is what every user sees first. A typo in it would go
        // unnoticed by everyone - it looks deliberate.
        mliv::Config config;
        config.parse(mliv::Config::DefaultText());

        Check(config.problems().empty(), "the template reads without complaint");
        Check(Has(config.keys("Menu"), 0x76), "and binds F7 to the menu");
        Check(config.keys("Up").size() == 2, "Up has a numpad key and an arrow key");
        Check(config.keys("Down").size() == 2, "Down too");
        Check(config.keys("Left").size() == 2, "Left too");
        Check(config.keys("Right").size() == 2, "Right too");
        Check(config.keys("Select").size() == 2, "Select too");
        Check(config.keys("Back").size() == 2, "Back too");

        // The flight keys deliberately have only one binding each: they are held
        // rather than tapped, and two keys held at once for the same direction
        // would produce double the speed.
        Check(config.keys("FlyForward").size() == 1 && Has(config.keys("FlyForward"), 'W'),
              "FlyForward is on W");
        Check(Has(config.keys("FlyDown"), 0x11), "FlyDown is on ctrl");
        Check(Has(config.keys("FlyUp"), 0x20), "FlyUp is on the space bar");

        Check(config.flag("Log.Enabled", false), "the log is on by default");
    }

    std::printf("\n%s\n", std::string(46, '=').c_str());
    std::printf(" %d passed, %d failed\n", g_passed, g_failed);
    std::printf("%s\n\n", std::string(46, '=').c_str());

    return g_failed == 0 ? 0 : 1;
}
