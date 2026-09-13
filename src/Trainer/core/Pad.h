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
    /// The buttons, and four directions per stick on top of them.
    ///
    /// The stick bits are ours, not XInput's - the mask it hands over is 16
    /// bits of buttons and has no room left. They start above it so the two
    /// never collide, which is also why this is 32 bits wide now.
    enum PadButton : unsigned
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

        PadLStickUp      = 0x00010000,
        PadLStickDown    = 0x00020000,
        PadLStickLeft    = 0x00040000,
        PadLStickRight   = 0x00080000,

        PadRStickUp      = 0x00100000,
        PadRStickDown    = 0x00200000,
        PadRStickLeft    = 0x00400000,
        PadRStickRight   = 0x00800000,
    };

    /// Turns the two axes of one stick into the four directions above.
    ///
    /// A stick is not a button and has to be made into one somewhere. That
    /// somewhere is here, in a function that takes two numbers and returns a
    /// mask, so it can be tested without a controller in anybody's hands.
    ///
    /// Two thresholds, not one: a stick held near the edge of a single one
    /// would flicker between pressed and released several times a second, and
    /// the menu would jump two entries where the hand moved once. It counts as
    /// pressed above the upper and stays pressed until it falls below the
    /// lower - which is why the mask from the frame before goes in.
    ///
    /// Diagonals report both directions. The menu ignores left and right on a
    /// list and up and down on a slider, so a slightly crooked hand still does
    /// what it meant to.
    unsigned StickDirections(int x, int y, unsigned previous, unsigned upBit, unsigned downBit,
                             unsigned leftBit, unsigned rightBit);

    /// Turns one button name into its mask. PadNone when the name is unknown.
    ///
    /// Names are case-insensitive and several spellings are accepted, because
    /// what a button is called depends on the pad in your hands: A on an Xbox
    /// pad is Cross on a PlayStation one, and both end up on the same bit.
    unsigned PadButtonFromName(const std::string& name);

    /// The other direction, for the template we write ourselves. Empty for a
    /// mask that is not exactly one known button.
    std::string PadNameFromButton(unsigned button);

    /// A chord: several buttons that have to be held at the same time.
    ///
    /// "L3+R3" is one chord, and the reason chords exist at all. A pad has few
    /// buttons and the game already uses all of them, so a single button to open
    /// the menu would fire during normal play. Two at once does not happen by
    /// accident.
    ///
    /// Unknown names land in @p unknown and are left out, exactly as with the
    /// keyboard: one wrong name must not take the rest of the line with it.
    unsigned PadChordFromNames(const std::string& text, std::vector<std::string>& unknown);
}
