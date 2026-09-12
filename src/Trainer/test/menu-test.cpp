// Checks the menu logic without the game.
//
// The menu deliberately depends on no game function, so it can be played
// through completely here. A navigation bug shows up in milliseconds instead
// of after a game start, a loading screen and a key press.

#include <cstdio>
#include <memory>
#include <sstream>
#include <string>
#include <vector>

#include "../menu/Menu.h"

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

    /// Collects what was drawn as text, so the output can be checked.
    class TextRenderer final : public mliv::IMenuRenderer
    {
    public:
        std::string output;
        std::string footer;

        void beginFrame(int) override { output.clear(); footer.clear(); }
        void drawTitle(const std::string& text) override { output += "[" + text + "]\n"; }

        void drawItem(const std::string& label, const std::string& value,
                      const bool highlighted, const bool selectable) override
        {
            output += highlighted ? "> " : (selectable ? "  " : "- ");
            output += label;
            if (!value.empty())
            {
                output += " : " + value;
            }

            output += "\n";
        }

        void drawFooter(const std::string& text) override { footer = text; }

        void endFrame() override {}
    };

    bool Contains(const std::string& haystack, const std::string& needle)
    {
        return haystack.find(needle) != std::string::npos;
    }
}

int main()
{
    std::printf("\n== Menu logic ==\n");

    bool godmode = false;
    int weather = 0;
    int actionsRun = 0;
    int choiceCalls = 0;

    auto vehicles = std::make_shared<mliv::Menu>("Vehicles");
    vehicles->add({"Repair", mliv::ItemKind::Action, [&] { ++actionsRun; }});
    vehicles->add({"Indestructible", mliv::ItemKind::Toggle, nullptr, &godmode});

    auto root = std::make_shared<mliv::Menu>("Modlauncher IV");
    root->add({"-- Player --", mliv::ItemKind::Label});
    root->add({"Godmode", mliv::ItemKind::Toggle, nullptr, &godmode});

    mliv::MenuItem weatherItem;
    weatherItem.label = "Weather";
    weatherItem.kind = mliv::ItemKind::Choice;
    weatherItem.choices = {"Sunny", "Rain", "Fog"};
    weatherItem.choiceIndex = &weather;
    weatherItem.onChoice = [&](int) { ++choiceCalls; };
    root->add(weatherItem);

    mliv::MenuItem sub;
    sub.label = "Vehicles";
    sub.kind = mliv::ItemKind::Submenu;
    sub.submenu = vehicles;
    root->add(sub);

    mliv::MenuController menu(root);
    TextRenderer renderer;

    // --- Visibility -----------------------------------------------------
    Check(!menu.visible(), "starts closed");
    menu.draw(renderer);
    Check(renderer.output.empty(), "draws nothing while it is closed");

    menu.handle(mliv::MenuInput::Toggle);
    Check(menu.visible(), "the menu key opens it");

    // --- Labels are skipped ----------------------------------------------
    Check(root->selected() == 1, "the selection does not sit on the heading");

    menu.draw(renderer);
    Check(Contains(renderer.output, "[Modlauncher IV]"), "the title is drawn");
    Check(Contains(renderer.output, "> Godmode : OFF"), "the selection is highlighted");
    Check(Contains(renderer.output, "- -- Player --"), "a heading is marked as not selectable");

    // The footer counts what can be picked. Counting the heading as well would
    // make the number disagree with what the eye sees moving.
    Check(renderer.footer == "1 / 3", "the footer counts only selectable entries");

    // --- Wrapping at the ends ---------------------------------------------
    menu.handle(mliv::MenuInput::Up);
    Check(root->selected() == 3, "up from the first entry wraps to the end");
    Check(root->items()[3].label == "Vehicles", "and does not land on the heading");

    menu.handle(mliv::MenuInput::Down);
    Check(root->selected() == 1, "down from the last entry wraps to the start");

    menu.handle(mliv::MenuInput::Up);
    menu.draw(renderer);
    Check(renderer.footer == "3 / 3", "the footer follows the selection");
    menu.handle(mliv::MenuInput::Down);

    // --- Toggle -----------------------------------------------------------
    menu.handle(mliv::MenuInput::Select);
    Check(godmode, "selecting turns the switch on");
    menu.draw(renderer);
    Check(Contains(renderer.output, "Godmode : ON"), "the state shows in the menu");

    menu.handle(mliv::MenuInput::Select);
    Check(!godmode, "selecting again toggles it back");

    // --- Choice -----------------------------------------------------------
    menu.handle(mliv::MenuInput::Down);
    Check(root->selected() == 2, "weather is selected");

    menu.handle(mliv::MenuInput::Right);
    Check(weather == 1 && choiceCalls == 1, "right increases it and reports it");
    menu.handle(mliv::MenuInput::Left);
    menu.handle(mliv::MenuInput::Left);
    Check(weather == 2, "left wraps past zero to the end");

    // --- Submenu ----------------------------------------------------------
    menu.handle(mliv::MenuInput::Down);
    menu.handle(mliv::MenuInput::Select);
    Check(menu.depth() == 1, "the submenu was entered");
    Check(menu.active().title() == "Vehicles", "and it is the right one");

    menu.handle(mliv::MenuInput::Select);
    Check(actionsRun == 1, "the action in the submenu ran");

    menu.handle(mliv::MenuInput::Back);
    Check(menu.depth() == 0, "back moves one level up");
    Check(menu.visible(), "and does not close everything at once");

    // --- Back at the root closes -------------------------------------------
    menu.handle(mliv::MenuInput::Back);
    Check(!menu.visible(), "back at the root closes the menu");

    // --- A menu of labels only must not hang -------------------------------
    auto onlyLabels = std::make_shared<mliv::Menu>("Text only");
    onlyLabels->add({"a", mliv::ItemKind::Label});
    onlyLabels->add({"b", mliv::ItemKind::Label});
    mliv::MenuController stuck(onlyLabels);
    stuck.handle(mliv::MenuInput::Toggle);
    stuck.handle(mliv::MenuInput::Down);
    stuck.handle(mliv::MenuInput::Up);
    Check(true, "a menu without selectable entries does not hang");

    std::printf("\n%s\n", std::string(46, '=').c_str());
    std::printf(" %d passed, %d failed\n", g_passed, g_failed);
    std::printf("%s\n\n", std::string(46, '=').c_str());

    return g_failed == 0 ? 0 : 1;
}
