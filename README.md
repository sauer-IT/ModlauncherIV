# Modlauncher IV

A guided downgrader and mod installer for GTA IV - and a trainer of its own.

- **Finds your game** - Steam, Epic or the Rockstar Games Launcher.
- **Downgrades the Complete Edition** to 1.0.7.0 or 1.0.8.0, the versions mods are
  made for, and switches between the two later on - taking out whatever will not
  run on the other one.
- **Installs mods from where their authors publish them**, every file checked
  against its SHA-256 before anything is written.
- **Backs up every change first.** Each mod can be taken back from the home page,
  and uninstalling offers to put the game back the way it was found.
- **sauer IV Trainer** - F8 on the keyboard, L3+R3 on a controller.
- **Play online** through GTA Connected, with a server list.

Nothing changes the game except the "Install" step in the wizard - and that only
after showing the plan, with a snapshot taken first.

> **Test phase.** So far it has run on one PC, with the Rockstar Games Launcher
> version of the game. Steam and Epic are detected by tested code, but no real
> installation of either has been in front of it yet.

## Download

One file: **ModlauncherIV.exe**, attached to the [latest release](../../releases/latest)
together with its SHA-256. Nothing to install first - a double click is enough.
You need your own copy of GTA IV; nothing here replaces the game.

Two things Windows says before the first start, both expected:

- **"Windows protected your PC"** - the *Run anyway* button is behind *More info*.
  The program is not signed with a certificate Windows knows yet; that is
  reputation, not a finding. More on that in [docs/windows.md](docs/windows.md).
- **Administrator rights** - GTA IV lives under `C:\Program Files`, and so do the
  backups that make every change undoable.

Four mods cannot be downloaded by any program - Nexus and MEGA only hand out
links a browser can follow. For those the wizard shows a **Download** button that
opens the right page; once the file is in your download folder it is found there
by its checksum, whatever it ended up being called.

## Disclaimer

Modlauncher IV is an unofficial fan project. It is not affiliated with, endorsed
by or sponsored by Rockstar Games or Take-Two Interactive. Grand Theft Auto and
GTA are trademarks of their respective owners.

You need your own, legitimately bought copy of GTA IV. This repository hosts no
game files and no mods: every download in the catalog comes from where its
authors publish it, and the mods remain the work of those authors.

The trainer is meant for single player. Use it, and the rest, at your own risk -
every change is backed up first, but keep a copy of your save games anyway.

## Documentation

| | |
|---|---|
| [docs/how-it-works.md](docs/how-it-works.md) | The shape of the program, how a path is planned, what a version change takes out, and what comes back when something goes wrong. |
| [docs/catalog.md](docs/catalog.md) | The recipes: which are proven, which are untested, and why the catalog is signed. |
| [docs/trainer.md](docs/trainer.md) | sauer IV Trainer - what it does and how it is built. |
| [docs/development.md](docs/development.md) | Building the launcher and the trainer, and the tests. |
| [docs/windows.md](docs/windows.md) | SmartScreen and Smart App Control. |
| [CONTRIBUTING.md](CONTRIBUTING.md) | Proposing a mod for the catalog, and how a pull request gets into a release. |
| [RELEASING.md](RELEASING.md) | How a release is built, signed and published. |

## Licence

GPL-3.0. See [LICENSE](LICENSE).
