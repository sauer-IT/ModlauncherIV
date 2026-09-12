# Modlauncher IV

A guided downgrader and mod installer for GTA IV - and a self-built trainer that
it ships at the end.

**State: M5 complete, trainer at T6, clickable release** - the program takes you
from an untouched game to a running trainer without ever touching a command
line, and it installs itself to the desktop on request.

The complete path has been run against a real installation: Complete Edition
1.2.0.59 through the Rockstar Games Launcher, downgraded to 1.0.7.0, plus ASI
loader, GFWL stub, Visual C++ 2005 runtime and the self-built trainer - 197 files
under management, all verified unchanged by a counter-check.

The only thing that changes the game is the "Install" step in the wizard, or
`apply` in the CLI - both only after a prompt and with a snapshot taken first.

## Layout

| Project | Purpose |
|---|---|
| `src/Launcher.Core` | Domain, pipeline and journey planning. No UI dependency, so it is testable against fixtures. |
| `src/Launcher.Cli` | Headless front end (`mliv`). Dry runs, diagnostics, CI. |
| `src/Launcher.App` | The program you start (WPF): home page and wizard. |
| `src/Trainer` | C++ ASI plugin, x86, IV-SDK. Player, weapons, vehicles, world, movement, peds, time and physics. |
| `catalog/` | The declarative recipes, twelve of them, six proven - and in `tools/` the programs the launcher can fetch but not undo. |
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

Two things get said first, on both paths. That registry key also records which
`GTAIV.exe` it will start, and it need not be the installation this launcher
looks after - in which case nothing installed here applies to what actually
runs. And the ASI loader does not care what the game is being used for:
everything in `plugins\` loads in multiplayer too, the trainer included. On a
server that is a good way to be thrown off it, and a good way to crash.

Saying it on both paths is deliberate. Skipping it on the shorter one would mean
the quick way is the way that warns about nothing.

**The trainer does not start separately.** It sits in the game directory as
`plugins\sauer.asi` and is loaded along by the ASI loader when
the game starts. In game, `F7` opens the menu.

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

## Tests

```
.\tests\run-tests.ps1
```

259 tests against fake game directories. No real installation is touched. If GTA
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

## The catalog - state and caveats

Twelve recipes with **real, self-computed SHA-256 checksums**. Six of them have
**run against a real installation** - Complete Edition 1.2.0.59 through the
Rockstar Games Launcher, downgraded to 1.0.7.0. The game starts.

| Recipe | Source | Size | State |
|---|---|---|---|
| `downgrade-ce-1070` | GitHub release of the Gillian guide project | 111 MB | run |
| `downgrade-ce-1080` | same release | 109 MB | untested |
| `ultimate-asi-loader` | ThirteenAG, GitHub release | 928 KB | run |
| `gfwl-stub` | FusionFix Legacy Addon, GitHub release | 4.0 MB | run |
| `vc80-runtime` | Microsoft, signed redistributable | 1.8 MB | run |
| `mliv-trainer` | shipped, self-built | 205 KB | run |
| `fusionfix` | ThirteenAG, GitHub release | 197 MB | 1.0.8.0 only - crashes 1.0.7.0 |
| `xbox-rain-droplets` | ThirteenAG, GitHub release | 365 KB | untested |
| `various-fixes` | valentyn-l, GitHub release | 1.7 GB | 1.0.8.0 only, untested |
| `hires-vehicle-pack` | Ash735, supplied by hand | 140 MB | 1.0.8.0 only, untested |
| `hires-misc-pack` | Ash735, supplied by hand | 365 MB | 1.0.8.0 only, untested |
| `libertys-legacy` | Const96b, supplied by hand | 0.7 MB | untested |

**FusionFix** is the largest mod in here and the one that shows what the
dependency chain is for. Its own readme is explicit: *only The Complete Edition
is fully supported; legacy versions such as 1.0.7.0 additionally require the
Legacy Addon*. That addon is already in the catalog as `gfwl-stub` - it is where
the GFWL replacement comes from - so `fusionfix` simply requires it, and both
halves come from the same GitHub release, which keeps them in step.

It also shows why shared files needed handling. FusionFix ships its own
`dinput8.dll` over the one `ultimate-asi-loader` installed. Two recipes then own
the same path, with different checksums, and the one on disk is FusionFix's.
Without the two rules above the launcher would report that file as changed on
every single run and delete it the next time either recipe was updated. With
them, the counter-check on the real installation reads: *352 files from 6
recipes, everything unchanged.*

**`various-fixes` builds on it.** It installs through FusionFix's overloader,
which is why it requires `fusionfix` rather than merely suggesting it: everything
lands in `update\`, where the overloader reads it, so not one original game file
is touched. The project also offers a manual variant that replaces files in
place - that one is deliberately not what this recipe uses.

Its download is not offered by [the page most people find it
on](https://www.nexusmods.com/gta4/mods/716): Nexus refuses plain requests and
hands out links that expire with a session, so no recipe could fetch it. The
project publishes the same files on GitHub, and that is where ours come from.

**The FusionFix chain is 1.0.8.0 only, and that was learned the hard way.** The
recipe originally claimed 1.0.7.0 as well. Installed there, the game gets about
ten seconds in and dies of heap corruption (`0xC0000374`, faulting module
`ntdll.dll` - the messenger, not the cause). The project supports the Complete
Edition only; of the older releases, 1.0.8.0 is the one people report getting it
to work on, and even that its authors decline to support. So the version gate now
says what is true, and on a 1.0.7.0 installation the planner refuses the whole
chain instead of installing something that takes the game down.

That is the same mechanism that stops a 1.0.7.0 recipe from being applied to the
Complete Edition. It only protects anyone if the recipes are honest about what
they fit - a claim nobody checked is worse than no claim, because the machinery
around it works perfectly and carries the wrong thing through.

**Which leaves a choice, and the graph can now express it.** `downgrade-ce-1080`
takes the Complete Edition to 1.0.8.0 instead, out of the same GitHub release as
the 1.0.7.0 package, and the trainer runs on both. So:

| | 1.0.7.0 | 1.0.8.0 |
|---|---|---|
| sauer trainer | yes, measured | yes, per the SDK's address set |
| FusionFix and the three that need it | crashes | the version its users report working |
| everything else in the scene | most mods target this | less |

There is deliberately no edge between 1.0.7.0 and 1.0.8.0. Both come from the
Complete Edition, and going from one to the other means restoring the Complete
Edition first - which `remove downgrade-ce-1070` does from its snapshot, since
that is what a snapshot is for.

**The wizard offers both, and offers them out of the catalog** rather than out
of a list of versions that exist. What can be picked is what some recipe
produces, so 1.0.4.0 - a modding target with no recipe behind it - is no longer
on the list at all, and 1.0.8.0, which had been marked "not for modding" while
four recipes wanted nothing else, now is. A version the game cannot reach from
where it stands stays visible and says why: somebody on 1.0.7.0 looking at four
mods that ask for 1.0.8.0 is told that the way there is to remove the 1.0.7.0
downgrade on the home page first. Leaving it off the list would answer the
question on the screen by not mentioning it.

**Three recipes have no URL at all** - the two texture packs and Liberty's
Legacy live only on Nexus. They are in the catalog anyway, with the checksum of
the exact file the page serves and a note naming the page and the file. The
launcher then says what to download and where to put it, and refuses anything
whose checksum does not match. That is the same "supplied by hand" path
`vc80-runtime` used to take, and it is acceptable here for the reason it was not
acceptable there: without these the game still starts. They are cosmetic.

**Every download is one URL, and that is the weak point.** Eight files come from
seven third-party releases, none of which anybody here controls; the Dropbox the
usual downgrader used is already gone, and GitHub releases get deleted too. A
mirror would mean hosting other people's gigabytes, which is not on. So two
cheaper things instead: `check-sources.ps1` asks every URL whether it still
answers and still announces the expected size - a file silently re-uploaded
shows up as a size that no longer matches, before a tester finds it - and every
recipe's note now names the project page and the exact tag. A dead link then
leaves the user with an address rather than a checksum and nowhere to go, and
the "supplied by hand" path that the Nexus recipes use is open to every other
one as well.

**What is not in the catalog, and why.** The radio restoration for the Complete
Edition is published as a `.rar` that contains an installer - `IVCERadioRestoration.exe`
plus a 970 MB `data1.dat` only that program can unpack. There are no game files
in it to copy. Running a foreign installer is deliberately not a recipe step, so
this one cannot be done here at all, whatever archive format it came in.

**The trainer is shipped, not downloaded.** It is the only file in the catalog
this project produces itself; putting it somewhere so that our own launcher can
fetch it again would be a detour with one more thing that can fail. It sits in
`bundled\` next to the program and is taken from there.

It is still checked against the checksum in the recipe. **Being shipped is no
bonus of trust:** the folder sits next to a program, and anyone with rights there
can write into it.

Its recipe is **generated, not maintained**. Checksum and size change with every
build - a recipe written by hand would, after the next build, reject exactly the
file it is supposed to install. `scripts\pack-trainer.ps1` builds, measures,
writes the recipe and signs the catalog again. Without that last step the
launcher afterwards loads nothing at all, and rightly so: a recipe file that does
not match the signed index is, from its point of view, indistinguishable from a
tampered one.

The generated release number carries the checksum (`0.4.0+ab12cd34`). Without
that, a freshly built trainer would carry the same number as the installed one,
the planner would consider it done, and the user would see "file changed" without
being offered anything that fixes it.

**Without `vc80-runtime` nothing starts.** 1.0.7.0 was built against the Visual
C++ 2005 runtime; on today's systems it is missing, and the downgrade package
does not bring it along. Windows then only reports "the side-by-side
configuration is invalid", which gives away nothing about the actual reason. The
runtime is placed next to the EXE rather than installed system-wide - that stays
inside the game directory, is reversible, and needs no recipe step allowed to run
foreign installers.

This used to be the one step a stranger could not get past: the recipe had no
source at all and expected the file to be supplied by hand. It is now fetched
from Microsoft by `scripts\pack-vc80.ps1` - an .exe with a valid Authenticode
signature, pinned to a checksum, and the signature is checked as well, because a
checksum only says the bytes are the ones we saw last time.

There is one twist, and it is the reason that script exists. GTAIV.exe asks for
`Microsoft.VC80.CRT` **8.0.50727.42** and `Microsoft.VC80.ATL` **8.0.50727.762**.
Microsoft ships neither any more: every current download, the SP1 page included,
serves **8.0.50727.6195** from the MS11-025 security update. And Windows does not
accept it. Measured against the real GTAIV.exe, assembly folders next to it,
nothing else changed:

| Next to the EXE | Result |
|---|---|
| nothing | side-by-side error |
| Microsoft 6195, as shipped | side-by-side error |
| 6195 binaries, manifest declaring 762 | starts |

For a private, app-local assembly Windows binds on the identity in the manifest
next to the binary - not on the version resource of the DLL, and not on the hash
attributes in the manifest either. Those do not even match in Microsoft's own
signed package; they are not checked. (That is worth knowing before suspecting a
file: a hash mismatch there is normal, not a sign of tampering.)

So the manifest declares the identity the game asks for and the binaries are
Microsoft's current ones. That is what a publisher policy does system-wide, done
app-locally instead - and it means the game gets the serviced runtime rather than
the unpatched 2007 build that is otherwise passed around. Those two manifests are
the only files this project authors here; the four DLLs are verbatim Microsoft.

**Why not the usual downgrader.** The widespread GTAIVDowngrader (v2.2, January
2025) pulls its game packages from a Dropbox. Those links now serve nothing but a
"File Deleted" page - for `1040.zip`, `1070.zip` and `1080.zip` alike, in both
branches of its manifest. The usual route is therefore broken for the time being.
Gillian's downgrader uses GitHub releases instead, and that is where our package
comes from.

Incidentally, evidence that the size check from M2 serves its purpose: the
Dropbox answer was 185 KB instead of 85.7 MB and would have been rejected before
anything reached the game directory.

**What the real run produced:**

- 187 files replaced. What stays behind is only Rockstar launcher artefacts
  (`MTLX.dll`, `index.bin`, `metadata.dat`, `title.rgl`, `uninstall.exe`) and no
  game data - the 1.0.7.0 game does not read them. A cleanup step is therefore
  unnecessary; removing `uninstall.exe` would even be harmful.
- The Complete Edition's `GTAIV.exe` (MD5 `1a47b45f...`) appears in **none** of
  the known hash lists for 1.2.0.59 - the files date from August 2026, the
  community's data from January 2025. It did not get in the way.
- The resulting 1.0.7.0 `GTAIV.exe` (`ab21c0d9...cb4c`) is byte-identical to the
  one from an independently obtained `Retail-1070.zip`. Two sources, the same
  checksum.

**After the downgrade, do not start through the Rockstar Games Launcher**, start
`GTAIV.exe` directly - otherwise the launcher notices the changed installation.

## Trainer: sauer IV Trainer

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

**The menu runs in game** - F7 opens it, the numpad or the arrow keys operate it,
toggles and choices react, actions run all the way through into the log file.

| Key | Effect |
|---|---|
| `F7` | open and close the menu |
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
[14:25:53.946] Menu ready. F7 opens it, 13 key bindings active.
```

## Catalog signature

The catalog determines which files are written into the game directory. Whoever
can swap it out can slip in arbitrary code - TLS only protects the transport,
not against a server that has been taken over.

Hence: a signed `index.json` lists every recipe file with its checksum, and the
signature is checked against a public key built into the program (ECDSA P-256,
SHA-256). Without a valid signature **not a single** recipe is loaded - not "the
inconspicuous ones anyway", because whoever can forge gets to pick which ones
look inconspicuous.

```
mliv catalog-key  --key C:\keys\catalog.pem        once, outside the repository
mliv catalog-sign --catalog .\catalog --key C:\keys\catalog.pem
```

The public half belongs in `CatalogSignature.EmbeddedPublicKey`, the private half
**not into the repository** - here it lives under
`%LOCALAPPDATA%\ModlauncherIV\keys\`. As long as no key is built in, the launcher
rejects every catalog; for development there is `--allow-unsigned`, which warns
loudly, and `--public-key <base64>` for a different signer.

Changing the built-in key makes **every catalog signed so far invalid**. That is
intended - it is the same operation as a recall.

**Whoever touches the catalog has to sign it again.** Every change to a recipe
file breaks the index, and the launcher then loads nothing at all. That is not an
annoyance, it is the whole point: a changed recipe file is, from the outside,
indistinguishable from a tampered one.

## Smart App Control

Smart App Control is active on this development machine. It blocked the first
build immediately: a normal `dotnet build` produces `mliv.exe` plus `mliv.dll`,
the exe is allowed to start, but loading the unsigned `mliv.dll` is refused by the
code integrity policy.

```
Event 3077 - attempted to load mliv.dll that did not meet the
Enterprise signing level requirements
Policy ID {0283ac0f-fff1-49ae-ada1-8a933130cad6}
```

**What was measured - and what was not.** While the block was active, a
single-file publish went through reliably: without a separate managed DLL there
is nothing to block. Later SAC stopped blocking unsigned builds of its own
accord. Three repetitions with a forced recompile all went through, signed and
unsigned alike.

From which follows the actual problem: **SAC is not rule-based, it is
reputation-based.** Microsoft's Intelligent Security Graph decides per file, and
the same file can be blocked today and allowed tomorrow. Whether signing helped
could therefore not be measured cleanly.

**What was built out of it:**

| Measure | Effect |
|---|---|
| Single-file publish (`scripts/run.ps1`, `scripts/package.ps1`) | Removes the separate managed DLL - the one surface where the block demonstrably applied. |
| Signing on every run (`scripts/sign.ps1`) | The release pipeline stands from the start; later only the certificate is swapped. |
| SAC detection in the diagnostic report | The user learns about it **before** the downgrade, not when the trainer stays mute. |

**No self-deception about signing:** a self-signed certificate does not satisfy
SAC. What is judged is the signer's reputation with the ISG, not the local trust
chain. Deterministically, only a real code-signing certificate with established
reputation solves this - set `MLIV_SIGN_THUMBPRINT`, then the release path in
`sign.ps1` applies.

**For the trainer: measured, and it went wrong.**

| | T0 | T1 |
|---|---|---|
| Size | 147 KB | 193 KB |
| Signature | self-signed | the same |
| SAC state | active (`1`) | active (`1`) |
| Result | **loaded** | **blocked** |

Same machine, same certificate, same policy - a different result. T1 failed with
event 3077 and ASI loader error 4551 (`0x11C7`, the Win32 part of `0x800711C7`).
The dependencies were clean, the ASI x86 and signed. There was technically
nothing to correct.

That establishes what stands above as a suspicion: **SAC is not a rule you can
satisfy.** On this development machine it was therefore switched off.

**For end users this stays unsolved.** Anyone with Smart App Control active will
not get the trainer loaded. Deterministically, only a real code-signing
certificate with established reputation helps there - set
`MLIV_SIGN_THUMBPRINT`, then the release path in `sign.ps1` applies.

**Old state, superseded:** the single-file approach does not help the trainer. An
`.asi` is by definition an unsigned DLL loaded into `GTAIV.exe` - exactly the
operation SAC prevents. This affects every end user with Smart App Control active
as well.

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
