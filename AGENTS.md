# Agent instructions

## Project
See [project.md](project.md) for the full plan: what the app does, its architecture, and the build order. Author docs live in [docs/](docs/), for example [docs/custom-visualizer.md](docs/custom-visualizer.md).

## Rules

1. **Prioritize the codebase-memory MCP for code exploration.** Use `search_graph` to find symbols, `trace_path` for callers and relationships, `get_code_snippet` to read source, and `get_architecture` for an overview. Check that the files you rely on are indexed with `check_index_coverage`. Fall back to grep or reading files directly for string literals, config, Markdown and other non-code files, and for anything the index doesn't cover. The index refreshes automatically. If results look stale, check `index_status` or run `index_repository`.
2. **Ask before making any judgement call.** If a decision has more than one reasonable answer (design, scope, naming, tradeoffs, which approach to take), ask the user instead of choosing. Don't fill gaps with your own assumptions.
