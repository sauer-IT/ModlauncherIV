// Modlauncher IV - Trainer, Stufe T2a
//
// Die einzige Uebersetzungseinheit, die das IV-SDK einbindet. Das ist keine
// Bequemlichkeit: IVSDK.cpp definiert Globals und ein eigenes DllMain. Wuerde
// es aus zwei .cpp-Dateien eingebunden, gaebe es doppelte Symbole. Alles
// SDK-Beruehrende bleibt deshalb hier, und die Menuelogik in Menu.cpp kennt das
// Spiel weiterhin nicht.

// Fremde Header sollen unsere Warnungsstufe nicht ausloesen - wir bauen mit
// /W4 /WX, und an fremdem Code haben wir nichts zu korrigieren.
#pragma warning(push, 0)
#include "IVSDK.cpp"
#pragma warning(pop)

#include "core/Log.h"
#include "game/GameVersion.h"
#include "menu/Menu.h"

#include <memory>

namespace
{
    // ------------------------------------------------------------ Zustand

    std::shared_ptr<mliv::Menu> g_root;
    std::unique_ptr<mliv::MenuController> g_menu;


    // ------------------------------------------------------------ Zeichnen

    /// Zeichnet mit den Text-Natives des Spiels.
    ///
    /// Bewusst kein D3D9-Hook: der waere huebscher, beisst sich aber mit DXVK,
    /// Overlays und anderen Mods - eine der haeufigsten Absturzursachen in
    /// dieser Szene. Die Text-Natives sehen schlichter aus und halten dafuer.
    class NativeMenuRenderer final : public mliv::IMenuRenderer
    {
    public:
        void beginFrame(const int itemCount) override
        {
            // Kopfzeile plus Eintraege, beides in Zeilenhoehen gerechnet.
            const float bodyHeight = static_cast<float>(itemCount) * kLine;
            const float total = kTitleHeight + bodyHeight + 2.0f * kPadding;

            titleY_ = kTop + kPadding;
            y_ = titleY_ + kTitleHeight;

            // Der Kasten zuerst: was spaeter gezeichnet wird, liegt darueber.
            FillRect(kLeft, kTop, kWidth, total, 0, 0, 0, 190);

            // Schmaler Streifen als Kopf, damit der Titel sich absetzt.
            FillRect(kLeft, kTop, kWidth, kTitleHeight + kPadding, 158, 87, 16, 230);
        }

        void drawTitle(const std::string& text) override
        {
            SetupText(255, 255, 255, 255, kTitleScale);
            Scripting::DISPLAY_TEXT_WITH_LITERAL_STRING(
                kLeft + kPadding, titleY_, "STRING", text.c_str());
        }

        void drawItem(const std::string& label, const std::string& value, const bool highlighted) override
        {
            if (highlighted)
            {
                FillRect(kLeft, y_ - kPadding * 0.4f, kWidth, kLine, 224, 160, 74, 210);
            }

            const unsigned tone = highlighted ? 20u : 235u;
            SetupText(tone, tone, tone, 255, kItemScale);
            Scripting::DISPLAY_TEXT_WITH_LITERAL_STRING(
                kLeft + kPadding, y_, "STRING", label.c_str());

            if (!value.empty())
            {
                SetupText(tone, tone, tone, 255, kItemScale);

                // Rechtsbuendig am rechten Rand des Kastens. SET_TEXT_WRAP legt
                // fest, wo "rechts" liegt - ohne das richtet sich der Text am
                // Bildschirmrand aus statt am Menue.
                Scripting::SET_TEXT_RIGHT_JUSTIFY(1);
                Scripting::SET_TEXT_WRAP(kLeft, kLeft + kWidth - kPadding);
                Scripting::DISPLAY_TEXT_WITH_LITERAL_STRING(
                    kLeft + kPadding, y_, "STRING", value.c_str());

                // Beides sofort zuruecknehmen: sonst erbt das Label des
                // naechsten Eintrags unsere Umbruchgrenzen und wird beschnitten.
                Scripting::SET_TEXT_RIGHT_JUSTIFY(0);
                Scripting::SET_TEXT_WRAP(0.0f, 1.0f);
            }

            y_ += kLine;
        }

        /// Textzustand zuruecksetzen.
        ///
        /// Die SET_TEXT_*-Natives wirken global und bleiben stehen, bis jemand
        /// sie wieder aendert. Wer danach zeichnet - Handy, HUD, Untertitel -
        /// erbt unsere Schriftart, Skalierung, Farbe und vor allem unsere
        /// Umbruchgrenzen. Beim Handy sah man das als verzerrte Darstellung,
        /// solange das Menue offen war.
        ///
        /// Das Spiel setzt vieles davon selbst, aber eben nicht alles und nicht
        /// zuverlaessig. Wer den Zustand anfasst, raeumt ihn auf.
        void endFrame() override
        {
            Scripting::SET_TEXT_RIGHT_JUSTIFY(0);
            Scripting::SET_TEXT_CENTRE(0);
            Scripting::SET_TEXT_WRAP(0.0f, 1.0f);
            Scripting::SET_TEXT_FONT(0);
            Scripting::SET_TEXT_SCALE(1.0f, 1.0f);
            Scripting::SET_TEXT_COLOUR(255, 255, 255, 255);
            Scripting::SET_TEXT_DROPSHADOW(0, 0, 0, 0, 0);
            Scripting::SET_TEXT_PROPORTIONAL(1);
        }

    private:
        // Linke obere Ecke des Menues, in Bildanteilen (0..1).
        static constexpr float kLeft = 0.025f;
        static constexpr float kTop = 0.12f;
        static constexpr float kWidth = 0.235f;

        static constexpr float kLine = 0.026f;
        static constexpr float kTitleHeight = 0.034f;
        static constexpr float kPadding = 0.008f;

        static constexpr float kTitleScale = 0.34f;
        static constexpr float kItemScale = 0.26f;

        float titleY_ = kTop;
        float y_ = kTop;

        /// DRAW_RECT in GTA IV nimmt MITTELPUNKT und GROESSE, nicht zwei Ecken.
        /// Die Parameternamen im SDK legen anderes nahe; mit Ecken gefuettert
        /// landen die Flaechen sichtbar daneben. Diese Funktion rechnet von
        /// links-oben plus Groesse um, weil sich Layout so denken laesst.
        static void FillRect(const float left, const float top,
                             const float width, const float height,
                             const int r, const int g, const int b, const int a)
        {
            Scripting::DRAW_RECT(left + width * 0.5f, top + height * 0.5f,
                                 width, height, r, g, b, a);
        }

        static void SetupText(const unsigned r, const unsigned g, const unsigned b,
                              const unsigned a, const float scale)
        {
            Scripting::SET_TEXT_FONT(0);
            Scripting::SET_TEXT_SCALE(scale, scale * 1.6f);
            Scripting::SET_TEXT_COLOUR(r, g, b, a);
            Scripting::SET_TEXT_DROPSHADOW(0, 0, 0, 0, 0);
            Scripting::SET_TEXT_CENTRE(0);
            Scripting::SET_TEXT_PROPORTIONAL(1);
        }
    };

    NativeMenuRenderer g_renderer;

    // ------------------------------------------------------------- Eingabe

    struct Key
    {
        int code;
        mliv::MenuInput input;
        bool wasDown = false;
    };

    // Numblock, wie es in dieser Szene ueblich ist. Zusaetzlich die Pfeiltasten,
    // damit es auch ohne Zehnerblock bedienbar bleibt.
    Key g_keys[] = {
        { VK_F7,       mliv::MenuInput::Toggle },
        { VK_NUMPAD0,  mliv::MenuInput::Back   },
        { VK_NUMPAD8,  mliv::MenuInput::Up     },
        { VK_NUMPAD2,  mliv::MenuInput::Down   },
        { VK_NUMPAD4,  mliv::MenuInput::Left   },
        { VK_NUMPAD6,  mliv::MenuInput::Right  },
        { VK_NUMPAD5,  mliv::MenuInput::Select },
        { VK_UP,       mliv::MenuInput::Up     },
        { VK_DOWN,     mliv::MenuInput::Down   },
        { VK_LEFT,     mliv::MenuInput::Left   },
        { VK_RIGHT,    mliv::MenuInput::Right  },
        { VK_RETURN,   mliv::MenuInput::Select },
        { VK_BACK,     mliv::MenuInput::Back   },
    };

    /// Nur Flanken melden, nicht gehaltene Tasten.
    ///
    /// Der Zeichen-Event laeuft pro Bild. Ohne Flankenerkennung raste ein
    /// einziger Tastendruck bei 60 Bildern je Sekunde sechzig Eintraege weiter -
    /// das Menue waere unbedienbar.
    void PollInput()
    {
        for (Key& key : g_keys)
        {
            const bool down = (GetAsyncKeyState(key.code) & 0x8000) != 0;

            if (down && !key.wasDown)
            {
                g_menu->handle(key.input);
            }

            key.wasDown = down;
        }
    }

    // -------------------------------------------------------- Spielzugriff

    /// Die Spielfunktionen leben hier und nicht in einer eigenen Datei, weil
    /// das IV-SDK nur in diese eine Uebersetzungseinheit darf. Die Menuelogik
    /// bleibt davon unberuehrt - sie kennt nur Lambdas.
    namespace game
    {
        Scripting::Player LocalPlayer()
        {
            return static_cast<Scripting::Player>(Scripting::GET_PLAYER_ID());
        }

        /// Der Ped des Spielers. 0, wenn gerade keiner da ist - etwa im Menue,
        /// beim Laden oder in einer Zwischensequenz. Jeder Aufrufer muss das
        /// pruefen: ein Native mit ungueltigem Handle ist kein harmloser
        /// Fehlschlag.
        Scripting::Ped LocalPed()
        {
            const Scripting::Player player = LocalPlayer();
            if (!Scripting::IS_PLAYER_PLAYING(player))
            {
                return 0;
            }

            Scripting::Ped ped = 0;
            Scripting::GET_PLAYER_CHAR(player, &ped);

            return Scripting::DOES_CHAR_EXIST(ped) ? ped : 0;
        }

        void RestoreHealth()
        {
            const Scripting::Ped ped = LocalPed();
            if (ped != 0)
            {
                // Das Spiel begrenzt selbst auf das Maximum der Figur; hoeher
                // anzusetzen ist ungefaehrlich und erspart uns die Frage, wie
                // hoch das Maximum gerade ist.
                Scripting::SET_CHAR_HEALTH(ped, 200);
            }
        }

        void RestoreArmour()
        {
            const Scripting::Ped ped = LocalPed();
            if (ped != 0)
            {
                Scripting::ADD_ARMOUR_TO_CHAR(ped, 100);
            }
        }

        void AddMoney(const int amount)
        {
            Scripting::ADD_SCORE(LocalPlayer(), amount);
        }

        void SetWantedLevel(const int level)
        {
            const Scripting::Player player = LocalPlayer();
            Scripting::ALTER_WANTED_LEVEL(player, static_cast<unsigned>(level));

            // Ohne das uebernimmt das Spiel die Aenderung erst irgendwann -
            // der Stern im HUD bliebe stehen und man haelt es fuer kaputt.
            Scripting::APPLY_WANTED_LEVEL_CHANGE_NOW(player);
        }
    }

    // -------------------------------------------------------------- Zustand



    bool g_godmode = false;
    bool g_neverWanted = false;
    int  g_wantedChoice = 0;
    int  g_moneyChoice = 1;

    const int kMoneyAmounts[] = {1000, 10000, 100000, 1000000};

    /// Laeuft jeden Frame.
    ///
    /// Godmode und "nie gesucht" werden hier immer wieder gesetzt, nicht nur
    /// beim Umschalten. Das Spiel setzt beides bei Respawn, Zwischensequenzen
    /// und Missionswechseln zurueck - ein einmal gesetzter Schalter hoerte
    /// stillschweigend auf zu wirken, und der Nutzer haelt den Trainer fuer
    /// kaputt statt das Spiel fuer eigenwillig.
    void EnforceToggles()
    {
        const Scripting::Player player = game::LocalPlayer();
        if (!Scripting::IS_PLAYER_PLAYING(player))
        {
            return;
        }

        if (g_godmode)
        {
            const Scripting::Ped ped = game::LocalPed();
            if (ped != 0)
            {
                Scripting::SET_CHAR_INVINCIBLE(ped, 1);
                Scripting::SET_PLAYER_INVINCIBLE(player, 1);
            }
        }

        if (g_neverWanted)
        {
            Scripting::CLEAR_WANTED_LEVEL(player);
        }
    }

    // --------------------------------------------------------------- Menue

    void BuildMenu()
    {
        g_root = std::make_shared<mliv::Menu>("Modlauncher IV");

        // --- Spieler ---
        g_root->add({"-- Spieler --", mliv::ItemKind::Label});

        // Beim Ausschalten muss die Unverwundbarkeit aktiv zurueckgenommen
        // werden. EnforceToggles setzt sie nur noch nicht mehr - abschalten
        // tut es nichts, und der Spieler bliebe unsterblich.
        g_root->add({"Godmode", mliv::ItemKind::Toggle, [] {
            if (!g_godmode)
            {
                const Scripting::Ped ped = game::LocalPed();
                if (ped != 0)
                {
                    Scripting::SET_CHAR_INVINCIBLE(ped, 0);
                    Scripting::SET_PLAYER_INVINCIBLE(game::LocalPlayer(), 0);
                }
            }
        }, &g_godmode});

        g_root->add({"Leben auffuellen", mliv::ItemKind::Action, game::RestoreHealth});
        g_root->add({"Panzerung auffuellen", mliv::ItemKind::Action, game::RestoreArmour});

        // --- Fahndung ---
        g_root->add({"-- Fahndung --", mliv::ItemKind::Label});

        mliv::MenuItem wanted;
        wanted.label = "Fahndungslevel";
        wanted.kind = mliv::ItemKind::Choice;
        wanted.choices = {"0", "1", "2", "3", "4", "5", "6"};
        wanted.choiceIndex = &g_wantedChoice;
        wanted.onChoice = [](const int level) { game::SetWantedLevel(level); };
        g_root->add(wanted);

        g_root->add({"Nie gesucht", mliv::ItemKind::Toggle, nullptr, &g_neverWanted});

        // --- Geld ---
        g_root->add({"-- Geld --", mliv::ItemKind::Label});

        mliv::MenuItem money;
        money.label = "Betrag";
        money.kind = mliv::ItemKind::Choice;
        money.choices = {"1.000", "10.000", "100.000", "1.000.000"};
        money.choiceIndex = &g_moneyChoice;
        g_root->add(money);

        g_root->add({"Geld geben", mliv::ItemKind::Action, [] {
            game::AddMoney(kMoneyAmounts[g_moneyChoice]);
            mliv::LogLine("Geld gegeben: %d", kMoneyAmounts[g_moneyChoice]);
        }});

        g_menu = std::make_unique<mliv::MenuController>(g_root);
    }

    /// Spiellogik. Laeuft nur, wenn das Spiel seine Skripte abarbeitet.
    ///
    /// Muss hier stehen und nicht im Zeichen-Event: processScriptsEvent setzt
    /// vorher CTheScripts::m_pCurrentThread, drawingEvent nicht. Natives
    /// brauchen diesen Script-Kontext. Dazu kommt, dass drawingEvent laut SDK
    /// auch im Menue und im Ladebildschirm laeuft - dort gibt es noch gar keine
    /// Skript-Maschine, und ein GET_PLAYER_ID beendet das Spiel wortlos.
    void OnScript()
    {
        PollInput();

        // Auch wenn das Menue zu ist: die Schalter sollen wirken, nicht nur
        // solange man hinsieht.
        EnforceToggles();

        // Gezeichnet wird hier, nicht in drawingEvent.
        //
        // Die Skripte des Spiels zeichnen ihr HUD selbst aus dem Script-Tick;
        // DRAW_RECT und DISPLAY_TEXT sind dafuer gemacht und landen dann in der
        // HUD-Phase. Aus drawingEvent gerufen laufen sie mitten in einer
        // Renderphase - und wenn dabei das Renderziel des Handys gebunden ist,
        // zeichnen sie in dessen Bildschirm hinein. Genau so sah es aus.
        if (Scripting::IS_PAUSE_MENU_ACTIVE() == 0)
        {
            g_menu->draw(g_renderer);
        }
    }
}

/// Ruft das SDK auf, nachdem es sich eingehaengt hat.
///
/// Das SDK prueft vorher selbst die Spielversion und hookt auf einer
/// unbekannten gar nichts - diese Funktion wird dann nie aufgerufen. Unsere
/// eigene Pruefung bleibt trotzdem: sie steht im Log, und sie haelt die
/// Bedingung dort fest, wo unser Code sie braucht.
void plugin::gameStartupEvent()
{
    wchar_t self[MAX_PATH]{};
    GetModuleFileNameW(GetModuleHandleW(L"ModlauncherIV-Trainer.asi"), self, MAX_PATH);
    mliv::LogOpen(self);

    mliv::LogLine("Modlauncher IV Trainer, Stufe T2a");

    const mliv::GameInfo game = mliv::DetectGame();
    mliv::LogLine("Version: %ls (%s)",
                  game.raw.empty() ? L"(nicht lesbar)" : game.raw.c_str(),
                  mliv::Describe(game.version));

    if (!mliv::IsSupported(game.version))
    {
        mliv::LogLine("ABBRUCH: Diese Version wird nicht unterstuetzt.");
        return;
    }

    BuildMenu();

    // Nur dieses eine Event: Eingabe, Schalter und Zeichnen laufen alle im
    // Script-Kontext. Siehe OnScript.
    plugin::processScriptsEvent::Add(OnScript);

    mliv::LogLine("Menue bereit. F7 oeffnet, Numblock oder Pfeiltasten bedienen.");
}

/// Wird beim Entladen gerufen. Das SDK verlangt die Funktion, auch wenn sie
/// wenig zu tun hat - ohne sie bleibt ein unaufgeloestes Symbol.
void plugin::gameShutdownEvent()
{
    mliv::LogLine("Spiel wird beendet.");
    mliv::LogClose();
}

