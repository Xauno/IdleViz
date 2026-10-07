<!--
Write the title as a conventional commit with a scope, in lowercase:
`feat(settings): add overlay toggle`, `fix(dismiss): ignore the trigger input`, `docs: add UI mockups`.

Open the description with a short paragraph: the problem or context, what this
changes, and what it deliberately does not cover. Link related PRs by number.
If this is a build step from the README's Roadmap, say which one.
-->

## What Changed

<!-- Describe the change clearly and keep scope tight. Use a table if it is clearer than prose. -->

## Why

<!-- The problem being solved and why this approach is the right one. -->

## UI Changes

<!-- Before/after screenshots for UI changes, and a short video for motion or interaction.
     Delete this section if not applicable. -->

## Verification

<!-- Be concrete and honest: the exact commands run and their results, what you checked by hand,
     and anything that fails or was not tested (and why), stated plainly rather than left out. -->

## Checklist

- [ ] `npm run check` passes locally (and `swift build && swift test` in `mac/` if Swift changed, `dotnet build` and `dotnet test` in `windows/` if C# changed)
- [ ] README updated if a build step finished or behavior changed
- [ ] `project.md` / `docs/` updated if the design changed
- [ ] Before/after screenshots included for UI changes
