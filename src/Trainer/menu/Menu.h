#pragma once

#include <functional>
#include <memory>
#include <string>
#include <vector>

// The menu does not know the game.
//
// Structure, navigation and state are pure logic; drawing goes through
// IMenuRenderer, and it is moved through abstract inputs. That is not for its
// own sake: it lets the menu be played through entirely outside the game. A
// navigation bug you would otherwise only notice after a game start, a loading
// screen and a key press shows up here in milliseconds.
namespace mliv
{
    enum class MenuInput
    {
        None,
        Up,
        Down,
        Left,     ///< decrease a value
        Right,    ///< increase a value
        Select,
        Back,
        Toggle,   ///< open or close the menu
    };

    enum class ItemKind
    {
        Action,     ///< Runs something.
        Toggle,     ///< On or off.
        Choice,     ///< A choice among several values.
        Submenu,    ///< Leads one level deeper.
        Label,      ///< Text only, not selectable.
    };

    class Menu;

    struct MenuItem
    {
        std::string label;
        ItemKind kind = ItemKind::Action;

        /// Called for Action and Toggle.
        std::function<void()> onSelect;

        /// For Toggle: the state. Owned by the caller, not by the menu - the
        /// trainer keeps its own switches.
        bool* toggle = nullptr;

        /// For Choice: the possible values and the current index.
        std::vector<std::string> choices;
        int* choiceIndex = nullptr;
        std::function<void(int)> onChoice;

        /// For Submenu: the submenu.
        std::shared_ptr<Menu> submenu;

        bool selectable() const { return kind != ItemKind::Label; }

        /// What sits to the right of the text - state or current value.
        std::string value() const;
    };

    class Menu
    {
    public:
        explicit Menu(std::string title) : title_(std::move(title)) {}

        const std::string& title() const { return title_; }
        const std::vector<MenuItem>& items() const { return items_; }
        int selected() const { return selected_; }

        Menu& add(MenuItem item);

        /// Moves the selection. Skips labels and wraps at the ends - pressing
        /// down at the bottom takes you to the top.
        void moveUp();
        void moveDown();

        const MenuItem* current() const;

    private:
        std::string title_;
        std::vector<MenuItem> items_;
        int selected_ = 0;

        void skipLabels(int direction);
    };

    /// Draws the menu. The implementation using the game's text natives lives
    /// in the plugin; for tests one that writes to the console is enough.
    class IMenuRenderer
    {
    public:
        virtual ~IMenuRenderer() = default;

        virtual void beginFrame(int itemCount) = 0;
        virtual void drawTitle(const std::string& text) = 0;

        /// @param selectable  false for a heading: it is drawn, but never
        ///                    highlighted, and reads as a divider rather than
        ///                    as something that can be picked.
        virtual void drawItem(const std::string& label, const std::string& value,
                              bool highlighted, bool selectable) = 0;

        /// One line under the entries: where we are, and how to get out.
        virtual void drawFooter(const std::string& text) = 0;

        virtual void endFrame() = 0;
    };

    /// Keeps track of where in the menu tree we currently are.
    class MenuController
    {
    public:
        explicit MenuController(std::shared_ptr<Menu> root);

        bool visible() const { return visible_; }
        Menu& active();

        void handle(MenuInput input);
        void draw(IMenuRenderer& renderer);

        /// How deep we stand in the tree. 0 = the root.
        size_t depth() const { return stack_.size() - 1; }

    private:
        std::vector<std::shared_ptr<Menu>> stack_;
        bool visible_ = false;

        void select();
        void back();
        void adjust(int direction);
    };
}
