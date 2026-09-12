// Modlauncher IV - Trainer, Stufe T1
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

    bool g_demoToggle = false;
    int  g_demoChoice = 0;

    // ------------------------------------------------------------ Zeichnen

    /// Zeichnet mit den Text-Natives des Spiels.
    ///
    /// Bewusst kein D3D9-Hook: der waere huebscher, beisst sich aber mit DXVK,
    /// Overlays und anderen Mods - eine der haeufigsten Absturzursachen in
    /// dieser Szene. Die Text-Natives sehen schlichter aus und halten dafuer.
    class NativeMenuRenderer final : public mliv::IMenuRenderer
    {
    public:
        void beginFrame() override
        {
            y_ = kTop;

            // Hintergrund, damit der Text auf hellen Szenen lesbar bleibt.
            Scripting::DRAW_RECT(kLeft - 0.01f, kTop - 0.02f,
                                 kLeft + kWidth, kTop + kHeight,
                                 0, 0, 0, 170);
        }

        void drawTitle(const std::string& text) override
        {
            SetupText(255, 255, 255, 255, 0.36f);
            Scripting::DISPLAY_TEXT_WITH_LITERAL_STRING(kLeft, y_, "STRING", text.c_str());
            y_ += kLineHeight * 1.4f;
        }

        void drawItem(const std::string& label, const std::string& value, const bool highlighted) override
        {
            if (highlighted)
            {
                Scripting::DRAW_RECT(kLeft - 0.008f, y_ - 0.004f,
                                     kLeft + kWidth - 0.012f, y_ + kLineHeight - 0.006f,
                                     200, 120, 30, 200);
            }

            SetupText(255, 255, 255, 255, 0.30f);
            Scripting::DISPLAY_TEXT_WITH_LITERAL_STRING(kLeft, y_, "STRING", label.c_str());

            if (!value.empty())
            {
                SetupText(220, 220, 220, 255, 0.30f);
                Scripting::DISPLAY_TEXT_WITH_LITERAL_STRING(
                    kLeft + kWidth - 0.10f, y_, "STRING", value.c_str());
            }

            y_ += kLineHeight;
        }

        void endFrame() override {}

    private:
        static constexpr float kLeft = 0.03f;
        static constexpr float kTop = 0.10f;
        static constexpr float kWidth = 0.26f;
        static constexpr float kHeight = 0.42f;
        static constexpr float kLineHeight = 0.028f;

        float y_ = kTop;

        static void SetupText(const unsigned r, const unsigned g, const unsigned b,
                              const unsigned a, const float scale)
        {
            Scripting::SET_TEXT_FONT(0);
            Scripting::SET_TEXT_SCALE(scale, scale * 1.5f);
            Scripting::SET_TEXT_COLOUR(r, g, b, a);
            Scripting::SET_TEXT_DROPSHADOW(1, 0, 0, 0, 220);
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

    // --------------------------------------------------------------- Menue

    void BuildMenu()
    {
        g_root = std::make_shared<mliv::Menu>("Modlauncher IV");

        g_root->add({"-- Stufe T1 --", mliv::ItemKind::Label});

        g_root->add({"Beispielschalter", mliv::ItemKind::Toggle, nullptr, &g_demoToggle});

        mliv::MenuItem choice;
        choice.label = "Beispielauswahl";
        choice.kind = mliv::ItemKind::Choice;
        choice.choices = {"Eins", "Zwei", "Drei"};
        choice.choiceIndex = &g_demoChoice;
        g_root->add(choice);

        g_root->add({"Ins Log schreiben", mliv::ItemKind::Action,
                     [] { mliv::LogLine("Menue: Aktion ausgeloest."); }});

        auto about = std::make_shared<mliv::Menu>("Ueber");
        about->add({"Stufe T1: Menue steht", mliv::ItemKind::Label});
        about->add({"Features folgen ab T2", mliv::ItemKind::Label});

        mliv::MenuItem sub;
        sub.label = "Ueber";
        sub.kind = mliv::ItemKind::Submenu;
        sub.submenu = about;
        g_root->add(sub);

        g_menu = std::make_unique<mliv::MenuController>(g_root);
    }

    /// Laeuft pro Bild. Hier wird nichts angelegt und nichts geloggt - ein
    /// Logeintrag je Bild waere bei 60 Bildern je Sekunde eine Datei, die
    /// schneller waechst als das Spiel laedt.
    void OnDraw()
    {
        PollInput();
        g_menu->draw(g_renderer);
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

    mliv::LogLine("Modlauncher IV Trainer, Stufe T1");

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
    plugin::drawingEvent::Add(OnDraw);

    mliv::LogLine("Menue bereit. F7 oeffnet, Numblock oder Pfeiltasten bedienen.");
}

/// Wird beim Entladen gerufen. Das SDK verlangt die Funktion, auch wenn sie
/// wenig zu tun hat - ohne sie bleibt ein unaufgeloestes Symbol.
void plugin::gameShutdownEvent()
{
    mliv::LogLine("Spiel wird beendet.");
    mliv::LogClose();
}
