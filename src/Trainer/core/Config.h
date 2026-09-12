#pragma once

#include <string>
#include <vector>

namespace mliv
{
    /// Turns a key name from the configuration into a virtual key code.
    /// 0 when the name is not recognised.
    ///
    /// The numbers are spelled out here rather than coming from Windows.h. That
    /// keeps this module free of platform headers and therefore testable without
    /// the game and without the Windows SDK - the same reason the menu logic does
    /// not know the game. The values have not changed since Windows 3.0.
    int KeyCodeFromName(const std::string& name);

    /// The other direction, for the template we write ourselves. Empty for an
    /// unknown code.
    std::string KeyNameFromCode(int code);

    /// The trainer's configuration.
    ///
    /// A plain INI format, because it can be edited without any tool and because
    /// it is what this scene uses. Not JSON: one lost brace there makes the whole
    /// file unreadable, and the user then sits in front of a trainer that falls
    /// back to default keys without explaining why.
    class Config
    {
    public:
        /// Reads text. Calling it repeatedly overwrites what occurs again and
        /// leaves the rest standing.
        void parse(const std::string& text);

        /// The key list for one action. Empty when nothing is set - the caller
        /// then uses its own default.
        ///
        /// Several keys per action is deliberate: a laptop without a numpad would
        /// otherwise be unusable.
        std::vector<int> keys(const std::string& action) const;

        bool flag(const std::string& name, bool fallback) const;

        float number(const std::string& name, float fallback) const;

        /// What did not work out while reading, in the user's language.
        ///
        /// Collected rather than thrown: an unknown key must not stop the trainer
        /// from starting, but passing over it silently is not allowed either -
        /// otherwise somebody goes looking for the bug in the game.
        const std::vector<std::string>& problems() const { return problems_; }

        /// The content the trainer writes when there is no file yet.
        static std::string DefaultText();

    private:
        struct Entry
        {
            std::string key;    ///< "section.name", lower case
            std::string value;
        };

        struct Binding
        {
            std::string action; ///< lower case
            std::vector<int> codes;
        };

        const std::string* find(const std::string& key) const;

        /// Resolves the key names of a [Keys] entry and reports what does not
        /// work out. Happens while reading, not while querying: otherwise a typo
        /// in an action nobody asks about would stay unnoticed forever, and which
        /// messages exist would depend on what the caller happened to read.
        void bind(const std::string& action, const std::string& value);

        std::vector<Entry> entries_;
        std::vector<Binding> bindings_;

        /// mutable because it is a log and not a statement about the content:
        /// that something did not work out while reading changes nothing about
        /// the query itself being a pure lookup.
        mutable std::vector<std::string> problems_;
    };
}
