# Use `git mv` (FsForge `GitOps.moveFile`) to archive processed inbox items

## Context
`eru inbox process` archives each processed item (and its `.meta.json` sidecar) from `inbox/raw/<channel>/` to `inbox/archive/<channel>/` with a plain `File.Move` (`src/Eru.Adapters/AdapterDeps.fs:96-102`), so git sees delete+add instead of a rename and nothing is staged. FsForge 0.0.1 adds `GitOps.moveFile worktreeDir source destination : Async<Result<unit,string>>` (runs `git mv`; paths relative to worktreeDir; fails if source untracked/missing or destination exists). Adopt it.

## Changes
1. `src/Eru.Adapters/Eru.Adapters.fsproj:14`: bump `FsForge` `0.0.1-beta1` -> `0.0.1` (confirm it is published on nuget.org; only `0.0.1-beta1` is in the local cache).
2. `src/Eru.Adapters/GitAdapter.fs`: new `moveFile src dst` (Result<unit,string>), wired in as `MoveLocalFile` in `AdapterDeps.fs` (the `Deps` signature is unchanged, so domain and tests are untouched):
   - Create dst's parent dir.
   - Run `Forge.GitOps.moveFile` with the source's own directory as the worktree dir, the file name as source and the relative path to dst as destination. Git finds the repo itself, so no repo-root lookup is needed.
   - If the destination already exists, return `Error` without touching the source (keeps `moveOrAcceptAlreadyDone` in `InboxProcess.fs` working).
   - If `git mv` fails (untracked source, or not a git repo), fall back to `File.Move`.
3. Docs (both in the plan):
   - `docs/reference/cli.md` (~lines 439-441, the "moved from `.../raw/<channel>/` to `.../archive/<channel>/`" passage): add that tracked items are moved with `git mv` (staged rename); untracked items and non-git dirs get a plain move.
   - `skills/eru/references/commands.md:275`: same note as a short parenthetical.
4. `CHANGELOG.md`: under `[Unreleased]` -> `### Changed`, add: "`eru inbox process` archives tracked items with `git mv` (FsForge 0.0.1 `GitOps.moveFile`), so the archive shows as staged renames; untracked items and non-git directories still get a plain move".

## Verification
- `dotnet build` and `dotnet test` (existing `InboxProcessTests` mock `MoveLocalFile`, so they should stay green).
- Adapter tests in `tests/Eru.Tests/GitAdapterTests.fs` (temp git repo): tracked file -> `git status` shows a rename (`R`); untracked file -> moved via fallback; non-git dir -> moved; destination exists -> `Error` and source kept.
- Manual: in a knowledge repo with a committed item in `inbox/raw/`, run `eru inbox process` and check `git status` shows renames to `inbox/archive/`.

## Notes
- Per AGENTS.md, no code is written until you say "implement".
- Domain paths stay `/`-based; relative-path computation lives in the Adapters layer.
