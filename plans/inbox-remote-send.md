# Remote git inbox send

## Context

`eru inbox send` only writes into a local directory (`src/Eru.Domain/InboxSend.fs`). Getting the item
into a GitHub repo needs a manual `git add/commit/push` afterwards (see
`docs/how-to/generate-docs-from-inbox-with-gh-aw.md`, step 5). `todo.md` lists "remote git inbox send"
as open, and `plans/inbox-send.md` named a remote (clone+push) inbox kind as a natural v2.

Goal: an inbox can be a remote GitHub repo. `eru inbox send` then clones it shallowly, writes the item,
commits and pushes. The git work uses the [FsForge](https://www.nuget.org/packages/FsForge/) NuGet
package (0.0.1-beta1: provider-agnostic git write-back; needs `git` and `gh` on PATH; APIs return
`Async<Result<_, string>>`, e.g. `GitOps.ensureClone auth url dir`).

## Decisions

- Send **commits and pushes directly to a branch**. It does not open a PR.
- `eru inbox add <name> <url>` accepts a URL as the positional path and auto-detects remote (`http(s)://`,
  `git@`) versus local.
- Remote inboxes are **send-only**. `inbox process` and `inbox watch` return a clear error. Processing
  happens in the repo via gh-aw or Actions.

## Approach

> Implementation note: rather than a separate `Remote` field, `InboxConfig.Path` holds the URL for a remote inbox (detected by `InboxConfig.isRemote`), so the JSON schema only gains `branch`. `--branch` is limited to the default branch or a new branch, because FsForge's `pushBranch` force-pushes.

1. **Config** (`src/Eru.Domain/Config.fs`): extend `InboxConfig` with optional `Remote : string option`
   and `Branch : string option`. `Path` stays for local inboxes. Keep the JSON backward compatible;
   update `normalizeInboxMap`, serialization and `Config.merge` (semantics unchanged).
2. **Add** (`InboxAdd.fs`, `Args.fs` `InboxAddArgs`, `InboxAddCli.fs`): a URL is stored as `Remote` and
   skips the `DirectoryExists` check. New optional `--branch`. Local behaviour is unchanged.
3. **Deps** (`Deps.fs`, `AdapterDeps.fs`): add one field, for example
   `PushToRemote : remoteUrl -> branch option -> commitMessage -> files:(relPath * bytes) list -> Result<unit, string>`.
4. **Adapter** (new `src/Eru.Adapters/RemoteInboxAdapter.fs` beside `GitAdapter.fs`): use FsForge to
   clone shallow into a temp dir (reuse the `withTempDir` pattern), write files, commit and push,
   returning `Result<_, string>`. Add `FsForge` to `Eru.Adapters.fsproj` with an inline `PackageReference`
   like the others, and add the file to the compile order. Reuse `noPromptEnv` semantics so git never
   blocks on a credential prompt. Sidecar `.meta.json` files go in the same commit. Confirm FsForge's
   commit/push surface and auth (`GitAuth`) at the start of implementation.
5. **Send** (`InboxSend.fs`): make target path, filename, frontmatter and sidecar generation pure and
   shared with the local path. If `inbox.Remote` is set, collect the files and call
   `deps.PushToRemote` instead of `WriteLocalFile`. `--dryrun` reports what would be pushed. Commit
   message: `inbox: add <slug> (<channel>)`. The result reports remote and branch. Branch defaults to
   the repo's default branch.
6. **Process/watch** (`InboxProcess.fs`, `InboxWatch`): return
   `Error "inbox '<name>' is remote; process/watch are not supported"`. `inbox list` shows the remote
   URL in the path column.
7. **Docs**: `docs/reference/cli.md` (inbox add/send), `docs/reference/config-file.md` (new fields), the
   gh-aw how-to (drop the manual git step) or a new how-to, `CHANGELOG.md`, `skills/eru/SKILL.md` and
   `references/commands.md`, tick `todo.md`.

## Tests (`tests/Eru.Tests`)

- Add `PushToRemote` to every test `makeDeps`.
- `InboxTests` / `InboxSendTests`: remote add (URL detection, branch, no directory check); send to a
  remote captures the files, paths and commit message; dryrun pushes nothing; process/watch error on a
  remote inbox; config round-trip and old-config compatibility.
- Adapter test in the `GitAdapterTests.fs` style: push to a local bare repo (`file://` URL) and assert
  the commit and file exist.

## Verification

- `dotnet build` and `dotnet test`.
- Manual: create a scratch GitHub repo, run `eru inbox add test https://github.com/<me>/<repo>`, then
  `echo hi | eru inbox send -i test`, and confirm the commit lands on the branch under
  `inbox/raw/default/`.

## Open points

- Auth relies on the existing `gh` login (FsForge needs `git` and `gh` on PATH).
- Files pushed per send are small, so a shallow clone per send is acceptable. Revisit with a cache if
  send latency matters.
