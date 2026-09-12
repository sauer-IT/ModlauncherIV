#include "Menu.h"

namespace mliv
{
    std::string MenuItem::value() const
    {
        switch (kind)
        {
            case ItemKind::Toggle:
                return toggle != nullptr && *toggle ? "ON" : "OFF";

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

        // The first selectable entry gets selected. If a menu starts with a
        // heading, the selection would otherwise sit on something that cannot
        // be selected at all.
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

    /// Looks for the next selectable entry in the given direction.
    /// Bounding it by the list length matters: a menu consisting only of labels
    /// would otherwise spin forever.
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

    /// One level back. At the root it closes the menu - otherwise you are stuck
    /// there and have to hunt for the menu key.
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
        const auto& items = menu.items();

        // The count comes first because a renderer has to size the background to
        // match - and it has to do so before the text lands on it. Surfaces drawn
        // later would sit on top.
        renderer.beginFrame(static_cast<int>(items.size()));
        renderer.drawTitle(menu.title());

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
