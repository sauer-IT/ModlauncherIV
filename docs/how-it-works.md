# How it works

The shape of the program, and what it does when a version changes underneath
what is installed. For building and testing see [development.md](development.md).

## Layout

| Project | Purpose |
|---|---|
| `src/Launcher.Core` | Domain, pipeline and journey planning. No UI dependency, so it is testable against fixtures. |
| `src/Launcher.Cli` | Headless front end (`mliv`). Dry runs, diagnostics, CI. |
| `src/Launcher.App` | The program you start (WPF): home page and wizard. |
| `src/Trainer` | C++ ASI plugin, x86, IV-SDK. Player, weapons, vehicles, world, movement, peds, time and physics. |
| `catalog/` | The declarative recipes, thirteen of them, six proven - and in `tools/` the programs the launcher can fetch but not undo. |
| `tests/` | Fixtures, the test script, and `UiSmoke` - which builds every page of the window without showing one. |

**Journey planning** (`Core/Planning`) is the difference between the CLI and the
wizard. The CLI runs a recipe you name. The planner derives from "I want the
trainer" that a downgrade, an ASI loader and a runtime come first - and skips
whatever is already installed.

The point where it is decided: recipes are checked against the version the game
will have **after** the version change, not against the current one. Otherwise
every 1.0.7.0 recipe would be "unsuitable" for every user on the Complete
Edition - even though it is exactly what they want in the end. A recipe there is
not inapplicable, it is merely not its turn yet.

That is why the version change always comes first: a downgrade swaps hundreds of
files, and anything installed beforehand would afterwards be overwritten, or
half overwritten.

The logic lives in Core and not in the window, because a window cannot be tested
against fixtures - and because this is exactly where the errors live that a user
experiences as "the launcher wrecked my game". It is reachable without a UI
through `mliv journey`.

## What a version change takes out

Recipes are checked against the game version when they are installed, so
everything in the ledger fitted the game as it was. Move the game underneath
them - a downgrade, or a switch from 1.0.8.0 to 1.0.7.0 - and some of them no
longer do, while every file they installed stays exactly where it was put. The
installation looks untouched and the mods are silent, or worse: FusionFix left
on 1.0.7.0 keeps the game from starting.

This used to be listed on the plan page and left alone. Found in the game what
that costs - four mods to remove by hand before 1.0.7.0 would start - so the
plan now takes them back itself, from their snapshots, before the version
change: newest first, so a pack built on FusionFix comes out before FusionFix,
and anything that needs one of them goes with it. Their files stay in the
cache; switching back and installing them again downloads nothing. The same
happens without a version change when something installed already does not
fit. `mliv journey` shows them as `[take back]`.

## When a mod has moved on

The home page compares what is installed against what the catalog has, and says
so on the row: *newer in the catalog: 0.4.0+83a5a28a*, with an **Update** next to
Remove. The trainer made this worth having - its release changes with every
build of it, so an installation is behind within a day, and the page used to
show the installed version with nothing to compare it against. The wizard knew,
five clicks away.

Update does not install anything by itself. It ticks that recipe and opens the
wizard, because bringing one up to date is an ordinary run: dependencies may
have moved with it, files it no longer installs have to be cleaned up, and there
is a plan to read and a question to answer before anything is written. What the
button saves is finding the right tick box.

## What comes back when it goes wrong

`%LOCALAPPDATA%\ModlauncherIV\launcher.log`, and a **Log** button at the bottom
of the home page that opens it. It holds what was found and with what version,
whether the catalog's signature verified, every step of the wizard, every file
fetched and from where, every recipe applied with its snapshot id, and every
failure with its stack.

This exists because the handout asked people to send a log and there was none:
the trainer wrote one, the launcher wrote nothing at all. What came back instead
was "it did not work", which is the least a person can say and the most they can
be expected to. Now the useful thing to send is one file, and the button that
opens it is on the page they are already looking at.

It rolls over at a megabyte, keeping one previous file - a crash should not
erase its own cause - and nothing in it throws. A launcher that fell over
because it could not write its own log would be a bad joke.

## Taking it off again

"Apps & features", *Modlauncher IV*, Uninstall - which starts the same EXE with
`--uninstall`. The order is the whole design: the mods come out of the game
first, the snapshots are offered for deletion second, and everything else after.
An uninstaller that cleaned up its own backups first would leave a modded game
with no way back and no program left to do it with.

**What decides whether anything is still owed is the ledgers, not detection.**
That distinction was a bug worth finding before somebody else did: detection
answers "which installations are on this machine now", and it deliberately
returns nothing when it finds two of them, because with two the user has to
choose. The uninstaller read that as "nothing is installed" and went straight on
to offering the snapshots for deletion - on a machine with two copies of GTA IV,
that meant throwing away the backups of a game still full of mods. The same held
for a game on a disk that was not plugged in.

The ledgers answer the question that actually matters: which folders is the
launcher still holding the earlier state of. Each one names its own game folder,
so a folder that is not reachable keeps its snapshots and says so, and the
offer to delete only comes when every installation came out clean.

The full project plan with architecture, risks and open questions exists as a
separate document.

