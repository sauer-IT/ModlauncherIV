// Prueft die Menuelogik ohne Spiel.
//
// Das Menue haengt bewusst an keiner Spielfunktion, also laesst es sich hier
// vollstaendig durchspielen. Ein Navigationsfehler faellt damit in
// Millisekunden auf statt nach Spielstart, Ladebildschirm und Tastendruck.

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

    /// Sammelt das Gezeichnete als Text, damit sich die Ausgabe pruefen laesst.
    class TextRenderer final : public mliv::IMenuRenderer
    {
    public:
        std::string output;

        void beginFrame(int) override { output.clear(); }
        void drawTitle(const std::string& text) override { output += "[" + text + "]\n"; }

        void drawItem(const std::string& label, const std::string& value, const bool highlighted) override
        {
            output += highlighted ? "> " : "  ";
            output += label;
            if (!value.empty())
            {
                output += " : " + value;
            }

            output += "\n";
        }

        void endFrame() override {}
    };

    bool Contains(const std::string& haystack, const std::string& needle)
    {
        return haystack.find(needle) != std::string::npos;
    }
}

int main()
{
    std::printf("\n== Menuelogik ==\n");

    bool godmode = false;
    int weather = 0;
    int actionsRun = 0;
    int choiceCalls = 0;

    auto vehicles = std::make_shared<mliv::Menu>("Fahrzeuge");
    vehicles->add({"Reparieren", mliv::ItemKind::Action, [&] { ++actionsRun; }});
    vehicles->add({"Unkaputtbar", mliv::ItemKind::Toggle, nullptr, &godmode});

    auto root = std::make_shared<mliv::Menu>("Modlauncher IV");
    root->add({"-- Spieler --", mliv::ItemKind::Label});
    root->add({"Godmode", mliv::ItemKind::Toggle, nullptr, &godmode});

    mliv::MenuItem weatherItem;
    weatherItem.label = "Wetter";
    weatherItem.kind = mliv::ItemKind::Choice;
    weatherItem.choices = {"Sonnig", "Regen", "Nebel"};
    weatherItem.choiceIndex = &weather;
    weatherItem.onChoice = [&](int) { ++choiceCalls; };
    root->add(weatherItem);

    mliv::MenuItem sub;
    sub.label = "Fahrzeuge";
    sub.kind = mliv::ItemKind::Submenu;
    sub.submenu = vehicles;
    root->add(sub);

    mliv::MenuController menu(root);
    TextRenderer renderer;

    // --- Sichtbarkeit ---------------------------------------------------
    Check(!menu.visible(), "startet geschlossen");
    menu.draw(renderer);
    Check(renderer.output.empty(), "zeichnet nichts, solange es zu ist");

    menu.handle(mliv::MenuInput::Toggle);
    Check(menu.visible(), "Menuetaste oeffnet");

    // --- Labels werden uebersprungen -------------------------------------
    Check(root->selected() == 1, "Auswahl steht nicht auf der Ueberschrift");

    menu.draw(renderer);
    Check(Contains(renderer.output, "[Modlauncher IV]"), "Titel wird gezeichnet");
    Check(Contains(renderer.output, "> Godmode : AUS"), "Auswahl ist hervorgehoben");

    // --- Umlauf am Rand ---------------------------------------------------
    menu.handle(mliv::MenuInput::Up);
    Check(root->selected() == 3, "nach oben vom ersten Eintrag laeuft ans Ende um");
    Check(root->items()[3].label == "Fahrzeuge", "und landet nicht auf der Ueberschrift");

    menu.handle(mliv::MenuInput::Down);
    Check(root->selected() == 1, "nach unten vom letzten Eintrag laeuft an den Anfang");

    // --- Toggle -----------------------------------------------------------
    menu.handle(mliv::MenuInput::Select);
    Check(godmode, "Auswaehlen schaltet den Schalter ein");
    menu.draw(renderer);
    Check(Contains(renderer.output, "Godmode : AN"), "der Zustand steht im Menue");

    menu.handle(mliv::MenuInput::Select);
    Check(!godmode, "nochmal Auswaehlen schaltet zurueck");

    // --- Choice -----------------------------------------------------------
    menu.handle(mliv::MenuInput::Down);
    Check(root->selected() == 2, "Wetter ist ausgewaehlt");

    menu.handle(mliv::MenuInput::Right);
    Check(weather == 1 && choiceCalls == 1, "rechts erhoeht und meldet es");
    menu.handle(mliv::MenuInput::Left);
    menu.handle(mliv::MenuInput::Left);
    Check(weather == 2, "links laeuft unter null hinweg ans Ende um");

    // --- Untermenue -------------------------------------------------------
    menu.handle(mliv::MenuInput::Down);
    menu.handle(mliv::MenuInput::Select);
    Check(menu.depth() == 1, "Untermenue wurde betreten");
    Check(menu.active().title() == "Fahrzeuge", "und es ist das richtige");

    menu.handle(mliv::MenuInput::Select);
    Check(actionsRun == 1, "Aktion im Untermenue wurde ausgefuehrt");

    menu.handle(mliv::MenuInput::Back);
    Check(menu.depth() == 0, "Zurueck fuehrt eine Ebene hoeher");
    Check(menu.visible(), "und schliesst dabei nicht gleich alles");

    // --- Zurueck auf der Wurzel schliesst ----------------------------------
    menu.handle(mliv::MenuInput::Back);
    Check(!menu.visible(), "Zurueck auf der Wurzel schliesst das Menue");

    // --- Ein Menue nur aus Labels darf sich nicht aufhaengen ---------------
    auto onlyLabels = std::make_shared<mliv::Menu>("Nur Text");
    onlyLabels->add({"a", mliv::ItemKind::Label});
    onlyLabels->add({"b", mliv::ItemKind::Label});
    mliv::MenuController stuck(onlyLabels);
    stuck.handle(mliv::MenuInput::Toggle);
    stuck.handle(mliv::MenuInput::Down);
    stuck.handle(mliv::MenuInput::Up);
    Check(true, "Menue ohne anwaehlbare Eintraege haengt sich nicht auf");

    std::printf("\n%s\n", std::string(46, '=').c_str());
    std::printf(" %d bestanden, %d fehlgeschlagen\n", g_passed, g_failed);
    std::printf("%s\n\n", std::string(46, '=').c_str());

    return g_failed == 0 ? 0 : 1;
}
