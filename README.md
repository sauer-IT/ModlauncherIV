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
| `catalog/` | The declarative recipes. Six of them, five proven. |
| `tests/` | Fixtures and the test script. |

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

## Tests

```
.\tests\run-tests.ps1
```

151 tests against fake game directories. No real installation is touched. If GTA
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

**The script is deliberately pure ASCII.** PowerShell 5.1 reads a `.ps1` without
a BOM as CP1252; a UTF-8 em dash becomes, among other things, `”`, and that
counts as a string delimiter. The parser then silently slips out of alignment
from that point on.

## The catalog - state and caveats

Six recipes with **real, self-computed SHA-256 checksums**. Five of them have
**run against a real installation** - Complete Edition 1.2.0.59 through the
Rockstar Games Launcher, downgraded to 1.0.7.0. The game starts.

| Recipe | Source | Size | State |
|---|---|---|---|
| `downgrade-ce-1070` | GitHub release of the Gillian guide project | 111 MB | run |
| `ultimate-asi-loader` | ThirteenAG, GitHub release | 928 KB | run |
| `gfwl-stub` | FusionFix Legacy Addon, GitHub release | 4.0 MB | run |
| `vc80-runtime` | Microsoft, signed redistributable | 1.8 MB | run |
| `mliv-trainer` | shipped, self-built | 205 KB | run |
| `scripthook-dotnet` | ClonkAndre, GitHub release | 647 KB | untested |

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
.\scripts\build-trainer.ps1 -Test      117 tests, without GTA IV
```

A navigation bug shows up in milliseconds that way, instead of after a game
start, a loading screen and a key press.

**State T6.** Roughly sixty options. The root holds **nothing but the eight
categories**, each of which opens its own submenu:

| Category | Content |
|---|---|
| Player | godmode, health, armour, invisible, jump to camera, traits |
| Weapons | all weapons, take them away, infinite ammo, weapon skill |
| Wanted | level, never wanted, upper limit, clear cops, no new patrols |
| Money | amount and give |
| Vehicles | spawn ten models, repair, indestructible, tuning, paint |
| World | time of day, weather, traffic density, jump to five places |
| Movement | fly, superjump, run speed, teleport to the waypoint |
| Pedestrians | density, everyone ignores you, riot, panic, clear the area |
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
- **M6** profiles, catalog update · uninstaller · mirrors for third-party sources

Open before a public release: SmartScreen without a real code-signing
certificate, third-party download sources that can vanish (the Dropbox already
did) with no mirrors of our own, only ever tested on one machine, and no
uninstaller yet. The `vc80-runtime` step, which used to be the hard blocker
because a stranger could not supply the file, now comes from Microsoft.

The full project plan with architecture, risks and open questions exists as a
separate document.

## Ground rules

- The core never touches files directly - every change runs through the pipeline
  of pre-flight, snapshot, apply, verify, commit, with automatic rollback.
- Ledger and snapshots live under `%LOCALAPPDATA%\ModlauncherIV\`, not in the
  game directory.
- Updating a recipe removes what its previous version owned and the new one no
  longer writes. The ledger already knows those files, so no list of old names
  has to be maintained - and an ASI that gets renamed cannot stay behind and
  answer on the same key as its successor.
- No game files and no third-party mods are shipped along. Everything is
  downloaded from the original source and checked by SHA-256.
