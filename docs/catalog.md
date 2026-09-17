# The catalog

What the recipes are, which ones are proven, and why the whole thing is signed.

## The catalog - state and caveats

Thirteen recipes with **real, self-computed SHA-256 checksums**. Six of them have
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
| `zmenu-iv` | Zolika1351, supplied by hand (MEGA) | 117 MB | untested - takes F7, sauer moves to F8 |

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

