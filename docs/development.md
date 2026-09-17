# Development

Building the launcher and the trainer, and the tests that have to pass before
a release. See also [RELEASING.md](../RELEASING.md) and [CONTRIBUTING.md](../CONTRIBUTING.md).

## Prerequisites

- **.NET 10 SDK** - for Launcher.Core and Launcher.Cli
- **Visual Studio Build Tools with C++ (x86)** - for the trainer. `build-trainer.ps1`
  fetches the SDK and the D3DX headers itself and checks them by SHA-256.

## Building and running

```
.\scripts\package.ps1                  the shippable release: one single EXE
.\artifacts\release\ModlauncherIV.exe  the program

.\scripts\run.ps1 detect               call the CLI
.\scripts\play.ps1                     start GTA IV, past the Rockstar launcher
.\scripts\pack-trainer.ps1             build the trainer and make it installable
.\scripts\pack-vc80.ps1                fetch the VC++ 2005 runtime from Microsoft
.\scripts\build-trainer.ps1 -Deploy    build the trainer and drop it in by hand
.\scripts\check-sources.ps1            are the catalog's downloads still there?
.\scripts\make-icon.ps1                regenerate the application icon
```

`package.ps1` produces **one self-contained file** of around 62 MB: the .NET
runtime, the catalog and the shipped trainer all sit inside. That costs disk
space but spares the user exactly what such tools usually fail on - an error
message about a missing runtime instead of a program. On the first start the
program offers to copy itself to `%LOCALAPPDATA%\Programs\ModlauncherIV` and to
put a shortcut on the desktop and into the start menu.

The program requires **administrator rights**, and from the outset. On Steam as
on Rockstar, GTA IV sits under `C:\Program Files`; writing there means elevated
rights, and that holds for the snapshot too, without which there is no rollback.
Relaunching only once something has to be written would mean: the wizard from the
top, the selection gone, and in the worst case an elevation in the middle of a
transaction.

It takes the catalog and the shipped payload from **its own folder**, not from
the working directory - a program started through a shortcut has an arbitrary
one and would then not find its own recipes.

**Multiplayer through GTA Connected**, and it can be installed from here - but
not as a recipe. The catalog has a second kind of entry for it, in
`catalog/tools/`, and the difference between the two kinds is the whole point.

A **recipe** is a change to the game directory. Every path it names goes through
the containment check, everything it writes is in a snapshot first, and it can be
taken back file by file. A **tool** is a program that installs itself somewhere
else entirely - under `%LOCALAPPDATA%` in this case - and ships as an installer.
The launcher fetches it and checks it against its checksum, exactly as it does
every recipe source, because that part is the same problem. Then it hands over,
and from that point it is somebody else's program writing where it likes. The
launcher cannot undo that, and says so before starting anything.

Listing it among the mods would have made that list claim something untrue about
what can be taken back again, so it has its own heading on the home page - *PLAY
ONLINE* - with Install when it is absent and Start when it is there. The
heading says what the thing is for rather than what it is, because that is what
anyone is looking for when they read it.

Both kinds live under `catalog/` and are covered by the same signature: the index
spans every `.json` below that directory, so a tool cannot be added or its URL
changed without breaking it. That matters more here than for a recipe, because
this one ends in an executable being started - which is also why a tool is
offered only when the signature actually verified.

Detection comes out of the signed catalog too, not out of the code: a registry
key is as much part of "which program is this" as a checksum is. The key it reads
is the one that program's own launcher reads, and the check asks for the file
rather than the key, because a key outlives an uninstall.

**"Play online" asks where to**, rather than handing straight over. The page it
opens is a list of servers with a Connect on each, out of three sources:

- **What is up right now**, from GTA Connected's own master list. Its client
  asks `serverlisting.gtaconnected.com` over a WebSocket with the sub-protocol
  `ws_masterlist1`; the page that host serves does the same thing in JavaScript,
  which is where the frame layout was read from rather than guessed. Both ends
  use .NET's conventions - a 7-bit encoded length in front of every string and
  number - so the reader is a few lines. Names, player counts and game modes
  come with it, and the busiest server is at the top.
- **What was kept here**, in `%LOCALAPPDATA%\ModlauncherIV\servers.json`. A
  history is not a choice: it forgets, it is ordered by accident, and a server
  nobody has been on yet is never in it. An address typed in once is in the list
  from then on, with a name if it was given one.
- **Where the client was last**, out of its own `History.xml` - the same file its
  own browser is built from, so a server dropped over there disappears here too.

Each address appears once, with the best that is known about it, and the row
says which of the three it came from.

**Connecting is the client's own URL.** It registers a gtac: protocol whose
handler is its own launcher with the whole URL as one argument, and the server
list on its own site builds exactly `gtac://connect/<address>/gta:iv`. The URL
carries the game; the /connect switch does not, and the client serves six games,
so "connect to 1.2.3.4" without saying to what is a question it cannot answer -
which is the likeliest reason Connect used to do nothing at all. /silent went
with it: it hides the launcher window, which is where the client asks for a
player name or a path to the game, and hidden, a first start looks identical to
a broken one. The page now says who you will be and what will start, and when
either is missing it says which and points at the button that opens the client.

**The master list belongs to somebody else and nobody has promised it will stay
put.** So every failure is the same failure: an empty list, the reason, and the
button next to it that opens the client's own server browser - which is where
this list would have come from anyway. Ten seconds is the longest it may take;
somebody has just clicked "play".

An address is checked before it is stored, not when it is used: it ends up on a
command line, so what may be in it is what may be in a host name and a port and
nothing else. That check runs on what the master list sends as well, because an
entry there is written by a stranger, and again when the file is read back,
because a file this program wrote is still a file somebody can edit.

The switches are the client's own - `/connect <server>` and `/silent`, read out
of its launcher's help text rather than guessed.

One thing gets said first, on both paths, because it is true and invisible: that
registry key also records which `GTAIV.exe` the client will start, and it need
not be the installation this launcher looks after - in which case nothing
installed here applies to what actually runs.

**What used to stand next to it was wrong.** It said everything in `plugins\`
comes along into multiplayer, the trainer included, and that this would get you
thrown off a server. That was reasoned rather than tried. Measured, the trainer
does not load under the client at all: it starts the game itself and decides
what goes into it. The warning is gone rather than corrected - there is nothing
there to warn about, and a warning that is not true is worse than none.

It is also the right way round. A trainer on a server with other people on it is
cheating, and working around a client's own decision about what it loads is not
something this project is going to do.

**The trainer does not start separately.** It sits in the game directory as
`plugins\sauer.asi` and is loaded along by the ASI loader when
the game starts. In game, `F8` opens the menu.

Commands:

```
detect                 look for installations, print a diagnostic report
catalog                list the available recipes
plan   <recipe-id>     show what a recipe would do - changes nothing
fetch  <recipe-id>     download the needed files and check them by SHA-256
apply  <recipe-id>     run a recipe, after a prompt and with a snapshot
remove <recipe-id>     take a recipe back out. --all for everything, newest first
status                 what the launcher changed about this installation
route  [version]       which way leads to another game version
journey <id,id,...>    the full path to the desired state, with dependencies
guard                  whether the platform can patch the game back
verify                 whether everything still sits the way the launcher left it

catalog-key            generate a signing key pair
catalog-sign           index and sign the catalog
```

Exit codes: `0` success - `1` nothing found - `2` wrong invocation -
`3` blockers found, nothing executed - `4` output not writable -
`5` execution failed.

## Tests

```
.\tests\run-tests.ps1
```

300 tests against fake game directories. No real installation is touched. If GTA
IV happens to be running, the script aborts up front - otherwise every pre-flight
rightly blocks and four tests fail without anything being wrong with the code.
Among the things covered:

- checksum protection and path escape out of the game directory
- that the dry run really changes nothing
- rollback after a failure in the middle of a recipe
- download over a mirror chain against a local HTTP server: the first source
  404s, the second delivers wrong content, the third is correct
- version graph: pathfinding across several downgrade edges
- journey planning: that a recipe for 1.0.7.0 is accepted on a Complete Edition
  as soon as the downgrade sits ahead of it in the same journey - and rejected
  when it does not. Plus circular dependencies, conflicts and unknown starting
  versions
- shipped payload: that a shipped file with a wrong checksum is rejected and does
  not even make it into the working directory
- update lock: detect an open Steam installation, set the switch, keep a backup
- counter-check: changed and deleted files are attributed to their recipe
- removal: newly created files disappear, overwritten ones get their old content
  back, recipes that are depended on are not removed
- catalog signature: unsigned is rejected, a recipe file changed afterwards is
  noticed, a forged signature is detected
- leftovers: a recipe that renames its file on an update gets the old one
  removed, says so in the dry run first, and a rollback brings it back
- shared files: a file a second recipe owns as well is neither deleted as a
  leftover nor reported as changed by the counter-check
- empty folders: the folders a recipe created go with it when it is removed, and
  a folder that was already there stays even when it ends up empty
- views: every page of the window is built once and laid out, with WPF's own
  binding trace treated as a failure. A mistyped resource key or a binding to a
  property that was renamed is not a build error - it is a page that looks
  broken five clicks into the wizard, and nothing else here would have caught it
- Steam and Epic: a library folder list as Steam writes it - escaped
  backslashes, the game in the second library, an entry for a disk that is gone
  - plus an Epic manifest folder in which one manifest belongs to another game
  and one is not even valid JSON. The Complete Edition's layout is in there
  too: the game sits in `GTAIV\` below the folder the store calls the
  installation, and both the search and a folder picked by hand have to arrive
  at the right one

**The script is deliberately pure ASCII.** PowerShell 5.1 reads a `.ps1` without
a BOM as CP1252; a UTF-8 em dash becomes, among other things, `”`, and that
counts as a string delimiter. The parser then silently slips out of alignment
from that point on.

## Milestones

- **M0** detection and diagnostic report ✔
- **M1** recipe engine, snapshot, rollback, ledger, dry run ✔
- **M2** acquisition, hash check, mirrors, catalog signature ✔
- **M3** downgrade recipes, version graph, update lock, counter-check, removal ✔
- **M4** base stack ✔ · trainer T0 ✔ · T1 menu scaffold ✔
- **M5** wizard ✔ · journey planning ✔ · trainer in the catalog ✔ ·
  T2a player ✔ · T2b weapons ✔ · T2c vehicles and world ✔ ·
  home page, self-install, icon, one-file release ✔ ·
  T3 configuration ✔ · T4-T6 movement, tuning, peds, time and physics ✔ ← *here*
- **M6** uninstaller ✔ · server list and log ✔ · profiles, catalog update ·
  mirrors for third-party sources

Open before a public release: SmartScreen without a real code-signing
certificate, third-party download sources that can vanish (the Dropbox already
did) with no mirrors of our own, and only ever tested on one machine. The
`vc80-runtime` step, which used to be the hard blocker because a stranger could
not supply the file, now comes from Microsoft.

## Ground rules

- The core never touches files directly - every change runs through the pipeline
  of pre-flight, snapshot, apply, verify, commit, with automatic rollback.
- A folder is deleted only when it is empty, and only when the snapshot says the
  recipe created it. Never recursively: a recipe that made `plugins\` does not
  thereby own what other recipes later put in it.
- Ledger and snapshots live under `%LOCALAPPDATA%\ModlauncherIV\`, not in the
  game directory.
- Updating a recipe removes what its previous version owned and the new one no
  longer writes. The ledger already knows those files, so no list of old names
  has to be maintained - and an ASI that gets renamed cannot stay behind and
  answer on the same key as its successor.
- No game files and no third-party mods are shipped along. Everything is
  downloaded from the original source and checked by SHA-256.
