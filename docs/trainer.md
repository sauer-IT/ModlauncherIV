# sauer IV Trainer

The trainer this project builds and ships: what it can do, how it is operated,
and how it is put together.


```
.\scripts\pack-trainer.ps1             build and make installable in the catalog
.\scripts\build-trainer.ps1 -Deploy    build and drop it into the game by hand
```

The usual route is the first: afterwards the trainer is offered in the wizard and
installed like any other mod - with snapshot, ledger and rollback. The second
route is for development, when you only want to see a change in game quickly.

`src/Trainer` is built into `sauer.asi`. Two constraints are not
convenience but a prerequisite:

- **x86.** GTA IV is 32-bit. An x64 DLL is ignored by the ASI loader without
  comment - the bug presents as "nothing happens".
- **Static C runtime (`/MT`).** A trainer that requires a redistributable would be
  out of place here of all things: a missing Visual C++ runtime is exactly what
  the game first failed on after the downgrade.

**The menu runs in game** - F8 opens it, the numpad or the arrow keys operate it,
toggles and choices react, actions run all the way through into the log file.

| Key | Effect |
|---|---|
| `F8` | open and close the menu |
| `Num 8` / `↑` · `Num 2` / `↓` | move the selection |
| `Num 4` / `←` · `Num 6` / `→` | change a value |
| `Num 5` / `Enter` | select |
| `Num 0` / `Backspace` | back |

**A controller works too**, alongside the keyboard rather than instead of it:

| Button | Effect |
|---|---|
| `L3` + `R3` | open and close the menu |
| D-pad | move the selection and change values |
| `A` | select |
| `B` | back |

The opener is a chord because a pad has few buttons and GTA IV already uses all
of them - a single button would fire during normal play. Both sticks clicked at
once does not happen by accident. Directions repeat while held, after a short
pause and then at a steady rate: on a pad you hold a direction, you do not tap it
sixty times.

**The phone stays in his pocket while the menu is open.** Up is the phone in GTA
IV and up is also how one walks through this menu, so every scroll took the
phone out over the top of the thing being scrolled - on the pad the same, with
d-pad up. While the menu is open the game is told a script has the phone, which
is what its own scripts do and what stops the button working; and if a press got
through anyway - the frame the menu opened, say - the phone is put away again.
Both are undone the moment the menu closes. A trainer that left the phone
disabled behind it would look exactly like a broken save.

XInput is loaded at run time rather than linked. Which `xinput` DLL exists
depends on the Windows version, and linking one would make the trainer refuse to
load where it is absent - which the ASI loader reports as nothing happening at
all. This way: no DLL, no controller, everything else still works. All four pad
slots are polled, because a pad does not have to sit in slot 0 and after a
reconnect usually does not.

What it cannot do is swallow the presses. They reach the game as well, so opening
the menu also does something in the game and scrolling switches weapons
underneath. **Settings → "Lock game input while open"** turns that off by taking
the controls away for as long as the menu is up. Off by default: being frozen in
traffic is the more drastic of the two annoyances, and which one you prefer is
not ours to decide.

Key bindings, controller buttons and the menu's position and scale live in
`sauer.ini`, which is written next to the game on the first start. The file is
plain INI with the sections `[Keys]`, `[Pad]`, `[Menu]` and `[Log]`, and pure
ASCII: it lands next to the game and gets opened with whatever happens to be
around. Button names accept both dialects - `A` and `Cross` are the same bit,
because what a button is called depends on the pad in your hands.

The **menu logic knows nothing about the game** - structure, navigation and state
live in `menu/`, drawing goes through `IMenuRenderer`, movement through abstract
inputs. That makes it possible to play the whole menu through without the game:

```
.\scripts\build-trainer.ps1 -Test      120 tests, without GTA IV
```

A navigation bug shows up in milliseconds that way, instead of after a game
start, a loading screen and a key press.

**State T6.** Roughly sixty options. The root holds **nothing but the eight
categories**, each of which opens its own submenu:

| Category | Content |
|---|---|
| Player | godmode, health, armour, invisible, jump to camera, skins, traits |
| Weapons | pick one and get it, all weapons, take them away, infinite ammo, skill |
| Wanted | level, never wanted, upper limit, clear cops, no new patrols, police interest |
| Money | amount and give |
| Vehicles | spawn ten models, repair, indestructible, tuning, paint |
| World | time of day, hold the clock, weather, traffic density, jump to five places |
| Movement | fly, superjump, run speed, map marker, three saved places |
| Pedestrians | density, everyone ignores you, riot, panic, clear the area, armed companion |
| Settings | lock game input while the menu is open |

It used to be a flat list that mixed about twenty single entries with a handful
of submenus, so reaching the world settings meant scrolling past health and
money. At sixty options a flat list stops being a list and becomes a search.

Some of it is not obvious:

- Spawned models go through `CStreaming::ScriptRequestModel`, not through
  `REQUEST_MODEL` - that one is commented out in the SDK. `CREATE_CAR` with a
  model that is not loaded does not create a vehicle, it **ends the game**; so it
  is checked beforehand and, in doubt, only logged.
- **Traffic density is set anew every frame.** The game turns the multipliers back
  to `1.0` every frame, so setting them once would have no effect. The "normal"
  level is the default value and touches nothing.
- Weather changes with `FORCE_WEATHER_NOW` rather than `FORCE_WEATHER`: the latter
  cross-fades over minutes and, seen from a menu, simply looks broken.
- **Noclip** is not a teleport. The ped keeps its physics handle; collision goes
  off, gravity goes to zero, and the movement runs through `SET_CHAR_VELOCITY`
  every frame. Velocity is in units per second, so the speed is the same
  regardless of frame rate - the earlier version, which moved the ped by a fixed
  distance per frame, flew at double speed on a 120 Hz display.
- **Teleporting** does three things in an order that matters. It asks the
  streamer for the target area and waits, because a ground query against a world
  that is not loaded answers `0.0` - which is sea level, and below most of the
  city. It looks the ground up from 1200 units above rather than from where the
  player is. And it moves the *vehicle* when there is one: moving the ped out of
  a moving car leaves the car behind and the player rolling down the street.
- **Changing skin** takes the same streaming detour as a vehicle, for the same
  reason, and afterwards releases the model again - otherwise every skin tried
  stays in memory. The new ped is a new handle, so the sticky switches are gone;
  nothing special is needed for that, because re-asserting on a changed handle is
  what they already do.
- **Saved places are kept for the session only.** Writing them out would mean a
  second file format next to the settings, with its own parsing and its own
  failure cases, for something whose whole use is "mark this spot, go and cause
  trouble, come back". Quitting the game ends that errand anyway.

The menu is drawn with `DRAW_RECT` and the text natives and nothing else - no
D3D9 hook. A hook would look better and is one of the most common causes of
crashes in this scene, because it collides with DXVK, with overlays and with
other mods. What can be had inside that limit: a shadow behind the panel, a
hairline in the accent colour along the left edge and under the header, a bar
marking the selected row, drop shadows on the text so it survives a bright sky
behind it, headings drawn as dividers rather than as entries, and a footer
counting the position. The footer counts only what can be picked - counting
dividers would make the number disagree with what the eye sees moving.

Sticky switches such as godmode are **set again when the handle changes**, not
only when toggled - the game takes invulnerability back on respawn, in cut scenes
and at mission changes, and the ped handle itself changes on death or a model
change. A switch set once would silently stop working, and you would then take
the trainer for broken instead of the game for wilful.

Writing them *unconditionally* every frame, which is what this did at first, is
worse than merely wasteful. Every switch that is off then writes its "off" value
over the game every frame, and not all of those are what the game would have
done by itself: with "shoot from vehicles" off it kept calling
`SET_PLAYER_CAN_DO_DRIVE_BY(0)`, so the trainer quietly took drive-bys away from
a player who had never touched the setting. The weapon skill had the same shape,
and now has an "As in the game" setting that writes nothing at all.

So the switches are now written **on change**, and re-asserted when the ped or
the vehicle handle changes. Together with resolving player, ped and vehicle
**once per tick** - `LocalPed` alone costs four natives and the per-frame path
asked for it half a dozen times over - an idle trainer went from around thirty
natives a frame to none.

**Everything runs in `processScriptsEvent`** - input, toggles and drawing. That
was the outcome of two bugs that only showed up in game:

- Game natives from `drawingEvent` **end the game on the loading screen.** Only
  `processScriptsEvent` sets `CTheScripts::m_pCurrentThread` beforehand, the
  context natives need; and per the SDK `drawingEvent` also runs in the menu and
  while loading, where there is no script machine yet.
- Drawing from `drawingEvent` ends up **on the screen of the phone** as soon as
  its render target is bound. The game's own scripts draw their HUD from the
  script tick as well - from there `DRAW_RECT` and `DISPLAY_TEXT` land in the HUD
  phase, where they belong.

**Two traps when drawing**, both only visible in game:

- In GTA IV `DRAW_RECT` takes **centre and size**, not two corners - the parameter
  names in the SDK (`x1, y1, x2, y2`) suggest otherwise. Fed with corners, the
  rectangles land visibly off.
- `beginFrame` is given the number of entries, because the background has to be
  drawn **before** the text lands on it. Areas drawn later would sit on top.

The `VersionAdapter` checks the game version on load and **aborts if it is not
supported**. On another version the native hashes and memory addresses do not
match, and writing to wrong addresses does not show up immediately but later and
somewhere else entirely. For the same reason the recipe's `appliesToVersions`
lists 1.0.7.0 only: otherwise the wizard would happily install the trainer on
1.0.8.0 and the user would end up with a file in the plugins folder that silently
does nothing.

Work does not happen in `DllMain` but in a thread of its own - Windows holds the
loader lock there, and anyone doing more than the bare minimum risks a deadlock
that presents as "hangs on game start".

The log file is called `sauer.log` and is flushed after every
line; otherwise, after a crash, the one line that would have given away the
reason is exactly the one missing.

It is created next to the DLL first - that is where people look for it. If the
game sits under `Program Files` and runs without elevated rights, that fails, and
it then falls back to `%LOCALAPPDATA%\ModlauncherIV\Trainer.log`. **On this
installation it is exactly the fallback path that is used.** A trainer without a
log file is, when there is a problem, as mute as one that never loaded at all.

The ASI loads, recognises 1.0.7.0 and reports for duty:

```
[14:25:53.944] sauer IV Trainer, stage T6
[14:25:53.945] Version: 1.0.7.0 (1.0.7.0)
[14:25:53.946] Menu ready. F8 opens it, 13 key bindings active.
```

