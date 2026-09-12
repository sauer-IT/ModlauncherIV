#pragma once

#include <functional>
#include <memory>
#include <string>
#include <vector>

// Das Menue kennt das Spiel nicht.
//
// Struktur, Navigation und Zustand sind reine Logik; gezeichnet wird ueber
// IMenuRenderer, und bewegt wird es ueber abstrakte Eingaben. Das ist nicht
// Selbstzweck: so laesst sich das Menue vollstaendig ausserhalb des Spiels
// durchspielen. Ein Navigationsfehler, den man sonst erst nach Spielstart,
// Ladebildschirm und Tastendruck bemerkt, faellt hier in Millisekunden auf.
namespace mliv
{
    enum class MenuInput
    {
        None,
        Up,
        Down,
        Left,     ///< Wert verringern
        Right,    ///< Wert erhoehen
        Select,
        Back,
        Toggle,   ///< Menue oeffnen oder schliessen
    };

    enum class ItemKind
    {
        Action,     ///< Fuehrt etwas aus.
        Toggle,     ///< An oder aus.
        Choice,     ///< Eine Auswahl aus mehreren Werten.
        Submenu,    ///< Fuehrt eine Ebene tiefer.
        Label,      ///< Nur Text, nicht anwaehlbar.
    };

    class Menu;

    struct MenuItem
    {
        std::string label;
        ItemKind kind = ItemKind::Action;

        /// Wird bei Action und Toggle aufgerufen.
        std::function<void()> onSelect;

        /// Bei Toggle: der Zustand. Gehoert dem Aufrufer, nicht dem Menue -
        /// der Trainer haelt seine Schalter selbst.
        bool* toggle = nullptr;

        /// Bei Choice: die moeglichen Werte und der aktuelle Index.
        std::vector<std::string> choices;
        int* choiceIndex = nullptr;
        std::function<void(int)> onChoice;

        /// Bei Submenu: das Untermenue.
        std::shared_ptr<Menu> submenu;

        bool selectable() const { return kind != ItemKind::Label; }

        /// Was rechts neben dem Text steht - Zustand oder aktueller Wert.
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

        /// Bewegt die Auswahl. Ueberspringt Labels und laeuft am Rand um -
        /// wer unten steht und weiter drueckt, landet oben.
        void moveUp();
        void moveDown();

        const MenuItem* current() const;

    private:
        std::string title_;
        std::vector<MenuItem> items_;
        int selected_ = 0;

        void skipLabels(int direction);
    };

    /// Zeichnet das Menue. Die Umsetzung mit den Text-Natives des Spiels kommt
    /// spaeter; fuer Tests genuegt eine, die in die Konsole schreibt.
    class IMenuRenderer
    {
    public:
        virtual ~IMenuRenderer() = default;

        virtual void beginFrame() = 0;
        virtual void drawTitle(const std::string& text) = 0;
        virtual void drawItem(const std::string& label, const std::string& value, bool highlighted) = 0;
        virtual void endFrame() = 0;
    };

    /// Haelt fest, wo im Menuebaum wir gerade sind.
    class MenuController
    {
    public:
        explicit MenuController(std::shared_ptr<Menu> root);

        bool visible() const { return visible_; }
        Menu& active();

        void handle(MenuInput input);
        void draw(IMenuRenderer& renderer);

        /// Wie tief wir im Baum stehen. 0 = Wurzel.
        size_t depth() const { return stack_.size() - 1; }

    private:
        std::vector<std::shared_ptr<Menu>> stack_;
        bool visible_ = false;

        void select();
        void back();
        void adjust(int direction);
    };
}
