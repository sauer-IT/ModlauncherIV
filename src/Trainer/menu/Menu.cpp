#include "Menu.h"

namespace mliv
{
    std::string MenuItem::value() const
    {
        switch (kind)
        {
            case ItemKind::Toggle:
                return toggle != nullptr && *toggle ? "AN" : "AUS";

            case ItemKind::Choice:
                if (choiceIndex != nullptr &&
                    *choiceIndex >= 0 &&
                    static_cast<size_t>(*choiceIndex) < choices.size())
                {
                    return choices[static_cast<size_t>(*choiceIndex)];
                }

                return "";

            case ItemKind::Submenu:
                return ">";

            default:
                return "";
        }
    }

    Menu& Menu::add(MenuItem item)
    {
        items_.push_back(std::move(item));

        // Der erste anwaehlbare Eintrag wird ausgewaehlt. Startet ein Menue mit
        // einer Ueberschrift, stuende die Auswahl sonst auf etwas, das man gar
        // nicht anwaehlen kann.
        if (items_[static_cast<size_t>(selected_)].selectable() == false)
        {
            skipLabels(+1);
        }

        return *this;
    }

    const MenuItem* Menu::current() const
    {
        if (items_.empty() || selected_ < 0 ||
            static_cast<size_t>(selected_) >= items_.size())
        {
            return nullptr;
        }

        return &items_[static_cast<size_t>(selected_)];
    }

    void Menu::moveUp()
    {
        if (items_.empty())
        {
            return;
        }

        selected_ = selected_ == 0 ? static_cast<int>(items_.size()) - 1 : selected_ - 1;
        skipLabels(-1);
    }

    void Menu::moveDown()
    {
        if (items_.empty())
        {
            return;
        }

        selected_ = static_cast<size_t>(selected_ + 1) >= items_.size() ? 0 : selected_ + 1;
        skipLabels(+1);
    }

    /// Sucht in der angegebenen Richtung den naechsten anwaehlbaren Eintrag.
    /// Die Begrenzung auf die Listenlaenge ist wichtig: besteht ein Menue nur
    /// aus Labels, drehte sich das sonst endlos.
    void Menu::skipLabels(const int direction)
    {
        const int count = static_cast<int>(items_.size());
        for (int i = 0; i < count; ++i)
        {
            if (items_[static_cast<size_t>(selected_)].selectable())
            {
                return;
            }

            selected_ += direction;
            if (selected_ < 0)
            {
                selected_ = count - 1;
            }
            else if (selected_ >= count)
            {
                selected_ = 0;
            }
        }
    }

    // ------------------------------------------------------------ Controller

    MenuController::MenuController(std::shared_ptr<Menu> root)
    {
        stack_.push_back(std::move(root));
    }

    Menu& MenuController::active()
    {
        return *stack_.back();
    }

    void MenuController::handle(const MenuInput input)
    {
        if (input == MenuInput::Toggle)
        {
            visible_ = !visible_;
            return;
        }

        if (!visible_)
        {
            return;
        }

        switch (input)
        {
            case MenuInput::Up:     active().moveUp();   break;
            case MenuInput::Down:   active().moveDown(); break;
            case MenuInput::Left:   adjust(-1);          break;
            case MenuInput::Right:  adjust(+1);          break;
            case MenuInput::Select: select();            break;
            case MenuInput::Back:   back();              break;
            default: break;
        }
    }

    void MenuController::select()
    {
        const MenuItem* item = active().current();
        if (item == nullptr)
        {
            return;
        }

        switch (item->kind)
        {
            case ItemKind::Submenu:
                if (item->submenu)
                {
                    stack_.push_back(item->submenu);
                }

                break;

            case ItemKind::Toggle:
                if (item->toggle != nullptr)
                {
                    *item->toggle = !*item->toggle;
                }

                if (item->onSelect)
                {
                    item->onSelect();
                }

                break;

            case ItemKind::Action:
                if (item->onSelect)
                {
                    item->onSelect();
                }

                break;

            default:
                break;
        }
    }

    /// Eine Ebene zurueck. Auf der Wurzel schliesst es das Menue - sonst sitzt
    /// man dort fest und muss die Menuetaste suchen.
    void MenuController::back()
    {
        if (stack_.size() > 1)
        {
            stack_.pop_back();
            return;
        }

        visible_ = false;
    }

    void MenuController::adjust(const int direction)
    {
        const MenuItem* item = active().current();
        if (item == nullptr || item->kind != ItemKind::Choice ||
            item->choiceIndex == nullptr || item->choices.empty())
        {
            return;
        }

        const int count = static_cast<int>(item->choices.size());
        int next = *item->choiceIndex + direction;

        if (next < 0)
        {
            next = count - 1;
        }
        else if (next >= count)
        {
            next = 0;
        }

        *item->choiceIndex = next;

        if (item->onChoice)
        {
            item->onChoice(next);
        }
    }

    void MenuController::draw(IMenuRenderer& renderer)
    {
        if (!visible_)
        {
            return;
        }

        Menu& menu = active();

        renderer.beginFrame();
        renderer.drawTitle(menu.title());

        const auto& items = menu.items();
        for (size_t i = 0; i < items.size(); ++i)
        {
            renderer.drawItem(
                items[i].label,
                items[i].value(),
                static_cast<int>(i) == menu.selected());
        }

        renderer.endFrame();
    }
}
