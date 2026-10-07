# Contributing

## Workflow

1. Branch from `main` (`feat/...`, `fix/...`, `chore/...`, `docs/...`).
2. Make the change and run `npm run check`. Run `swift build && swift test` in `mac/` too if you touched Swift, and `dotnet format --verify-no-changes`, `dotnet build` and `dotnet test` in `windows/` if you touched the Windows app.
3. Open a pull request. Fill in the template.
4. CI must pass. The required check is **CI passed**, and the branch must be up to date with `main`.
5. Merge with **squash**. `main` keeps a linear history, and direct pushes, force pushes and deletion of `main` are blocked.

## Build steps

The steps each app was built in are listed in [docs/roadmap.md](docs/roadmap.md). One step is one pull request, and the pull request also:

- ticks the step off in `docs/roadmap.md` and updates the README and `docs/usage.md` if features, installation or usage changed
- updates `project.md` and `docs/` if the design changed
- retakes the README's screenshots in `docs/images/` if the UI changed, so they show what the app looks like now
- adds tests for the new behavior

## Tests

- JavaScript: Vitest, in `tests/`. New visualizer plugins are picked up automatically by `tests/plugin-contract.test.js` when placed in `web/visuals/`.
- Swift (the Mac app, in `mac/`): XCTest. Keep logic in a Swift package target so `swift test` can run it in CI. Linting is SwiftLint (`mac/.swiftlint.yml`).
- C# (the Windows app, in `windows/`): xUnit. Keep logic in `IdleViz.Core` so `dotnet test` can run it in CI. Linting is `dotnet format` (`windows/.editorconfig`) and the .NET analyzers, with warnings as errors. The Windows build steps are listed in `docs/roadmap.md` and written up in `docs/windows.md`.

## Code style

Prettier formats JavaScript, JSON and YAML (`npm run format`). Markdown is not auto-formatted. Comments should say why, not what.
