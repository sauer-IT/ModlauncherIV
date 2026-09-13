// Checks the controller bindings without the game and without a controller.
//
// Pad.cpp knows no platform header, for the same reason Config.cpp does not:
// button names and chords can then be played through here, in milliseconds,
// instead of by plugging in a pad and starting the game. What actually reads
// the device lives in plugin.cpp and is the one part this cannot cover.

#include <algorithm>
#include <cstdio>
#include <string>
#include <vector>

#include "../core/Config.h"
#include "../core/Pad.h"

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

    void Section(const char* title)
    {
        std::printf("\n== %s ==\n", title);
    }

    bool Held(const unsigned buttons, const unsigned chord)
    {
        return (buttons & chord) == chord;
    }
}

int main()
{
    using namespace mliv;

    Section("Button names");

    Check(PadButtonFromName("A") == PadA, "A is recognised");
    Check(PadButtonFromName("a") == PadA, "lower case is recognised");
    Check(PadButtonFromName("  B  ") == PadB, "surrounding spaces do not matter");
    Check(PadButtonFromName("DPadUp") == PadUp, "DPadUp is recognised");
    Check(PadButtonFromName("L3") == PadLeftStick, "L3 is recognised");
    Check(PadButtonFromName("RB") == PadRightShoulder, "RB is recognised");
    Check(PadButtonFromName("Back") == PadBack, "Back is recognised");

    // Whoever holds a PlayStation pad reads Cross on it, not A.
    Check(PadButtonFromName("Cross") == PadA, "Cross is the same button as A");
    Check(PadButtonFromName("Circle") == PadB, "Circle is the same button as B");
    Check(PadButtonFromName("Triangle") == PadY, "Triangle is the same button as Y");

    Check(PadButtonFromName("nonsense") == PadNone, "nonsense is not recognised");
    Check(PadButtonFromName("") == PadNone, "an empty name is not recognised");
    Check(PadButtonFromName("L 3") == PadNone, "L3 with a space is not recognised");

    Section("Names back");

    Check(PadNameFromButton(PadA) == "a", "A comes back");
    Check(PadNameFromButton(PadLeftStick) == "l3", "the canonical name wins over the alias");
    Check(PadNameFromButton(0x0400) == "", "an unused bit has no name");

    Section("Chords");

    {
        std::vector<std::string> unknown;
        const unsigned chord = PadChordFromNames("L3+R3", unknown);

        Check(chord == (PadLeftStick | PadRightStick), "L3+R3 is both sticks");
        Check(unknown.empty(), "and nothing was left over");
    }

    {
        std::vector<std::string> unknown;
        const unsigned chord = PadChordFromNames("  A  +  B  ", unknown);
        Check(chord == (PadA | PadB), "spaces around the plus do not matter");
    }

    {
        std::vector<std::string> unknown;
        const unsigned chord = PadChordFromNames("A", unknown);
        Check(chord == PadA, "a single button is a chord of one");
    }

    {
        std::vector<std::string> unknown;
        PadChordFromNames("L3+nonsense", unknown);

        Check(unknown.size() == 1, "the unknown half is reported");
        Check(unknown.front() == "nonsense", "by name");
    }

    Section("Reading a configuration");

    {
        Config config;
        config.parse(
            "[Pad]\n"
            "Menu = L3+R3\n"
            "Select = A, Start\n"
            "Up = DPadUp\n");

        const std::vector<unsigned> menu = config.chords("Menu");
        Check(menu.size() == 1, "the menu chord comes from the file");
        Check(!menu.empty() && menu.front() == (PadLeftStick | PadRightStick),
              "and it is both sticks");

        const std::vector<unsigned> select = config.chords("Select");
        Check(select.size() == 2, "a comma gives two alternatives");
        Check(select.size() == 2 && select[0] == PadA && select[1] == PadStart,
              "and both are the right ones");

        Check(config.chords("Back").empty(), "what is not in the file stays empty");
        Check(config.problems().empty(), "a clean file has nothing to report");
    }

    Section("Section and name are case-insensitive");

    {
        Config config;
        config.parse("[PAD]\nMENU = l3+r3\n");

        Check(config.chords("menu").size() == 1, "written in capitals it still binds");
    }

    Section("What can go wrong");

    {
        Config config;
        config.parse("[Pad]\nMenu = L3+nonsense\nSelect = A\n");

        // Half a chord is not the chord that was meant, and the half that is
        // left might be a button the game itself uses.
        Check(config.chords("Menu").empty(), "a chord with an unknown half is not bound");
        Check(!config.problems().empty(), "and it gets reported");
        Check(config.chords("Select").size() == 1, "the other line is unaffected");
    }

    {
        Config config;
        config.parse("[Pad]\nMenu = nonsense\n");
        Check(config.chords("Menu").empty(), "an entirely unknown button binds nothing");
    }

    Section("Enabled is a switch, not an action");

    {
        Config config;
        config.parse("[Pad]\nEnabled = no\nMenu = L3+R3\n");

        Check(!config.flag("Pad.Enabled", true), "the switch is read");
        Check(config.chords("Enabled").empty(), "and not mistaken for a binding");
        Check(config.problems().empty(), "so it reports no unknown button");
        Check(config.chords("Menu").size() == 1, "the binding next to it still works");
    }

    Section("The shipped template");

    {
        Config config;
        config.parse(Config::DefaultText());

        Check(config.problems().empty(), "the template reads without complaint");
        Check(config.flag("Pad.Enabled", false), "the controller is on by default");

        const std::vector<unsigned> menu = config.chords("Menu");
        Check(menu.size() == 1 && menu.front() == (PadLeftStick | PadRightStick),
              "the menu is on L3+R3");

        Check(config.chords("Select").size() == 1 && config.chords("Select").front() == PadA,
              "Select is on A");
        Check(config.chords("Back").size() == 1 && config.chords("Back").front() == PadB,
              "Back is on B");
        Check(config.chords("Up").size() == 1 && config.chords("Up").front() == PadUp,
              "Up is on the d-pad, as it was");

        // The menu key stays F8 as well: the pad is meant to work alongside the
        // keyboard, not instead of it.
        Check(!config.keys("Menu").empty(), "and the keyboard still has its menu key");
    }

    Section("Holding a chord");

    {
        // What PollPad does with the mask, without the pad: a chord counts as
        // held only while every one of its buttons is down.
        const unsigned chord = PadLeftStick | PadRightStick;

        Check(!Held(PadNone, chord), "nothing held is not the chord");
        Check(!Held(PadLeftStick, chord), "half the chord is not the chord");
        Check(Held(chord, chord), "both together are");
        Check(Held(chord | PadA, chord), "an extra button does not break it");
    }

    Section("The sticks as directions");

    {
        Check(PadButtonFromName("LStickUp") == PadLStickUp, "the left stick has an up");
        Check(PadButtonFromName("leftstickdown") == PadLStickDown, "and a down, however it is spelled");
        Check(PadButtonFromName("RStickRight") == PadRStickRight, "the right one too");

        // The bits must not land on top of the buttons: a mask carries both.
        Check((PadLStickUp & 0xFFFF) == 0, "a stick direction is above the button mask");
        Check(PadNameFromButton(PadLStickLeft) == "lstickleft", "and has a name to write back");
    }

    Section("A stick is not a button");

    {
        const short far = 30000;
        const short middling = 15000;
        const short resting = 2000;

        const unsigned up = StickDirections(0, far, 0, PadLStickUp, PadLStickDown,
                                            PadLStickLeft, PadLStickRight);

        Check(up == PadLStickUp, "pushed up is up and nothing else");

        Check(StickDirections(0, static_cast<short>(-far), 0, PadLStickUp, PadLStickDown,
                              PadLStickLeft, PadLStickRight) == PadLStickDown,
              "pulled down is down");

        Check(StickDirections(0, resting, 0, PadLStickUp, PadLStickDown,
                              PadLStickLeft, PadLStickRight) == 0,
              "a stick at rest is nothing");

        // The hysteresis: halfway does not start a press, but it keeps one.
        Check(StickDirections(0, middling, 0, PadLStickUp, PadLStickDown,
                              PadLStickLeft, PadLStickRight) == 0,
              "halfway does not begin a press");

        Check(StickDirections(0, middling, PadLStickUp, PadLStickUp, PadLStickDown,
                              PadLStickLeft, PadLStickRight) == PadLStickUp,
              "but halfway keeps one that had begun");

        Check(StickDirections(0, resting, PadLStickUp, PadLStickUp, PadLStickDown,
                              PadLStickLeft, PadLStickRight) == 0,
              "and letting go all the way ends it");

        // A hand is never exactly on an axis.
        const unsigned corner = StickDirections(far, far, 0, PadLStickUp, PadLStickDown,
                                                PadLStickLeft, PadLStickRight);

        Check((corner & PadLStickUp) != 0 && (corner & PadLStickRight) != 0,
              "a diagonal reports both of its directions");
    }

    Section("What the menu is bound to");

    {
        Config config;
        config.parse(Config::DefaultText());

        const std::vector<unsigned> up = config.chords("Up");

        // The d-pad, deliberately kept: scrolling can bring the phone up and
        // that was judged the lesser annoyance. The stick is there for anyone
        // who decides otherwise, which is what these names are for.
        Check(!up.empty() && up.front() == PadUp, "the template navigates on the d-pad");
        Check(PadButtonFromName("LStickUp") == PadLStickUp, "and the stick is bindable instead");
    }

    std::printf("\n==============================================\n");
    std::printf(" %d passed, %d failed\n", g_passed, g_failed);
    std::printf("==============================================\n");

    return g_failed == 0 ? 0 : 1;
}
