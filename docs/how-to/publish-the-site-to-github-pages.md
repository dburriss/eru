---
title: Publish the knowledge site to GitHub Pages
type: how-to
tags: [site, github-actions, github-pages, ci]
---

# Publish the knowledge site to GitHub Pages

Turn a [knowledge repo](set-up-a-knowledge-repo.md) into a browsable, searchable site that rebuilds whenever
its content changes. This uses `eru sync` and `eru site generate` in a GitHub Action and deploys the output
with the official Pages actions.

## Prerequisites

- A knowledge repo with `.eru/config.json` that lists the repo itself as a source (with an OKF bundle), so
  `eru sync` has something to pull into the cache.
- **Settings → Pages → Source** set to **GitHub Actions**.

## 1. Keep agent and workflow files out of the site

A knowledge repo usually contains installed skills, agent files and the inbox. Exclude them with
`siteIgnorePatterns` in `.eru/config.json` (setting it replaces the defaults, so keep `index.md` and `log.md`):

```json
{
  "settings": {
    "siteIgnorePatterns": [
      "index.md",
      "log.md",
      ".agents/**",
      ".claude/**",
      ".github/**",
      "apm_modules/**",
      "inbox/**"
    ]
  }
}
```

## 2. Add the workflow

Create `.github/workflows/pages.yml`:

```yaml
name: Publish site

on:
  push:
    branches: [main]
    paths:
      - "**/*.md"
      - ".eru/config.json"
      - ".github/workflows/pages.yml"
  # Only needed if PRs are merged by a workflow using GITHUB_TOKEN (see below).
  workflow_run:
    workflows: ["Automerge ingest PRs"]
    types: [completed]
  workflow_dispatch:

permissions:
  contents: read
  pages: write
  id-token: write

concurrency:
  group: pages
  cancel-in-progress: false

jobs:
  build:
    if: github.event_name != 'workflow_run' || github.event.workflow_run.conclusion == 'success'
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4

      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: "10.0.x"

      - name: Install eru
        run: dotnet tool install --global Eru.Tool --version 0.9.1

      - name: Sync the knowledge source
        run: eru sync

      - name: Generate the site
        run: eru site generate -o _site

      - uses: actions/configure-pages@v5

      - uses: actions/upload-pages-artifact@v3
        with:
          path: _site

  deploy:
    needs: build
    runs-on: ubuntu-latest
    environment:
      name: github-pages
      url: ${{ steps.deployment.outputs.page_url }}
    steps:
      - id: deployment
        uses: actions/deploy-pages@v4
```

Pin `--version` so a new eru release can't change your site unexpectedly; bump it deliberately.

## Notes

- **Ingest PRs don't trigger the site build on their own.** Pushes made with `GITHUB_TOKEN` (such as an
  auto-merge workflow) don't fire other workflows, which is why the `workflow_run` trigger rebuilds the site
  after the [automerge workflow](generate-docs-from-inbox-with-gh-aw.md) finishes. Its `workflows:` name must match
  that workflow's `name:`. Drop the trigger if you merge PRs by hand.
- **`eru sync` is required.** `eru site generate` reads the local cache, so sync first or the site will be empty.
- **Customize the look** with `--custom-css`; see [Customize the generated site](customize-the-generated-site.md).
