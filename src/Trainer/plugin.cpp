// sauer - the trainer of Modlauncher IV, stage T6
//
// The only translation unit that includes the IV-SDK. That is not a
// convenience: IVSDK.cpp defines globals and its own DllMain. Included from
// two .cpp files it would produce duplicate symbols. Everything touching the
// SDK therefore stays here, and the menu logic in Menu.cpp still does not know
// the game.

// Foreign headers should not trigger our warning level - we build with
// /W4 /WX, and there is nothing for us to fix in somebody else's code.
#pragma warning(push, 0)
#include "IVSDK.cpp"
#pragma warning(pop)

#include "core/Config.h"
#include "core/Log.h"
#include "core/Pad.h"
#include "game/GameVersion.h"
#include "menu/Menu.h"

#include <cmath>
#include <cstdlib>
#include <fstream>
#include <memory>
#include <sstream>
#include <string>
#include <vector>

namespace
{
    /// The name in the menu title and in the log.
    ///
    /// One name in one place, so the title cannot drift away from what the
    /// launcher shows. The files stay short - sauer.asi, sauer.ini, sauer.log -
    /// the way the launcher itself is ModlauncherIV.exe and "Modlauncher IV".
    constexpr const char* kTrainerName = "sauer IV Trainer";

    // -------------------------------------------------------------- State

    std::shared_ptr<mliv::Menu> g_root;
    std::unique_ptr<mliv::MenuController> g_menu;


    // ------------------------------------------------------------- Drawing

    /// Draws with the game's text natives.
    ///
    /// Deliberately no D3D9 hook: that would look nicer but clashes with DXVK,
    /// overlays and other mods - one of the most common causes of crashes in
    /// this scene. The text natives look plainer and hold up instead.
    class NativeMenuRenderer final : public mliv::IMenuRenderer
    {
    public:
        void beginFrame(const int itemCount) override
        {
            // Header plus entries, both counted in line heights.
            const float bodyHeight = static_cast<float>(itemCount) * line_;
            const float total = titleHeight_ + bodyHeight + 2.0f * padding_;

            titleY_ = top_ + padding_;
            y_ = titleY_ + titleHeight_;

            // The box first: whatever is drawn later sits on top of it.
            FillRect(left_, top_, width_, total, 0, 0, 0, 190);

            // A narrow strip as a header so the title stands out.
            FillRect(left_, top_, width_, titleHeight_ + padding_, 158, 87, 16, 230);
        }

        void drawTitle(const std::string& text) override
        {
            SetupText(255, 255, 255, 255, titleScale_);
            Scripting::DISPLAY_TEXT_WITH_LITERAL_STRING(
                left_ + padding_, titleY_, "STRING", text.c_str());
        }

        void drawItem(const std::string& label, const std::string& value, const bool highlighted) override
        {
            if (highlighted)
            {
                FillRect(left_, y_ - padding_ * 0.4f, width_, line_, 224, 160, 74, 210);
            }

            const unsigned tone = highlighted ? 20u : 235u;
            SetupText(tone, tone, tone, 255, itemScale_);
            Scripting::DISPLAY_TEXT_WITH_LITERAL_STRING(
                left_ + padding_, y_, "STRING", label.c_str());

            if (!value.empty())
            {
                SetupText(tone, tone, tone, 255, itemScale_);

                // Right-aligned to the right edge of the box. SET_TEXT_WRAP
                // defines where "right" is - without it the text aligns to the
                // screen edge instead of the menu.
                Scripting::SET_TEXT_RIGHT_JUSTIFY(1);
                Scripting::SET_TEXT_WRAP(left_, left_ + width_ - padding_);
                Scripting::DISPLAY_TEXT_WITH_LITERAL_STRING(
                    left_ + padding_, y_, "STRING", value.c_str());

                // Undo both immediately: otherwise the label of the next entry
                // inherits our wrap bounds and gets cut off.
                Scripting::SET_TEXT_RIGHT_JUSTIFY(0);
                Scripting::SET_TEXT_WRAP(0.0f, 1.0f);
            }

            y_ += line_;
        }

        /// Reset the text state.
        ///
        /// The SET_TEXT_* natives are global and stay in effect until somebody
        /// changes them again. Whoever draws next - phone, HUD, subtitles -
        /// inherits our font, scale, colour and above all our wrap bounds. On
        /// the phone that showed up as a distorted display for as long as the
        /// menu was open.
        ///
        /// The game sets much of this itself, but not all of it and not
        /// reliably. Whoever touches the state cleans it up.
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

        /// Takes position and size from the configuration.
        ///
        /// The scale factor pulls line height, padding and both text sizes along
        /// with it. Enlarging only the font would not be enough - the text would
        /// then grow beyond its own line.
        void configure(const float left, const float top, const float width, const float scale)
        {
            left_ = left;
            top_ = top;
            width_ = width;

            line_ = 0.026f * scale;
            titleHeight_ = 0.034f * scale;
            padding_ = 0.008f * scale;
            titleScale_ = 0.34f * scale;
            itemScale_ = 0.26f * scale;
        }

    private:
        // Top-left corner of the menu, as a fraction of the screen (0..1). The
        // values here are the defaults; the configuration may override them.
        float left_ = 0.025f;
        float top_ = 0.12f;
        float width_ = 0.235f;

        float line_ = 0.026f;
        float titleHeight_ = 0.034f;
        float padding_ = 0.008f;

        float titleScale_ = 0.34f;
        float itemScale_ = 0.26f;

        float titleY_ = 0.12f;
        float y_ = 0.12f;

        /// DRAW_RECT in GTA IV takes CENTRE and SIZE, not two corners. The
        /// parameter names in the SDK suggest otherwise; fed with corners the
        /// rectangles land visibly off. This function converts from top-left
        /// plus size, because that is how layout is thought about.
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

    // --------------------------------------------------------------- Input

    struct Key
    {
        int code;
        mliv::MenuInput input;
        bool wasDown = false;
    };

    std::vector<Key> g_keys;

    /// The defaults: numpad, as is usual in this scene, plus the arrow keys so
    /// it stays usable without a numpad.
    ///
    /// They live here and not only in the template file, because they also have
    /// to apply when the file is missing, unreadable, or somebody deleted an
    /// action from it. A trainer that cannot be operated at all after one
    /// broken line would be the worst possible answer.
    struct DefaultBinding
    {
        const char* action;
        mliv::MenuInput input;
        int first;
        int second;
    };

    const DefaultBinding kDefaults[] = {
        { "Menu",    mliv::MenuInput::Toggle, VK_F7,      0         },
        { "Up",      mliv::MenuInput::Up,     VK_NUMPAD8, VK_UP     },
        { "Down",    mliv::MenuInput::Down,   VK_NUMPAD2, VK_DOWN   },
        { "Left",    mliv::MenuInput::Left,   VK_NUMPAD4, VK_LEFT   },
        { "Right",   mliv::MenuInput::Right,  VK_NUMPAD6, VK_RIGHT  },
        { "Select",  mliv::MenuInput::Select, VK_NUMPAD5, VK_RETURN },
        { "Back",    mliv::MenuInput::Back,   VK_NUMPAD0, VK_BACK   },
    };

    /// Builds the key bindings from the configuration, with the defaults as a net.
    void BindKeys(const mliv::Config& config)
    {
        g_keys.clear();

        for (const DefaultBinding& fallback : kDefaults)
        {
            std::vector<int> codes = config.keys(fallback.action);

            if (codes.empty())
            {
                codes.push_back(fallback.first);

                if (fallback.second != 0)
                {
                    codes.push_back(fallback.second);
                }
            }

            for (const int code : codes)
            {
                g_keys.push_back({code, fallback.input});
            }
        }
    }

    // ------------------------------------------------------------ Controller

    /// XInput, loaded at run time rather than linked.
    ///
    /// Which xinput DLL exists depends on the Windows version - 1_4 since
    /// Windows 8, 1_3 from the old DirectX redistributable, 9_1_0 everywhere
    /// since Vista. Linking against one of them would make the trainer refuse to
    /// load where it is absent, and the ASI loader reports that as nothing
    /// happening at all. Loading it ourselves costs one call and fails softly:
    /// no DLL, no controller, everything else works.
    using XInputGetStateFn = unsigned long (__stdcall*)(unsigned long, void*);

    XInputGetStateFn g_xinput = nullptr;
    bool g_padEnabled = true;

    /// XINPUT_STATE, only as far as we read it: a packet counter, then the pad
    /// with its button mask. Declared here for the same reason the button values
    /// are in Pad.h - so that no header has to be present at build time.
    struct XInputStateHead
    {
        unsigned long packet;
        unsigned short buttons;
    };

    void LoadXInput()
    {
        const wchar_t* candidates[] = {
            L"xinput1_4.dll",
            L"xinput1_3.dll",
            L"xinput9_1_0.dll",
        };

        for (const wchar_t* name : candidates)
        {
            if (const HMODULE module = LoadLibraryW(name))
            {
                g_xinput = reinterpret_cast<XInputGetStateFn>(
                    reinterpret_cast<void*>(GetProcAddress(module, "XInputGetState")));

                if (g_xinput != nullptr)
                {
                    mliv::LogLine("Controller: %ls in use.", name);
                    return;
                }
            }
        }

        mliv::LogLine("Controller: no XInput found, keyboard only.");
    }

    /// What is held on the first connected pad. 0 when there is none.
    ///
    /// All four slots are asked, because a single pad does not have to sit in
    /// slot 0 - after a reconnect it usually does not.
    unsigned short ReadPad()
    {
        if (g_xinput == nullptr)
        {
            return mliv::PadNone;
        }

        for (unsigned long slot = 0; slot < 4; ++slot)
        {
            XInputStateHead state{};

            // ERROR_SUCCESS. Anything else means nothing is plugged in there.
            if (g_xinput(slot, &state) == 0)
            {
                return state.buttons;
            }
        }

        return mliv::PadNone;
    }

    /// One controller binding.
    ///
    /// Navigation repeats while held: on a pad you hold a direction, you do not
    /// tap it sixty times. Select and Back do not repeat - a menu that keeps
    /// confirming because a thumb stayed on the button would be unusable.
    struct PadBinding
    {
        unsigned short chord;
        mliv::MenuInput input;
        bool repeats;
        bool wasDown = false;
        unsigned nextRepeat = 0;
    };

    std::vector<PadBinding> g_pad;

    /// How long before a held direction starts repeating, and how fast then.
    /// Roughly what a keyboard does, which is what the hand expects.
    constexpr unsigned kRepeatFirstMs = 350;
    constexpr unsigned kRepeatEveryMs = 110;

    struct PadDefault
    {
        const char* action;
        mliv::MenuInput input;
        unsigned short chord;
        bool repeats;
    };

    const PadDefault kPadDefaults[] = {
        { "Menu",   mliv::MenuInput::Toggle, mliv::PadLeftStick | mliv::PadRightStick, false },
        { "Up",     mliv::MenuInput::Up,     mliv::PadUp,                              true  },
        { "Down",   mliv::MenuInput::Down,   mliv::PadDown,                            true  },
        { "Left",   mliv::MenuInput::Left,   mliv::PadLeft,                            true  },
        { "Right",  mliv::MenuInput::Right,  mliv::PadRight,                           true  },
        { "Select", mliv::MenuInput::Select, mliv::PadA,                               false },
        { "Back",   mliv::MenuInput::Back,   mliv::PadB,                               false },
    };

    void BindPad(const mliv::Config& config)
    {
        g_pad.clear();
        g_padEnabled = config.flag("Pad.Enabled", true);

        for (const PadDefault& fallback : kPadDefaults)
        {
            std::vector<unsigned short> chords = config.chords(fallback.action);

            if (chords.empty())
            {
                chords.push_back(fallback.chord);
            }

            for (const unsigned short chord : chords)
            {
                g_pad.push_back({chord, fallback.input, fallback.repeats});
            }
        }
    }

    bool g_lockInput = false;
    bool g_controlTaken = false;

    /// Removes what earlier versions of this trainer left lying around.
    ///
    /// The launcher cleans up the files its recipe owns - it knows them from the
    /// ledger. The log and the settings are not among them: the trainer writes
    /// those itself at run time, so nothing but the trainer knows they exist.
    ///
    /// Only our own old names, and only files. Anything else here would be a
    /// plugin deleting things it does not own.
    void RemoveLegacyFiles(const std::wstring& dllPath)
    {
        const wchar_t* legacy[] = {
            L"ModlauncherIV-Trainer.log",
            L"ModlauncherIV-Trainer.ini",
            L"Trainer.log",   // the old name of the fallback under LOCALAPPDATA
        };

        std::vector<std::wstring> folders;

        const size_t slash = dllPath.find_last_of(L"\\/");
        if (slash != std::wstring::npos)
        {
            folders.push_back(dllPath.substr(0, slash + 1));
        }

        wchar_t* appData = nullptr;
        size_t length = 0;
        if (_wdupenv_s(&appData, &length, L"LOCALAPPDATA") == 0 && appData != nullptr)
        {
            folders.push_back(std::wstring(appData) + L"\\ModlauncherIV\\");
            free(appData);
        }

        for (const std::wstring& folder : folders)
        {
            for (const wchar_t* name : legacy)
            {
                const std::wstring path = folder + name;

                // Deleting is allowed to fail: under Program Files without
                // elevation it will, and a leftover log is not worth a word on
                // screen. It is written down, and that is enough.
                if (DeleteFileW(path.c_str()))
                {
                    mliv::LogLine("Left over from an earlier version, removed: %ls", path.c_str());
                }
            }
        }
    }

    /// Reads the pad and turns it into menu input.
    ///
    /// A chord counts as pressed the moment its last button goes down, and as
    /// released as soon as any one of them comes up. Otherwise L3+R3 would fire
    /// a second time when only one stick is let go.
    void PollPad()
    {
        if (!g_padEnabled || g_xinput == nullptr)
        {
            return;
        }

        const unsigned short buttons = ReadPad();
        const unsigned now = GetTickCount();

        for (PadBinding& binding : g_pad)
        {
            const bool down = (buttons & binding.chord) == binding.chord;

            if (down && !binding.wasDown)
            {
                g_menu->handle(binding.input);
                binding.nextRepeat = now + kRepeatFirstMs;
            }
            else if (down && binding.repeats && static_cast<int>(now - binding.nextRepeat) >= 0)
            {
                g_menu->handle(binding.input);
                binding.nextRepeat = now + kRepeatEveryMs;
            }

            binding.wasDown = down;
        }
    }

    /// Report edges only, not held keys.
    ///
    /// The tick runs once per frame. Without edge detection a single key press
    /// would race sixty entries onwards at 60 frames per second - the menu
    /// would be unusable.
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

    // ------------------------------------------------------------- Settings

    /// Where the configuration is looked for and created.
    ///
    /// Next to the DLL first - that is where people look. If the game sits under
    /// Program Files and runs without elevation, writing there fails, and then
    /// it falls back to LOCALAPPDATA. The same split as for the log file, so
    /// both files end up in the same place.
    std::wstring ConfigPathNextToDll(const std::wstring& dllPath)
    {
        const size_t slash = dllPath.find_last_of(L"\\/");
        if (slash == std::wstring::npos)
        {
            return {};
        }

        return dllPath.substr(0, slash + 1) + L"sauer.ini";
    }

    std::wstring ConfigPathInAppData()
    {
        wchar_t* base = nullptr;
        size_t length = 0;

        if (_wdupenv_s(&base, &length, L"LOCALAPPDATA") != 0 || base == nullptr)
        {
            return {};
        }

        std::wstring path(base);
        free(base);

        path += L"\\ModlauncherIV";
        CreateDirectoryW(path.c_str(), nullptr);

        return path + L"\\sauer.ini";
    }

    bool ReadFileText(const std::wstring& path, std::string& out)
    {
        std::ifstream file(path, std::ios::binary);
        if (!file)
        {
            return false;
        }

        std::ostringstream buffer;
        buffer << file.rdbuf();
        out = buffer.str();

        return true;
    }

    bool WriteFileText(const std::wstring& path, const std::string& text)
    {
        std::ofstream file(path, std::ios::binary | std::ios::trunc);
        if (!file)
        {
            return false;
        }

        file << text;
        return file.good();
    }

    /// Reads the configuration and creates it when there is none yet.
    ///
    /// Creating it is deliberate: a file that only exists once you write it
    /// yourself is one nobody finds. This way anyone glancing into the game
    /// directory sees what can be configured.
    mliv::Config LoadConfig(const std::wstring& dllPath)
    {
        mliv::Config config;

        const std::wstring beside = ConfigPathNextToDll(dllPath);
        const std::wstring appdata = ConfigPathInAppData();

        std::string text;

        for (const std::wstring& candidate : {beside, appdata})
        {
            if (!candidate.empty() && ReadFileText(candidate, text))
            {
                mliv::LogLine("Settings from: %ls", candidate.c_str());
                config.parse(text);

                return config;
            }
        }

        // None there - write the template, preferably next to the DLL.
        for (const std::wstring& candidate : {beside, appdata})
        {
            if (!candidate.empty() && WriteFileText(candidate, mliv::Config::DefaultText()))
            {
                mliv::LogLine("Settings created: %ls", candidate.c_str());
                config.parse(mliv::Config::DefaultText());

                return config;
            }
        }

        mliv::LogLine("Settings could neither be read nor created - defaults apply.");
        return config;
    }

    // ---------------------------------------------------------- Game access

    /// The game functions live here and not in their own file, because the
    /// IV-SDK may only appear in this one translation unit. The menu logic
    /// stays untouched by that - it only knows lambdas.
    namespace game
    {
        Scripting::Player LocalPlayer()
        {
            return static_cast<Scripting::Player>(Scripting::GET_PLAYER_ID());
        }

        /// The player ped. 0 when there is none right now - in the menu, while
        /// loading or during a cutscene. Every caller has to check that: a
        /// native with an invalid handle is not a harmless no-op but a crash.
        ///
        /// Four natives per call. That is fine for a menu action, which happens
        /// once; the per-frame path takes the handles from Frame below instead.
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

        // ------------------------------------------------- The current frame

        /// The handles for this tick, resolved once.
        ///
        /// LocalPed costs four natives, CurrentVehicle six, and the per-frame
        /// path used to ask for them half a dozen times over. Nothing inside one
        /// tick changes which ped the player is, so once is enough.
        struct Frame
        {
            Scripting::Player player = 0;
            Scripting::Ped ped = 0;
            Scripting::Vehicle vehicle = 0;
            bool playing = false;
        };

        Frame g_frame;

        /// Resolves the handles for this tick. Everything downstream reads them
        /// instead of asking the game again.
        void BeginFrame()
        {
            g_frame = Frame{};

            g_frame.player = static_cast<Scripting::Player>(Scripting::GET_PLAYER_ID());
            g_frame.playing = Scripting::IS_PLAYER_PLAYING(g_frame.player) != 0;

            if (!g_frame.playing)
            {
                return;
            }

            Scripting::Ped ped = 0;
            Scripting::GET_PLAYER_CHAR(g_frame.player, &ped);

            if (!Scripting::DOES_CHAR_EXIST(ped))
            {
                return;
            }

            g_frame.ped = ped;

            if (Scripting::IS_CHAR_IN_ANY_CAR(ped))
            {
                Scripting::GET_CAR_CHAR_IS_USING(ped, &g_frame.vehicle);
            }
        }

        void RestoreHealth()
        {
            const Scripting::Ped ped = LocalPed();
            if (ped != 0)
            {
                // The game clamps to the character maximum itself; setting it
                // higher is harmless and spares us the question of what the
                // maximum currently is.
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

        /// The weapons that actually exist in the base game.
        ///
        /// Listed deliberately rather than walking the enum range: there are
        /// WEAPON_UNUSED0, twelve EPISODIC slots, WEAPON_CAMERA and
        /// WEAPON_OBJECT in between. Handing those out catches you either
        /// nothing, or items nobody wants in their weapon wheel.
        const unsigned kWeapons[] = {
            Scripting::WEAPON_BASEBALLBAT, Scripting::WEAPON_POOLCUE,
            Scripting::WEAPON_KNIFE,       Scripting::WEAPON_GRENADE,
            Scripting::WEAPON_MOLOTOV,     Scripting::WEAPON_ROCKET,
            Scripting::WEAPON_PISTOL,      Scripting::WEAPON_DEAGLE,
            Scripting::WEAPON_SHOTGUN,     Scripting::WEAPON_BARETTA,
            Scripting::WEAPON_MICRO_UZI,   Scripting::WEAPON_MP5,
            Scripting::WEAPON_AK47,        Scripting::WEAPON_M4,
            Scripting::WEAPON_SNIPERRIFLE, Scripting::WEAPON_M40A1,
            Scripting::WEAPON_RLAUNCHER,   Scripting::WEAPON_FTHROWER,
            Scripting::WEAPON_MINIGUN,
        };

        void GiveAllWeapons()
        {
            const Scripting::Ped ped = LocalPed();
            if (ped == 0)
            {
                return;
            }

            for (const unsigned weapon : kWeapons)
            {
                Scripting::GIVE_WEAPON_TO_CHAR(ped, weapon, 500, 0);
            }
        }

        void RemoveAllWeapons()
        {
            const Scripting::Ped ped = LocalPed();
            if (ped != 0)
            {
                Scripting::REMOVE_ALL_CHAR_WEAPONS(ped);
            }
        }

        /// Refills the ammo of the weapon currently held.
        ///
        /// Only the current one, not all of them: that is one native per frame
        /// instead of nineteen. Switching weapons gives full ammo again on the
        /// next frame - the difference is imperceptible, the saving is not.

        void RefillCurrentAmmo()
        {
            const Scripting::Ped ped = g_frame.ped;
            if (ped == 0)
            {
                return;
            }

            unsigned weapon = 0;
            if (!Scripting::GET_CURRENT_CHAR_WEAPON(ped, &weapon) ||
                weapon == Scripting::WEAPON_UNARMED)
            {
                return;
            }

            unsigned maxAmmo = 0;
            if (Scripting::GET_MAX_AMMO(ped, weapon, &maxAmmo) && maxAmmo > 0)
            {
                Scripting::SET_CHAR_AMMO(ped, weapon, maxAmmo);
            }
        }

        // -------------------------------------------------------- Vehicles

        /// The vehicle the player is sitting in. 0 when they are on foot.
        Scripting::Vehicle CurrentVehicle()
        {
            const Scripting::Ped ped = LocalPed();
            if (ped == 0 || !Scripting::IS_CHAR_IN_ANY_CAR(ped))
            {
                return 0;
            }

            Scripting::Vehicle vehicle = 0;
            Scripting::GET_CAR_CHAR_IS_USING(ped, &vehicle);

            return vehicle;
        }

        void RepairVehicle()
        {
            const Scripting::Vehicle vehicle = CurrentVehicle();
            if (vehicle != 0)
            {
                Scripting::FIX_CAR(vehicle);
                Scripting::SET_CAR_HEALTH(vehicle, 1000);
            }
        }

        /// Spawns a vehicle in front of the player and puts them inside.
        ///
        /// The detour through streaming is mandatory: CREATE_CAR with a model
        /// that is not loaded does not create a vehicle, it ends the game.
        /// REQUEST_MODEL is commented out in the SDK, hence going through
        /// CStreaming directly.
        void SpawnVehicle(const char* modelName)
        {
            const Scripting::Ped ped = LocalPed();
            if (ped == 0)
            {
                return;
            }

            const unsigned hash = Scripting::GET_HASH_KEY(modelName);

            CStreaming::ScriptRequestModel(static_cast<int32_t>(hash));
            CStreaming::LoadAllRequestedModels(false);

            if (!Scripting::HAS_MODEL_LOADED(hash))
            {
                mliv::LogLine("Model not loaded: %s", modelName);
                return;
            }

            float x = 0.0f, y = 0.0f, z = 0.0f;
            Scripting::GET_CHAR_COORDINATES(ped, &x, &y, &z);

            Scripting::Vehicle vehicle = 0;
            Scripting::CREATE_CAR(hash, x + 3.0f, y + 3.0f, z, &vehicle, 1);

            if (vehicle != 0)
            {
                Scripting::WARP_CHAR_INTO_CAR(ped, vehicle);
                mliv::LogLine("Vehicle spawned: %s", modelName);
            }

            // Without this the game keeps the model in memory for good. With a
            // trainer people like to try twenty cars with, that adds up.

            Scripting::MARK_MODEL_AS_NO_LONGER_NEEDED(hash);
        }

        // ------------------------------------------------------------ World

        void SetTime(const int hour)
        {
            Scripting::SET_TIME_OF_DAY(static_cast<unsigned>(hour), 0);
        }

        void SetWeather(const unsigned weather)
        {
            // FORCE_WEATHER_NOW rather than FORCE_WEATHER: the latter fades
            // slowly, and from the menu that looks like it did nothing.
            Scripting::FORCE_WEATHER_NOW(weather);
        }

        /// Puts the player at a position and lands them on the ground.
        ///
        /// Without the ground height you either fall through the world or stand
        /// in mid-air. GET_GROUND_Z_FOR_3D_COORD does need loaded geometry
        /// though - so put them there roughly first, then correct.
        void Teleport(const float x, const float y, const float z)
        {
            const Scripting::Ped ped = LocalPed();
            if (ped == 0)
            {
                return;
            }

            Scripting::SET_CHAR_COORDINATES(ped, x, y, z);

            float ground = 0.0f;
            Scripting::GET_GROUND_Z_FOR_3D_COORD(x, y, z + 50.0f, &ground);

            if (ground > 0.0f)
            {
                Scripting::SET_CHAR_COORDINATES(ped, x, y, ground + 1.0f);
            }
        }

        // ------------------------------------------------------- Bewegung

        /// The game camera's view direction as a unit vector.
        ///
        /// GET_CAM_ROT returns angles in degrees: X is the pitch, Z the compass
        /// heading. The conversion follows the game's convention - Y points
        /// north, not X.
        void CameraForward(float& fx, float& fy, float& fz)
        {
            int camera = 0;
            Scripting::GET_GAME_CAM(&camera);

            float pitch = 0.0f, roll = 0.0f, yaw = 0.0f;
            Scripting::GET_CAM_ROT(camera, &pitch, &roll, &yaw);

            const float p = pitch * 3.14159265f / 180.0f;
            const float y = yaw * 3.14159265f / 180.0f;

            fx = -std::sin(y) * std::cos(p);
            fy = std::cos(y) * std::cos(p);
            fz = std::sin(p);
        }

        /// Switches free flight on or off.
        ///
        /// The first attempt froze the player and moved them a step each frame.
        /// That looked like stutter, and rightly so: between two jumps there is
        /// no motion for the engine to smooth, and the step size depends on the
        /// frame rate.
        ///
        /// The right way is to let the physics work and only hand it a velocity.
        /// Then the engine interpolates in between, and a value in units per
        /// second is independent of the frame rate. Collision off so you get
        /// through walls - that is the point of the exercise - and gravity off
        /// so you can hold still.
        void SetNoclip(const bool on, const float normalGravity)
        {
            const Scripting::Ped ped = LocalPed();
            if (ped == 0)
            {
                return;
            }

            Scripting::SET_CHAR_COLLISION(ped, on ? 0 : 1);
            Scripting::SET_CHAR_GRAVITY(ped, on ? 0.0f : normalGravity);

            if (!on)
            {
                // Without this you keep the last flight velocity on exit and
                // shoot off across the map.
                Scripting::SET_CHAR_VELOCITY(ped, 0.0f, 0.0f, 0.0f);
            }
        }

        /// Sets the flight direction. Called every frame, even without input -
        /// otherwise you fall in the gap between two key presses.
        void FlyBy(const float forward, const float side, const float up, const float speed)
        {
            const Scripting::Ped ped = g_frame.ped;
            if (ped == 0)
            {
                return;
            }

            float fx = 0.0f, fy = 0.0f, fz = 0.0f;
            CameraForward(fx, fy, fz);

            // Right vector: the view direction turned 90 degrees, without pitch.
            // With pitch, strafing sideways would produce a climb or descent
            // nobody asked for.
            const float length = std::sqrt(fx * fx + fy * fy);
            const float rx = length > 0.0001f ? fy / length : 1.0f;
            const float ry = length > 0.0001f ? -fx / length : 0.0f;

            float vx = (fx * forward + rx * side) * speed;
            float vy = (fy * forward + ry * side) * speed;
            float vz = (fz * forward + up) * speed;

            // Held diagonally you would otherwise be 1.41 times faster than
            // straight ahead - the oldest bug in any flight control.
            const float total = std::sqrt(vx * vx + vy * vy + vz * vz);
            if (total > speed)
            {
                const float factor = speed / total;
                vx *= factor;
                vy *= factor;
                vz *= factor;
            }

            Scripting::SET_CHAR_VELOCITY(ped, vx, vy, vz);
        }

        /// Jumps to the waypoint on the map.
        ///
        /// The waypoint is a blip like any other; its Z coordinate, however, is
        /// not the ground height but zero. So it gets discarded and the ground
        /// is looked up - otherwise you land beneath the map.
        bool TeleportToWaypoint()
        {
            const Scripting::Blip blip = Scripting::GET_FIRST_BLIP_INFO_ID(Scripting::BLIP_WAYPOINT);
            if (blip == 0)
            {
                return false;
            }

            Scripting::Vector3 position{};
            Scripting::GET_BLIP_COORDS(blip, &position);

            Teleport(position.x, position.y, 200.0f);
            return true;
        }

        // ------------------------------------------------------ Vehicles II

        void BoostVehicle(const float extra)
        {
            const Scripting::Vehicle vehicle = CurrentVehicle();
            if (vehicle == 0)
            {
                return;
            }

            float speed = 0.0f;
            Scripting::GET_CAR_SPEED(vehicle, &speed);
            Scripting::SET_CAR_FORWARD_SPEED(vehicle, speed + extra);
        }

        /// Puts a stranded vehicle back on its wheels.
        ///
        /// SET_CAR_ON_GROUND_PROPERLY does not exist in this SDK, so by hand:
        /// place it a little above the ground and undo the tilt by setting the
        /// heading again.
        void UprightVehicle()
        {
            const Scripting::Vehicle vehicle = CurrentVehicle();
            if (vehicle == 0)
            {
                return;
            }

            float x = 0.0f, y = 0.0f, z = 0.0f;
            Scripting::GET_CAR_COORDINATES(vehicle, &x, &y, &z);

            float ground = 0.0f;
            Scripting::GET_GROUND_Z_FOR_3D_COORD(x, y, z + 20.0f, &ground);

            float heading = 0.0f;
            Scripting::GET_CAR_HEADING(vehicle, &heading);

            Scripting::SET_CAR_COORDINATES(vehicle, x, y, (ground > 0.0f ? ground : z) + 1.5f);

            // Setting the heading again takes pitch and roll back with it -
            // that is the way to flip it upright without the native this SDK
            // does not have.
            Scripting::SET_CAR_HEADING(vehicle, heading);
        }

        void DeleteCurrentVehicle()
        {
            Scripting::Vehicle vehicle = CurrentVehicle();
            if (vehicle == 0)
            {
                return;
            }

            const Scripting::Ped ped = LocalPed();
            if (ped != 0)
            {
                // Get out first. A deleted vehicle with an occupant inside
                // leaves the player in mid-air.
                float x = 0.0f, y = 0.0f, z = 0.0f;
                Scripting::GET_CHAR_COORDINATES(ped, &x, &y, &z);
                Scripting::SET_CHAR_COORDINATES(ped, x + 2.0f, y, z);
            }

            Scripting::DELETE_CAR(&vehicle);
        }

        // ------------------------------------------------------ Pedestrians

        /// Arms the nearest pedestrian and sets them on the player.
        void ProvokeNearest()
        {
            const Scripting::Ped player = LocalPed();
            if (player == 0)
            {
                return;
            }

            float x = 0.0f, y = 0.0f, z = 0.0f;
            Scripting::GET_CHAR_COORDINATES(player, &x, &y, &z);

            Scripting::Ped other = 0;
            Scripting::GET_CLOSEST_CHAR(x, y, z, 30.0f, 1, 1, &other);

            if (other == 0 || other == player)
            {
                return;
            }

            Scripting::GIVE_WEAPON_TO_CHAR(other, Scripting::WEAPON_PISTOL, 200, 1);
            Scripting::SET_CHAR_ACCURACY(other, 40);
            Scripting::SET_CHAR_AS_ENEMY(other, 1);
            Scripting::TASK_COMBAT(other, player);
        }

        /// Sets the health of everyone nearby to zero.
        ///
        /// GET_CLOSEST_CHAR only ever returns one; after each hit a different
        /// one is nearest, so call it repeatedly. A fixed upper bound rather
        /// than a loop until "nobody left" - otherwise the game hangs as soon
        /// as the native returns the same pedestrian for any reason.
        void KillNearby()
        {
            const Scripting::Ped player = LocalPed();
            if (player == 0)
            {
                return;
            }

            float x = 0.0f, y = 0.0f, z = 0.0f;
            Scripting::GET_CHAR_COORDINATES(player, &x, &y, &z);

            for (int i = 0; i < 24; ++i)
            {
                Scripting::Ped other = 0;
                Scripting::GET_CLOSEST_CHAR(x, y, z, 35.0f, 1, 1, &other);

                if (other == 0 || other == player)
                {
                    break;
                }

                Scripting::SET_CHAR_HEALTH(other, 0);
            }
        }

        /// An explosion some distance in front of the player.
        ///
        /// Offset on purpose and not at your own position: an explosion under
        /// your own feet is not a feature, it is a suicide button.
        void ExplosionAhead()
        {
            const Scripting::Ped ped = LocalPed();
            if (ped == 0)
            {
                return;
            }

            float x = 0.0f, y = 0.0f, z = 0.0f;
            Scripting::GET_OFFSET_FROM_CHAR_IN_WORLD_COORDS(ped, 0.0f, 8.0f, 0.0f, &x, &y, &z);

            Scripting::ADD_EXPLOSION(x, y, z, 0, 1.0f, 1, 0, 1.0f);
        }

        // ------------------------------------------------- More vehicles

        /// Calls something for every vehicle within a radius.
        ///
        /// GET_RANDOM_CAR_IN_SPHERE_NO_SAVE returns one vehicle, not all of
        /// them. Called repeatedly it yields different ones, but not every one
        /// with any guarantee - hence a fixed upper bound rather than a loop
        /// hoping for completeness that might never end.
        template <typename Action>
        void ForNearbyCars(const float radius, const int attempts, Action action)
        {
            const Scripting::Ped ped = LocalPed();
            if (ped == 0)
            {
                return;
            }

            float x = 0.0f, y = 0.0f, z = 0.0f;
            Scripting::GET_CHAR_COORDINATES(ped, &x, &y, &z);

            const Scripting::Vehicle own = CurrentVehicle();

            for (int i = 0; i < attempts; ++i)
            {
                Scripting::Vehicle car = 0;
                Scripting::GET_RANDOM_CAR_IN_SPHERE_NO_SAVE(x, y, z, radius, 0, 0, &car);

                if (car == 0 || car == own)
                {
                    continue;
                }

                action(car);
            }
        }

        void EnterNearestCar()
        {
            const Scripting::Ped ped = LocalPed();
            if (ped == 0)
            {
                return;
            }

            float x = 0.0f, y = 0.0f, z = 0.0f;
            Scripting::GET_CHAR_COORDINATES(ped, &x, &y, &z);

            Scripting::Vehicle car = 0;
            Scripting::GET_RANDOM_CAR_IN_SPHERE_NO_SAVE(x, y, z, 25.0f, 0, 0, &car);

            if (car != 0)
            {
                Scripting::WARP_CHAR_INTO_CAR(ped, car);
            }
        }

        // ------------------------------------------------------- More player

        void SetInvisible(const bool on)
        {
            const Scripting::Ped ped = LocalPed();
            if (ped != 0)
            {
                Scripting::SET_CHAR_VISIBLE(ped, on ? 0 : 1);
            }
        }

        /// Puts the player where the camera is.
        ///
        /// The fastest way anywhere: look at it, trigger. Meant for rooftops
        /// and places you would otherwise have to climb to.
        void TeleportToCamera()
        {
            const Scripting::Ped ped = LocalPed();
            if (ped == 0)
            {
                return;
            }

            int camera = 0;
            Scripting::GET_GAME_CAM(&camera);

            float x = 0.0f, y = 0.0f, z = 0.0f;
            Scripting::GET_CAM_POS(camera, &x, &y, &z);

            Scripting::SET_CHAR_COORDINATES(ped, x, y, z);
        }

        void Heal()
        {
            const Scripting::Ped ped = LocalPed();
            if (ped == 0)
            {
                return;
            }

            Scripting::SET_CHAR_HEALTH(ped, 200);
            Scripting::ADD_ARMOUR_TO_CHAR(ped, 100);
        }

        // ---------------------------------------------------- Sticky flags

        /// The switches that attach themselves to the player.
        ///
        /// These are not written every frame. They are written when they change,
        /// and again when the ped changes - on death, a model change or a
        /// mission start the player gets a new handle, and everything set on the
        /// old one is gone. A switch that silently stops working after the first
        /// hospital visit is worse than no switch at all.
        ///
        /// Writing them unconditionally, which is what this did before, is worse
        /// than merely wasteful. Every switch that is off then writes its "off"
        /// value over the game every frame, and not all of those are what the
        /// game would have done: with "shoot from vehicles" off it kept calling
        /// SET_PLAYER_CAN_DO_DRIVE_BY(0), so the trainer quietly took drive-bys
        /// away from a player who had never touched the setting.
        /// Weapon skill: leave whatever the game set.
        constexpr int kSkillUntouched = -1;

        struct PlayerFlags
        {
            bool neverTired;
            bool fastReload;
            bool waterproof;
            bool fireproof;
            bool invisibleToAi;
            bool cantBeDragged;
            bool stayOnBike;
            bool shootInCar;
            bool drunk;
            bool noCriticalHits;
            int weaponSkill;
        };

        /// What was last written, and to whom.
        PlayerFlags g_appliedPlayer{};
        Scripting::Ped g_appliedToPed = 0;
        Scripting::Player g_appliedToPlayer = -1;

        void ApplyPlayerFlags(const PlayerFlags& want)
        {
            const Scripting::Player player = g_frame.player;
            const Scripting::Ped ped = g_frame.ped;

            if (ped == 0)
            {
                return;
            }

            // A ped we have not written to yet starts from the game's own
            // defaults. So forget what we know: everything that is on gets
            // written again, everything that is off already is what it should
            // be. The player-level switches get rewritten with it - they would
            // survive, but the handle changes so rarely that keeping two
            // separate records would cost more reading than it saves calls.
            if (ped != g_appliedToPed || player != g_appliedToPlayer)
            {
                g_appliedPlayer = PlayerFlags{};
                g_appliedPlayer.weaponSkill = kSkillUntouched;
            }

            const PlayerFlags& have = g_appliedPlayer;

            if (want.neverTired != have.neverTired)
            {
                Scripting::SET_PLAYER_NEVER_GETS_TIRED(player, want.neverTired ? 1 : 0);
            }

            if (want.fastReload != have.fastReload)
            {
                Scripting::SET_PLAYER_FAST_RELOAD(player, want.fastReload ? 1 : 0);
            }

            if (want.invisibleToAi != have.invisibleToAi)
            {
                Scripting::SET_PLAYER_INVISIBLE_TO_AI(want.invisibleToAi ? 1 : 0);
            }

            if (want.waterproof != have.waterproof)
            {
                Scripting::SET_CHAR_DROWNS_IN_WATER(ped, want.waterproof ? 0 : 1);
                Scripting::SET_CHAR_DIES_INSTANTLY_IN_WATER(ped, want.waterproof ? 0 : 1);
                Scripting::SET_CHAR_MAX_TIME_UNDERWATER(ped, want.waterproof ? 10000.0f : 10.0f);
            }

            if (want.fireproof != have.fireproof)
            {
                Scripting::SET_CHAR_FIRE_DAMAGE_MULTIPLIER(ped, want.fireproof ? 0.0f : 1.0f);
            }

            if (want.cantBeDragged != have.cantBeDragged)
            {
                Scripting::SET_CHAR_CANT_BE_DRAGGED_OUT(ped, want.cantBeDragged ? 1 : 0);
            }

            if (want.stayOnBike != have.stayOnBike)
            {
                Scripting::SET_CHAR_CAN_BE_KNOCKED_OFF_BIKE(ped, want.stayOnBike ? 0 : 1);
            }

            if (want.shootInCar != have.shootInCar)
            {
                Scripting::SET_CHAR_CAN_BE_SHOT_IN_VEHICLE(ped, want.shootInCar ? 1 : 0);
                Scripting::SET_PLAYER_CAN_DO_DRIVE_BY(player, want.shootInCar ? 1 : 0);
            }

            if (want.drunk != have.drunk)
            {
                Scripting::SET_CHAR_DRUGGED_UP(ped, want.drunk ? 1 : 0);
            }

            if (want.noCriticalHits != have.noCriticalHits)
            {
                Scripting::SET_CHAR_SUFFERS_CRITICAL_HITS(ped, want.noCriticalHits ? 0 : 1);
            }

            // kSkillUntouched means the game keeps whatever it had. There is no
            // way back to that once we have written a number, so not writing one
            // in the first place is the only way to offer it.
            if (want.weaponSkill != kSkillUntouched && want.weaponSkill != have.weaponSkill)
            {
                Scripting::SET_CHAR_WEAPON_SKILL(ped, want.weaponSkill);
            }

            g_appliedPlayer = want;
            g_appliedToPed = ped;
            g_appliedToPlayer = player;
        }

        struct VehicleFlags
        {
            bool watertight;
            bool noVisibleDamage;
            bool noCollision;
            bool alwaysSkids;
        };

        VehicleFlags g_appliedVehicleFlags{};
        Scripting::Vehicle g_appliedToVehicle = 0;

        void ApplyVehicleFlags(const VehicleFlags& want)
        {
            const Scripting::Vehicle vehicle = g_frame.vehicle;
            if (vehicle == 0)
            {
                return;
            }

            // Same as with the ped, only this handle changes far more often -
            // every time the player gets into a different car.
            if (vehicle != g_appliedToVehicle)
            {
                g_appliedVehicleFlags = VehicleFlags{};
            }

            const VehicleFlags& have = g_appliedVehicleFlags;

            if (want.watertight != have.watertight)
            {
                Scripting::SET_CAR_WATERTIGHT(vehicle, want.watertight ? 1 : 0);
            }

            if (want.noVisibleDamage != have.noVisibleDamage)
            {
                Scripting::SET_CAR_CAN_BE_VISIBLY_DAMAGED(vehicle, want.noVisibleDamage ? 0 : 1);
            }

            if (want.noCollision != have.noCollision)
            {
                Scripting::SET_CAR_COLLISION(vehicle, want.noCollision ? 0 : 1);
            }

            if (want.alwaysSkids != have.alwaysSkids)
            {
                Scripting::SET_CAR_ALWAYS_CREATE_SKIDS(vehicle, want.alwaysSkids ? 1 : 0);
            }

            g_appliedVehicleFlags = want;
            g_appliedToVehicle = vehicle;
        }

        void ClearCopsNearby()
        {
            const Scripting::Ped ped = LocalPed();
            if (ped == 0)
            {
                return;
            }

            float x = 0.0f, y = 0.0f, z = 0.0f;
            Scripting::GET_CHAR_COORDINATES(ped, &x, &y, &z);

            Scripting::CLEAR_AREA_OF_COPS(x, y, z, 150.0f);
        }

        // ------------------------------------------------------------- Time

        void SetTimeScale(const float scale)
        {
            Scripting::SET_TIME_SCALE(scale);
        }

        void SetGravity(const float value)
        {
            const Scripting::Ped ped = LocalPed();
            if (ped == 0)
            {
                return;
            }

            // SET_GRAVITY_OFF affects the whole world, SET_CHAR_GRAVITY only
            // the player. For moon jumps you want the second one - otherwise
            // every vehicle floats away as well.
            Scripting::SET_CHAR_GRAVITY(ped, value);
        }

        void SetWantedLevel(const int level)
        {
            const Scripting::Player player = LocalPlayer();
            Scripting::ALTER_WANTED_LEVEL(player, static_cast<unsigned>(level));

            // Without this the game only picks the change up eventually - the
            // star in the HUD would stay put and you would think it was broken.
            Scripting::APPLY_WANTED_LEVEL_CHANGE_NOW(player);
        }
    }

    // --------------------------------------------------------------- State



    bool g_godmode = false;
    bool g_neverWanted = false;
    bool g_infiniteAmmo = false;
    bool g_strongVehicle = false;

    int g_vehicleChoice = 0;
    int g_timeChoice = 2;
    int g_weatherChoice = 1;
    int g_placeChoice = 0;

    /// Index of the "untouched" level - there we do not touch the density.
    constexpr int kTrafficDefault = 2;
    int g_trafficChoice = kTrafficDefault;

    bool g_noclip = false;
    bool g_invisible = false;
    bool g_frozenVehicle = false;
    bool g_invisibleVehicle = false;

    game::PlayerFlags g_player{};
    game::VehicleFlags g_vehicleFlags{};

    bool g_noHud = false;
    bool g_noRadar = false;
    bool g_noVehicleLights = false;

    int g_skillChoice = 0;
    int g_maxWantedChoice = 6;
    int g_parkedChoice = kTrafficDefault;

    /// The player's weapon skill. 100 means no sway and no spread.
    ///
    /// The first entry writes nothing at all. Without it the trainer would set
    /// a number on a player who never asked for one, and there would be no way
    /// back to whatever the game had.
    const int kSkills[] = {game::kSkillUntouched, 50, 75, 100};
    bool g_peacefulPeds = false;
    bool g_noCops = false;

    int g_gravityChoice = 0;
    int g_timeScaleChoice = 2;
    int g_pedChoice = kTrafficDefault;
    int g_colourChoice = 0;
    int g_lightChoice = 0;
    int g_flySpeedChoice = 1;

    const float kGravities[] = {9.8f, 2.0f, 0.5f};
    const float kTimeScales[] = {0.1f, 0.3f, 1.0f, 1.6f, 2.5f};
    /// In units per second, not per frame. For comparison: walking is about 2,
    /// running 7, a car on the motorway 30.
    const float kFlySpeeds[] = {8.0f, 22.0f, 60.0f};

    const float kTrafficDensities[] = {0.0f, 0.5f, 1.0f, 2.0f};
    const int kTimes[] = {0, 6, 12, 18, 21};

    const unsigned kWeathers[] = {
        Scripting::WEATHER_EXTRA_SUNNY, Scripting::WEATHER_SUNNY,
        Scripting::WEATHER_CLOUDY,      Scripting::WEATHER_RAINING,
        Scripting::WEATHER_FOGGY,       Scripting::WEATHER_LIGHTNING,
    };

    /// Model names from the game's handling.dat.
    const char* const kVehicles[] = {
        "infernus", "comet", "banshee", "turismo", "sultanrs",
        "nrg900",   "sanchez", "patriot", "annihilator", "maverick",
    };

    /// A few places in Liberty City. Coordinates taken from the game.
    struct Place
    {
        const char* name;
        float x, y, z;
    };

    const Place kPlaces[] = {
        { "Broker",        -70.0f,  1210.0f,  19.0f },
        { "Algonquin",    -350.0f,   970.0f,  15.0f },
        { "Bohan",         640.0f,  1800.0f,  20.0f },
        { "Airport",      1600.0f,  -400.0f,  15.0f },
        { "Happiness I.", -380.0f,  1450.0f,  15.0f },
    };
    int  g_wantedChoice = 0;
    int  g_moneyChoice = 1;

    const int kMoneyAmounts[] = {1000, 10000, 100000, 1000000};

    /// Takes the game controls away while the menu is open, if asked to.
    ///
    /// The presses reach the game as well - there is no way to swallow them from
    /// here. On the keyboard that is bearable, because the numpad does little in
    /// GTA IV. On a pad it is not: every button is already taken, so opening the
    /// menu also does something in the game, and scrolling through it switches
    /// weapons underneath.
    ///
    /// Off by default all the same. Freezing the player is the more drastic of
    /// the two annoyances if it happens in traffic, and which one somebody
    /// prefers is not ours to decide.
    void ApplyInputLock()
    {
        const bool want = g_lockInput && g_menu->visible();

        // Not while there is no player: the call would go nowhere, and control
        // has to come back on the next frame that has one - otherwise closing
        // the menu during a cutscene would leave the player frozen afterwards.
        if (want == g_controlTaken || !game::g_frame.playing)
        {
            return;
        }

        Scripting::SET_PLAYER_CONTROL(game::g_frame.player, want ? 0 : 1);
        g_controlTaken = want;
    }

    /// Runs every frame.
    ///
    /// Godmode and "never wanted" get set again and again here, not just when
    /// toggled. The game resets both on respawn, during cutscenes and on
    /// mission changes - a switch set once would silently stop working, and the
    /// user would think the trainer is broken rather than the game wilful.

    void EnforceToggles()
    {
        // The handles come from the frame, resolved once in OnScript. Asking the
        // game again here is what made the idle trainer cost around thirty
        // natives a frame for nothing.
        const Scripting::Player player = game::g_frame.player;

        if (!game::g_frame.playing)
        {
            return;
        }

        if (g_godmode && game::g_frame.ped != 0)
        {
            Scripting::SET_CHAR_INVINCIBLE(game::g_frame.ped, 1);
            Scripting::SET_PLAYER_INVINCIBLE(player, 1);
        }

        if (g_neverWanted)
        {
            Scripting::CLEAR_WANTED_LEVEL(player);
        }

        if (g_infiniteAmmo)
        {
            game::RefillCurrentAmmo();
        }

        if (g_strongVehicle && game::g_frame.vehicle != 0)
        {
            Scripting::SET_CAR_STRONG(game::g_frame.vehicle, 1);
            Scripting::SET_CAR_PROOFS(game::g_frame.vehicle, 1, 1, 1, 1, 1);
        }

        // The game resets the density multipliers to 1.0 every frame. Setting
        // them once from the menu would have no effect at all.
        if (g_trafficChoice != kTrafficDefault)
        {
            const float density = kTrafficDensities[g_trafficChoice];
            Scripting::SET_CAR_DENSITY_MULTIPLIER(density);
            Scripting::SET_RANDOM_CAR_DENSITY_MULTIPLIER(density);
        }

        if (g_pedChoice != kTrafficDefault)
        {
            const float density = kTrafficDensities[g_pedChoice];
            Scripting::SET_PED_DENSITY_MULTIPLIER(density);
            Scripting::SET_SCENARIO_PED_DENSITY_MULTIPLIER(density, density);
        }

        if (g_peacefulPeds)
        {
            Scripting::SET_EVERYONE_IGNORE_PLAYER(player, 1);
        }

        if (g_noCops)
        {
            Scripting::SET_CREATE_RANDOM_COPS(0);
        }

        if (g_parkedChoice != kTrafficDefault)
        {
            Scripting::SET_PARKED_CAR_DENSITY_MULTIPLIER(kTrafficDensities[g_parkedChoice]);
        }

        g_player.weaponSkill = kSkills[g_skillChoice];
        game::ApplyPlayerFlags(g_player);
        game::ApplyVehicleFlags(g_vehicleFlags);

        // The game switches HUD and radar back on at every scene change.
        if (g_noHud)
        {
            Scripting::DISPLAY_HUD(0);
        }

        if (g_noRadar)
        {
            Scripting::DISPLAY_RADAR(0);
        }

        if (g_noVehicleLights)
        {
            Scripting::FORCE_ALL_VEHICLE_LIGHTS_OFF(1);
        }
    }

    // ------------------------------------------------------------- Flying

    /// The keys for flying.
    ///
    /// Their own keys and not the menu's: while flying, the menu should stay
    /// usable. W/A/S/D and space/ctrl also sit where people know them from
    /// other games.
    struct FlyKey
    {
        const char* action;
        int code;
        float* axis;
        float sign;
    };

    float g_flyForward = 0.0f;
    float g_flySide = 0.0f;
    float g_flyUp = 0.0f;

    FlyKey g_flyKeys[] = {
        { "FlyForward", 'W',        &g_flyForward, +1.0f },
        { "FlyBack",    'S',        &g_flyForward, -1.0f },
        { "FlyRight",   'D',        &g_flySide,    +1.0f },
        { "FlyLeft",    'A',        &g_flySide,    -1.0f },
        { "FlyUp",      VK_SPACE,   &g_flyUp,      +1.0f },
        { "FlyDown",    VK_CONTROL, &g_flyUp,      -1.0f },
    };

    void BindFlyKeys(const mliv::Config& config)
    {
        for (FlyKey& key : g_flyKeys)
        {
            const std::vector<int> codes = config.keys(key.action);
            if (!codes.empty())
            {
                key.code = codes.front();
            }
        }
    }

    /// Held keys, not edges: flying means holding the key down.
    void ApplyNoclip()
    {
        if (!g_noclip)
        {
            return;
        }

        g_flyForward = 0.0f;
        g_flySide = 0.0f;
        g_flyUp = 0.0f;

        for (const FlyKey& key : g_flyKeys)
        {
            if ((GetAsyncKeyState(key.code) & 0x8000) != 0)
            {
                *key.axis += key.sign;
            }
        }

        // Set even without input, and to zero: otherwise the player keeps their
        // last velocity and drifts on.
        game::FlyBy(g_flyForward, g_flySide, g_flyUp, kFlySpeeds[g_flySpeedChoice]);
    }

    // ---------------------------------------------------------------- Menu

    void BuildMenu()
    {
        g_root = std::make_shared<mliv::Menu>(kTrainerName);

        // Every category is a submenu, and the root holds nothing else.
        //
        // It used to mix around twenty single entries with a few submenus, which
        // meant scrolling past health and money to reach the world settings. At
        // sixty options a flat list stops being a list and becomes a search.
        auto submenu = [](const char* label, std::shared_ptr<mliv::Menu> menu) {
            mliv::MenuItem item;
            item.label = label;
            item.kind = mliv::ItemKind::Submenu;
            item.submenu = std::move(menu);
            return item;
        };

        // --- Player ---
        auto player = std::make_shared<mliv::Menu>("Player");

        // Switching off has to actively take invincibility back. EnforceToggles
        // merely stops setting it - that turns nothing off, and the player would
        // stay immortal.
        player->add({"Godmode", mliv::ItemKind::Toggle, [] {
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

        player->add({"Refill health", mliv::ItemKind::Action, game::RestoreHealth});
        player->add({"Refill armour", mliv::ItemKind::Action, game::RestoreArmour});
        player->add({"Full heal", mliv::ItemKind::Action, game::Heal});
        player->add({"Invisible", mliv::ItemKind::Toggle,
                     [] { game::SetInvisible(g_invisible); }, &g_invisible});
        player->add({"Jump to camera", mliv::ItemKind::Action, game::TeleportToCamera});

        // --- Traits ---
        auto traits = std::make_shared<mliv::Menu>("Traits");

        traits->add({"Never gets tired", mliv::ItemKind::Toggle, nullptr, &g_player.neverTired});
        traits->add({"Fast reload", mliv::ItemKind::Toggle, nullptr, &g_player.fastReload});
        traits->add({"Does not drown", mliv::ItemKind::Toggle, nullptr, &g_player.waterproof});
        traits->add({"Fireproof", mliv::ItemKind::Toggle, nullptr, &g_player.fireproof});
        traits->add({"No critical hits", mliv::ItemKind::Toggle, nullptr, &g_player.noCriticalHits});
        traits->add({"Invisible to the AI", mliv::ItemKind::Toggle, nullptr, &g_player.invisibleToAi});
        traits->add({"Cannot be dragged out", mliv::ItemKind::Toggle, nullptr, &g_player.cantBeDragged});
        traits->add({"Stays on the bike", mliv::ItemKind::Toggle, nullptr, &g_player.stayOnBike});
        traits->add({"Shoot from vehicles", mliv::ItemKind::Toggle, nullptr, &g_player.shootInCar});
        traits->add({"Drunk", mliv::ItemKind::Toggle, nullptr, &g_player.drunk});

        player->add(submenu("Traits", traits));
        g_root->add(submenu("Player", player));

        // --- Weapons ---
        auto weapons = std::make_shared<mliv::Menu>("Weapons");

        weapons->add({"Give all weapons", mliv::ItemKind::Action, game::GiveAllWeapons});
        weapons->add({"Take weapons away", mliv::ItemKind::Action, game::RemoveAllWeapons});
        weapons->add({"Infinite ammo", mliv::ItemKind::Toggle, nullptr, &g_infiniteAmmo});

        // Skill sits with the weapons, not with the traits: that is where it is
        // looked for.
        mliv::MenuItem skill;
        skill.label = "Weapon skill";
        skill.kind = mliv::ItemKind::Choice;
        skill.choices = {"As in the game", "Normal", "Good", "Perfect"};
        skill.choiceIndex = &g_skillChoice;
        weapons->add(skill);

        g_root->add(submenu("Weapons", weapons));

        // --- Wanted ---
        auto wantedMenu = std::make_shared<mliv::Menu>("Wanted");

        mliv::MenuItem wanted;
        wanted.label = "Wanted level";
        wanted.kind = mliv::ItemKind::Choice;
        wanted.choices = {"0", "1", "2", "3", "4", "5", "6"};
        wanted.choiceIndex = &g_wantedChoice;
        wanted.onChoice = [](const int level) { game::SetWantedLevel(level); };
        wantedMenu->add(wanted);

        wantedMenu->add({"Never wanted", mliv::ItemKind::Toggle, nullptr, &g_neverWanted});

        mliv::MenuItem maxWanted;
        maxWanted.label = "At most";
        maxWanted.kind = mliv::ItemKind::Choice;
        maxWanted.choices = {"0", "1", "2", "3", "4", "5", "6"};
        maxWanted.choiceIndex = &g_maxWantedChoice;
        maxWanted.onChoice = [](const int level) {
            Scripting::SET_MAX_WANTED_LEVEL(static_cast<unsigned>(level));
        };
        wantedMenu->add(maxWanted);

        wantedMenu->add({"Clear cops nearby", mliv::ItemKind::Action, game::ClearCopsNearby});
        wantedMenu->add({"No new police patrols", mliv::ItemKind::Toggle, nullptr, &g_noCops});

        g_root->add(submenu("Wanted", wantedMenu));

        // --- Money ---
        auto moneyMenu = std::make_shared<mliv::Menu>("Money");

        mliv::MenuItem money;
        money.label = "Amount";
        money.kind = mliv::ItemKind::Choice;
        money.choices = {"1.000", "10.000", "100.000", "1.000.000"};
        money.choiceIndex = &g_moneyChoice;
        moneyMenu->add(money);

        moneyMenu->add({"Give money", mliv::ItemKind::Action, [] {
            game::AddMoney(kMoneyAmounts[g_moneyChoice]);
            mliv::LogLine("Money given: %d", kMoneyAmounts[g_moneyChoice]);
        }});

        g_root->add(submenu("Money", moneyMenu));

        // --- Vehicles ---
        auto vehicles = std::make_shared<mliv::Menu>("Vehicles");

        mliv::MenuItem model;
        model.label = "Model";
        model.kind = mliv::ItemKind::Choice;
        model.choiceIndex = &g_vehicleChoice;
        for (const char* name : kVehicles)
        {
            model.choices.emplace_back(name);
        }

        vehicles->add(model);
        vehicles->add({"Spawn", mliv::ItemKind::Action,
                       [] { game::SpawnVehicle(kVehicles[g_vehicleChoice]); }});
        vehicles->add({"Repair", mliv::ItemKind::Action, game::RepairVehicle});
        vehicles->add({"Indestructible", mliv::ItemKind::Toggle, nullptr, &g_strongVehicle});

        vehicles->add({"-- Tuning --", mliv::ItemKind::Label});
        vehicles->add({"Boost", mliv::ItemKind::Action, [] { game::BoostVehicle(20.0f); }});
        vehicles->add({"Flip upright", mliv::ItemKind::Action, game::UprightVehicle});
        vehicles->add({"Remove", mliv::ItemKind::Action, game::DeleteCurrentVehicle});

        mliv::MenuItem colour;
        colour.label = "Colour";
        colour.kind = mliv::ItemKind::Choice;
        colour.choices = {"Black", "White", "Red", "Blue", "Yellow", "Green"};
        colour.choiceIndex = &g_colourChoice;
        colour.onChoice = [](const int i) {
            const Scripting::Vehicle vehicle = game::CurrentVehicle();
            if (vehicle != 0)
            {
                // The colour indices come from the game's carcols.dat.
                static const int kColours[] = {0, 111, 27, 64, 88, 50};
                Scripting::CHANGE_CAR_COLOUR(vehicle, kColours[i], kColours[i]);
            }
        };
        vehicles->add(colour);

        mliv::MenuItem lights;
        lights.label = "Lights";
        lights.kind = mliv::ItemKind::Choice;
        lights.choices = {"Automatic", "Always on", "Always off"};
        lights.choiceIndex = &g_lightChoice;
        lights.onChoice = [](const int i) {
            const Scripting::Vehicle vehicle = game::CurrentVehicle();
            if (vehicle != 0)
            {
                Scripting::FORCE_CAR_LIGHTS(vehicle, i);
            }
        };
        vehicles->add(lights);

        vehicles->add({"-- Toys --", mliv::ItemKind::Label});
        vehicles->add({"Freeze", mliv::ItemKind::Toggle,
                       [] {
                           const Scripting::Vehicle v = game::CurrentVehicle();
                           if (v != 0) { Scripting::FREEZE_CAR_POSITION(v, g_frozenVehicle ? 1 : 0); }
                       }, &g_frozenVehicle});
        vehicles->add({"Invisible", mliv::ItemKind::Toggle,
                       [] {
                           const Scripting::Vehicle v = game::CurrentVehicle();
                           if (v != 0) { Scripting::SET_CAR_VISIBLE(v, g_invisibleVehicle ? 0 : 1); }
                       }, &g_invisibleVehicle});
        vehicles->add({"Doors open", mliv::ItemKind::Action, [] {
            const Scripting::Vehicle v = game::CurrentVehicle();
            if (v != 0) { for (unsigned d = 0; d < 6; ++d) { Scripting::OPEN_CAR_DOOR(v, d); } }
        }});
        vehicles->add({"Doors closed", mliv::ItemKind::Action, [] {
            const Scripting::Vehicle v = game::CurrentVehicle();
            if (v != 0) { Scripting::CLOSE_ALL_CAR_DOORS(v); }
        }});
        vehicles->add({"Shoot out tyres", mliv::ItemKind::Action, [] {
            const Scripting::Vehicle v = game::CurrentVehicle();
            if (v != 0) { for (unsigned t = 0; t < 4; ++t) { Scripting::BURST_CAR_TYRE(v, t); } }
        }});
        vehicles->add({"Into the nearest car", mliv::ItemKind::Action, game::EnterNearestCar});
        vehicles->add({"Floats", mliv::ItemKind::Toggle, nullptr, &g_vehicleFlags.watertight});
        vehicles->add({"Stays undamaged", mliv::ItemKind::Toggle, nullptr, &g_vehicleFlags.noVisibleDamage});
        vehicles->add({"Drives through anything", mliv::ItemKind::Toggle, nullptr, &g_vehicleFlags.noCollision});
        vehicles->add({"Always leaves skid marks", mliv::ItemKind::Toggle, nullptr, &g_vehicleFlags.alwaysSkids});
        vehicles->add({"Blow up nearby cars", mliv::ItemKind::Action, [] {
            game::ForNearbyCars(40.0f, 24, [](const Scripting::Vehicle car) {
                Scripting::EXPLODE_CAR(car, 1, 0);
            });
        }});

        g_root->add(submenu("Vehicles", vehicles));

        // --- World ---
        auto world = std::make_shared<mliv::Menu>("World");

        mliv::MenuItem time;
        time.label = "Time of day";
        time.kind = mliv::ItemKind::Choice;
        time.choices = {"Midnight", "Morning", "Noon", "Evening", "Night"};
        time.choiceIndex = &g_timeChoice;
        time.onChoice = [](const int i) { game::SetTime(kTimes[i]); };
        world->add(time);

        mliv::MenuItem weather;
        weather.label = "Weather";
        weather.kind = mliv::ItemKind::Choice;
        weather.choices = {"Clear", "Sunny", "Cloudy", "Rain", "Fog", "Thunder"};
        weather.choiceIndex = &g_weatherChoice;
        weather.onChoice = [](const int i) { game::SetWeather(kWeathers[i]); };
        world->add(weather);

        mliv::MenuItem traffic;
        traffic.label = "Traffic";
        traffic.kind = mliv::ItemKind::Choice;
        traffic.choices = {"None", "Few", "Normal", "Many"};
        traffic.choiceIndex = &g_trafficChoice;
        world->add(traffic);

        world->add({"-- Teleport --", mliv::ItemKind::Label});

        mliv::MenuItem place;
        place.label = "Place";
        place.kind = mliv::ItemKind::Choice;
        place.choiceIndex = &g_placeChoice;
        for (const Place& p : kPlaces)
        {
            place.choices.emplace_back(p.name);
        }

        world->add(place);
        world->add({"Take me there", mliv::ItemKind::Action, [] {
            const Place& p = kPlaces[g_placeChoice];
            game::Teleport(p.x, p.y, p.z);
            mliv::LogLine("Teleport: %s", p.name);
        }});

        mliv::MenuItem timeScale;
        timeScale.label = "Time scale";
        timeScale.kind = mliv::ItemKind::Choice;
        timeScale.choices = {"Very slow", "Slow", "Normal", "Fast", "Very fast"};
        timeScale.choiceIndex = &g_timeScaleChoice;

        // Once on toggle, not per frame: unlike the density multipliers the time
        // scale holds by itself, and setting it every frame would fight with
        // cutscenes.
        timeScale.onChoice = [](const int i) { game::SetTimeScale(kTimeScales[i]); };
        world->add(timeScale);

        mliv::MenuItem parked;
        parked.label = "Parked cars";
        parked.kind = mliv::ItemKind::Choice;
        parked.choices = {"None", "Few", "Normal", "Many"};
        parked.choiceIndex = &g_parkedChoice;
        world->add(parked);

        world->add({"-- Display --", mliv::ItemKind::Label});

        // Switching off has to actively take it back: EnforceToggles merely
        // stops setting it, and the display would stay gone forever.
        world->add({"Hide the HUD", mliv::ItemKind::Toggle,
                    [] { if (!g_noHud) { Scripting::DISPLAY_HUD(1); } }, &g_noHud});
        world->add({"Hide the radar", mliv::ItemKind::Toggle,
                    [] { if (!g_noRadar) { Scripting::DISPLAY_RADAR(1); } }, &g_noRadar});
        world->add({"All headlights off", mliv::ItemKind::Toggle,
                    [] { if (!g_noVehicleLights) { Scripting::FORCE_ALL_VEHICLE_LIGHTS_OFF(0); } },
                    &g_noVehicleLights});

        g_root->add(submenu("World", world));

        // --- Movement ---
        auto motion = std::make_shared<mliv::Menu>("Movement");

        mliv::MenuItem noclip;
        noclip.label = "Fly";
        noclip.kind = mliv::ItemKind::Toggle;
        noclip.toggle = &g_noclip;
        noclip.onSelect = [] { game::SetNoclip(g_noclip, kGravities[g_gravityChoice]); };
        motion->add(noclip);

        mliv::MenuItem flySpeed;
        flySpeed.label = "Fly speed";
        flySpeed.kind = mliv::ItemKind::Choice;
        flySpeed.choices = {"Slow", "Normal", "Fast"};
        flySpeed.choiceIndex = &g_flySpeedChoice;
        motion->add(flySpeed);

        motion->add({"W A S D moves, space up, ctrl down", mliv::ItemKind::Label});

        mliv::MenuItem gravity;
        gravity.label = "Gravity";
        gravity.kind = mliv::ItemKind::Choice;
        gravity.choices = {"Normal", "Low", "Moon"};
        gravity.choiceIndex = &g_gravityChoice;
        gravity.onChoice = [](const int i) { game::SetGravity(kGravities[i]); };
        motion->add(gravity);

        motion->add({"To the waypoint", mliv::ItemKind::Action, [] {
            if (!game::TeleportToWaypoint())
            {
                mliv::LogLine("No waypoint set on the map.");
            }
        }});

        g_root->add(submenu("Movement", motion));

        // --- Pedestrians ---
        auto peds = std::make_shared<mliv::Menu>("Pedestrians");

        mliv::MenuItem density;
        density.label = "Pedestrians";
        density.kind = mliv::ItemKind::Choice;
        density.choices = {"None", "Few", "Normal", "Many"};
        density.choiceIndex = &g_pedChoice;
        peds->add(density);

        peds->add({"Everyone ignores you", mliv::ItemKind::Toggle, nullptr, &g_peacefulPeds});

        peds->add({"-- Chaos --", mliv::ItemKind::Label});
        peds->add({"Turn the nearest one on you", mliv::ItemKind::Action, game::ProvokeNearest});
        peds->add({"Explosion ahead", mliv::ItemKind::Action, game::ExplosionAhead});
        peds->add({"Kill everyone nearby", mliv::ItemKind::Action, game::KillNearby});

        g_root->add(submenu("Pedestrians", peds));

        // --- Settings ---
        auto settings = std::make_shared<mliv::Menu>("Settings");

        settings->add({"Lock game input while open", mliv::ItemKind::Toggle, nullptr, &g_lockInput});

        g_root->add(submenu("Settings", settings));

        g_menu = std::make_unique<mliv::MenuController>(g_root);
    }

    /// Game logic. Only runs while the game is processing its scripts.
    ///
    /// Has to live here and not in the drawing event: processScriptsEvent sets
    /// CTheScripts::m_pCurrentThread beforehand, drawingEvent does not. Natives
    /// need that script context. On top of that, per the SDK drawingEvent also
    /// runs in the menu and on the loading screen - where there is no script
    /// machine yet, and a GET_PLAYER_ID ends the game without a word.
    void OnScript()
    {
        PollInput();
        PollPad();

        // Player, ped and vehicle once for this tick. Everything below reads
        // them from there instead of asking the game over and over.
        game::BeginFrame();

        // Even with the menu closed: the switches should take effect, not only
        // while you are looking.
        EnforceToggles();
        ApplyNoclip();
        ApplyInputLock();

        // Drawing happens here, not in drawingEvent.
        //
        // The game's own scripts draw their HUD from the script tick; DRAW_RECT
        // and DISPLAY_TEXT are made for that and then land in the HUD phase.
        // Called from drawingEvent they run in the middle of a render phase -
        // and if the phone's render target happens to be bound at that moment,
        // they draw into its screen. That is exactly what it looked like.
        if (Scripting::IS_PAUSE_MENU_ACTIVE() == 0)
        {
            g_menu->draw(g_renderer);
        }
    }
}

/// Called by the SDK once it has hooked itself in.
///
/// The SDK checks the game version itself first and hooks nothing at all on an
/// unknown one - this function is then never called. Our own check stays
/// anyway: it goes into the log, and it records the condition where our code
/// needs it.
void plugin::gameStartupEvent()
{
    wchar_t self[MAX_PATH]{};
    GetModuleFileNameW(GetModuleHandleW(L"sauer.asi"), self, MAX_PATH);
    mliv::LogOpen(self);

    mliv::LogLine("%s, stage T6", kTrainerName);

    RemoveLegacyFiles(self);

    const mliv::GameInfo game = mliv::DetectGame();
    mliv::LogLine("Version: %ls (%s)",
                  game.raw.empty() ? L"(not readable)" : game.raw.c_str(),
                  mliv::Describe(game.version));

    if (!mliv::IsSupported(game.version))
    {
        mliv::LogLine("ABORT: this version is not supported.");
        return;
    }

    const mliv::Config config = LoadConfig(self);

    // What did not work out while reading goes into the log, not onto the
    // screen. A misspelled key otherwise shows up as "the key does nothing",
    // and then people look in the game instead of in the file.
    for (const std::string& problem : config.problems())
    {
        mliv::LogLine("Settings: %s", problem.c_str());
    }

    if (!config.flag("Log.Enabled", true))
    {
        mliv::LogLine("Log closed on request.");
        mliv::LogClose();
    }

    BindKeys(config);
    BindFlyKeys(config);

    LoadXInput();
    BindPad(config);

    g_renderer.configure(
        config.number("Menu.Left", 0.025f),
        config.number("Menu.Top", 0.12f),
        config.number("Menu.Width", 0.235f),
        config.number("Menu.Scale", 1.0f));

    BuildMenu();

    // Only this one event: input, switches and drawing all run in the script
    // context. See OnScript.
    plugin::processScriptsEvent::Add(OnScript);

    const std::string opener = mliv::KeyNameFromCode(g_keys.empty() ? VK_F7 : g_keys.front().code);
    mliv::LogLine("Menu ready. %s opens it, %zu key bindings active.",
                  opener.empty() ? "F7" : opener.c_str(), g_keys.size());
}

/// Called on unload. The SDK requires the function even though there is little
/// for it to do - without it an unresolved symbol remains.
void plugin::gameShutdownEvent()
{
    mliv::LogLine("Game is shutting down.");
    mliv::LogClose();
}

