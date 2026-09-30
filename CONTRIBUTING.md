# Contributing

## Workflow

1. Branch from `main` (`feat/...`, `fix/...`, `chore/...`, `docs/...`).
2. Make the change and run `npm run check`. Run `swift build && swift test` too if you touched Swift.
3. Open a pull request. Fill in the template.
4. CI must pass. The required check is **CI passed**, and the branch must be up to date with `main`.
5. Merge with **squash**. `main` keeps a linear history, and direct pushes, force pushes and deletion of `main` are blocked.

## Build steps

The app is built in the order listed in [project.md](project.md). One step is one pull request, and the pull request also:

- ticks the step off in the README roadmap and updates the README's features, installation and usage if they changed
- updates `project.md` and `docs/` if the design changed
- adds tests for the new behavior

## Tests

- JavaScript: Vitest, in `tests/`. New visualizer plugins are picked up automatically by `tests/plugin-contract.test.js` when placed in `IdleViz/web/visuals/`.
- Swift: XCTest. Keep logic in a Swift package target so `swift test` can run it in CI. Linting is SwiftLint (`.swiftlint.yml`).

## Code style

Prettier formats JavaScript, JSON and YAML (`npm run format`). Markdown is not auto-formatted. Comments should say why, not what.
