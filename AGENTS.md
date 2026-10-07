# Agent instructions

## Project
See [project.md](project.md) for the design: what the app does, its architecture, and the decisions behind it. The Mac app is in `mac/`, the Windows app in `windows/`, and the page both show in `web/`. Author docs live in [docs/](docs/), for example [docs/custom-visualizer.md](docs/custom-visualizer.md). Work the Windows app has that the Mac app still needs is listed in [docs/mac-todo.md](docs/mac-todo.md).

## Rules

1. **Prioritize the codebase-memory MCP for code exploration.** Use `search_graph` to find symbols, `trace_path` for callers and relationships, `get_code_snippet` to read source, and `get_architecture` for an overview. Check that the files you rely on are indexed with `check_index_coverage`. Fall back to grep or reading files directly for string literals, config, Markdown and other non-code files, and for anything the index doesn't cover. The index refreshes automatically. If results look stale, check `index_status` or run `index_repository`.
2. **Ask before making any judgement call.** If a decision has more than one reasonable answer (design, scope, naming, tradeoffs, which approach to take), ask the user instead of choosing. Don't fill gaps with your own assumptions.
3. **Work through pull requests, and test as you go.** `main` is protected, so never push to it. Branch, commit, push and open a PR for each change. Run `npm run check` (and `swift build && swift test` in `mac/` for the Mac app, and `dotnet format --verify-no-changes`, `dotnet build` and `dotnet test` in `windows/` for the Windows app) and fix failures before pushing, and check CI results after. Each build step in the README's Roadmap is one PR and must include its tests. Write each PR in the style of [pingdotgg/t3code](https://github.com/pingdotgg/t3code/pulls): a conventional-commit title with a scope (`feat(settings): ...`), then a descriptive body following `.github/PULL_REQUEST_TEMPLATE.md`.
4. **Update [README.md](README.md) at the end of every build step**: tick the step in the Roadmap table and update Features, Installation and Usage to match what now actually works. Don't describe unbuilt things as working.
