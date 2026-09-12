#pragma once

#include <string>
#include <vector>

namespace mliv
{
    /// The buttons of an XInput pad, as a bit mask.
    ///
    /// The numbers are XInput's own XINPUT_GAMEPAD_* values, spelled out here
    /// rather than included. Same reason as with the key codes in Config: this
    /// module stays free of platform headers and is therefore testable without
    /// the game and without the Windows SDK. They are part of a published ABI
    /// and cannot change.
    enum PadButton : unsigned short
    {
        PadNone          = 0x0000,
        PadUp            = 0x0001,
        PadDown          = 0x0002,
        PadLeft          = 0x0004,
        PadRight         = 0x0008,
        PadStart         = 0x0010,
        PadBack          = 0x0020,
        PadLeftStick     = 0x0040,
        PadRightStick    = 0x0080,
        PadLeftShoulder  = 0x0100,
        PadRightShoulder = 0x0200,
        PadA             = 0x1000,
        PadB             = 0x2000,
        PadX             = 0x4000,
        PadY             = 0x8000,
    };

    /// Turns one button name into its mask. PadNone when the name is unknown.
    ///
    /// Names are case-insensitive and several spellings are accepted, because
    /// what a button is called depends on the pad in your hands: A on an Xbox
    /// pad is Cross on a PlayStation one, and both end up on the same bit.
    unsigned short PadButtonFromName(const std::string& name);

    /// The other direction, for the template we write ourselves. Empty for a
    /// mask that is not exactly one known button.
    std::string PadNameFromButton(unsigned short button);

    /// A chord: several buttons that have to be held at the same time.
    ///
    /// "L3+R3" is one chord, and the reason chords exist at all. A pad has few
    /// buttons and the game already uses all of them, so a single button to open
    /// the menu would fire during normal play. Two at once does not happen by
    /// accident.
    ///
    /// Unknown names land in @p unknown and are left out, exactly as with the
    /// keyboard: one wrong name must not take the rest of the line with it.
    unsigned short PadChordFromNames(const std::string& text, std::vector<std::string>& unknown);
}
