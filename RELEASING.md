# Handing this to other people

One file goes out. Everything else here is about what to say alongside it.

## Building it

```
.\tests\run-tests.ps1                  233 tests, and they have to pass
.\scripts\build-trainer.ps1 -Test      120 more, without the game
.\scripts\check-sources.ps1            is everything still downloadable?
.\scripts\package.ps1                  the release
```

`check-sources.ps1` asks every URL in the catalog whether it still answers and
still announces the size the recipe expects, and hashes the files the launcher
ships itself. A source that has been deleted or silently re-uploaded otherwise
becomes visible as a failed installation on somebody else's machine, and the
checksums that make that failure safe also make it certain. `-Deep` downloads
everything and checks the checksums instead of the sizes - over two gigabytes,
which is why it is not what runs by default.

`package.ps1` does the whole chain in the order that matters: fetch the VC++
runtime from Microsoft, build the trainer, write its recipe with the measured
checksum, **sign the catalog**, then build the EXE. Building the EXE first would
pack the previous catalog, and signing afterwards would leave the packed one
unsigned - which the launcher rightly refuses to load. The script checks that
the signed index still matches the folder before it builds, because
`-SkipTrainer` skips the signing along with the trainer and that combination has
shipped a dead catalog before.

The result:

```
artifacts\release\ModlauncherIV.exe     about 63 MB
```

Self-contained. No .NET to install, no separate catalog, no folder. Take its
SHA-256 and publish it next to the file:

```
(Get-FileHash artifacts\release\ModlauncherIV.exe -Algorithm SHA256).Hash
```

## Where to put it

A GitHub release, for three reasons that apply here specifically: the URL stays
put, the checksum can sit beside the file, and it is where the rest of this
ecosystem already lives - the catalog itself downloads FusionFix, the ASI loader
and the downgrade packages from GitHub releases. One less kind of host to trust.

## What has to be said in the release notes

**SmartScreen will stop it.** The EXE is signed, but with a self-signed
certificate nobody has any reason to trust, so Windows shows "Windows protected
your PC" with the "Run anyway" hidden behind *More info*. This is not something
to apologise for or to work around: it is reputation, not a rule, and only a real
code-signing certificate with a history behind it changes it. Say what the dialog
looks like and where the button is. People who are told in advance click it;
people who are surprised by it do not.

**It asks for administrator rights, deliberately.** GTA IV lives under
`C:\Program Files`, and so do the snapshots that make a rollback possible.

**What it changes and how to undo it.** Everything it installs goes through a
snapshot, mod by mod, with Remove on the home page. It removes itself through
"Apps & features" and offers to put the game back on the way out.

## Before a first public release

- [ ] The signing key is backed up somewhere other than this machine. Losing it
      means never signing a catalog again that the copies already out there
      accept - the public half is compiled into every one of them.
- [ ] Tested on a machine that is not the one it was built on. Steam and Epic
      detection now runs against store layouts built out of paper in the tests -
      a libraryfolders.vdf, an appmanifest, an Epic manifest, and the Complete
      Edition's habit of keeping the game one folder further down. That is
      enough to know the parsing works; it is not the same as a real store
      installation, which nobody here owns.
- [ ] Decide what happens when a third-party download disappears. The Dropbox
      the usual downgrader used is already gone; everything here points at
      GitHub releases, and those can be deleted too. There are still no mirrors,
      because a mirror means hosting somebody else's gigabytes. What exists
      instead: `check-sources.ps1` notices it here first, and every recipe now
      names the project page in its note, so a dead link leaves the user with a
      place to go rather than a checksum and no address.

## What is knowingly not ready

Three recipes have no URL and expect the file to be supplied by hand: both
texture packs and Liberty's Legacy, all three published only on Nexus, which
refuses plain requests. They are cosmetic - the game runs without them - and the
launcher says exactly which file to fetch and where to put it. That is the
honest state, not an oversight.

The FusionFix chain is limited to 1.0.8.0 and is untested there. On 1.0.7.0 it
corrupts the heap, which is measured rather than assumed.
