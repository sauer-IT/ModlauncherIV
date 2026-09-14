# Contributing

Suggestions, bug reports and pull requests are welcome.

## Proposing a mod for the catalog

A recipe is a JSON file in `catalog/`. Look at an existing one of the same kind
before writing one - `xbox-rain-droplets.json` for a plain GitHub download,
`zmenu-iv.json` for a file that has to be supplied by hand.

What a recipe needs:

- **The original source.** The URL where the author publishes the file - a
  GitHub release, the author's own site. No re-uploads and no mirrors: the
  catalog points at the authors, it does not host their work. When a site does
  not allow direct downloads, leave `urls` empty and say in `note` where to get
  the file.
- **SHA-256 and size** of exactly that file. Nothing is installed without them.
- **The game versions** it is made for, in `appliesToVersions`, measured rather
  than assumed.
- Its dependencies in `requires`, and a `category`.

Run `.\scripts\check-sources.ps1` and `.\tests\run-tests.ps1` before opening the
pull request.

## Why a merged recipe does not show up until the next release

The launcher only loads a catalog signed with the project's key, and that key
stays with the maintainer. A pull request changes the recipe files; the
signature is renewed when the next release is built. That is on purpose - it is
what stops anybody else from deciding which files end up in people's game
folders.

## Code

- `src/Launcher.Core` - planning and installation logic, tested against fixtures.
- `src/Launcher.App` - the WPF window. `tests/UiSmoke` builds every page.
- `src/Trainer` - the C++ trainer, x86. `.\scripts\build-trainer.ps1 -Test`.

Both test runs have to pass. By contributing you agree that your contribution is
licensed under the GPL-3.0, like the rest of the project.
