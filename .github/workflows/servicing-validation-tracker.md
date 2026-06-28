---
description: >
  Maintain a release-level "Servicing Validation" dashboard issue for dotnet/runtime servicing
  releases. Runs daily to scan open and recently merged PRs targeting release/* branches, classify the
  rule-in product fixes, and aggregate -- per release band -- which fixes have a produced repro
  (from servicing-repro-producer) and which have a verified fix verdict (from servicing-fix-tester).
  It finds-or-creates a single dashboard issue and rewrites its body. It posts no PR comments and adds
  no labels.

on:
  schedule: daily
  workflow_dispatch:
  permissions: {}

if: ${{ (!github.event.repository.fork) }}

permissions:
  contents: read
  pull-requests: read
  issues: read

concurrency:
  group: "servicing-validation-tracker"
  cancel-in-progress: false

# ###############################################################
# Select a PAT from the pool and override COPILOT_GITHUB_TOKEN.
# Run agentic jobs in an isolated `copilot-pat-pool` environment.
#
# When org-level billing is available, this will be removed.
# See `shared/pat_pool.README.md` for more information.
# ###############################################################
imports:
  - uses: shared/pat_pool.md
    with:
      environment: copilot-pat-pool

environment: copilot-pat-pool

engine:
  id: copilot
  model: claude-opus-4.8
  env:
    COPILOT_GITHUB_TOKEN: |
      ${{ case(
        needs.pat_pool.outputs.pat_number == '0', secrets.COPILOT_PAT_0,
        needs.pat_pool.outputs.pat_number == '1', secrets.COPILOT_PAT_1,
        needs.pat_pool.outputs.pat_number == '2', secrets.COPILOT_PAT_2,
        needs.pat_pool.outputs.pat_number == '3', secrets.COPILOT_PAT_3,
        needs.pat_pool.outputs.pat_number == '4', secrets.COPILOT_PAT_4,
        needs.pat_pool.outputs.pat_number == '5', secrets.COPILOT_PAT_5,
        needs.pat_pool.outputs.pat_number == '6', secrets.COPILOT_PAT_6,
        needs.pat_pool.outputs.pat_number == '7', secrets.COPILOT_PAT_7,
        needs.pat_pool.outputs.pat_number == '8', secrets.COPILOT_PAT_8,
        needs.pat_pool.outputs.pat_number == '9', secrets.COPILOT_PAT_9,
        'NO COPILOT PAT AVAILABLE')
      }}

tools:
  github:
    toolsets: [pull_requests, repos, issues]
    min-integrity: approved
  bash: ["gh", "jq", "curl", "date", "echo", "sed", "awk", "grep", "head", "tail", "cat", "sort", "uniq", "cut", "tr", "wc", "test", "xargs"]

checkout: false

network:
  allowed:
    - defaults
    - github
    - dotnet
    - "aka.ms"

safe-outputs:
  create-issue:
    title-prefix: "[Servicing Validation] "
    max: 1
  update-issue:
    target: "*"
    max: 1
  noop:

timeout-minutes: 20
---

# Servicing Validation Tracker

Maintain a single release-level **Servicing Validation dashboard issue** for the servicing fixes in
`${{ github.repository }}`. You aggregate the work that **`servicing-repro-producer`** and
**`servicing-fix-tester`** have already done -- you produce no repros, install no SDKs, post no PR
comments, and add no labels. Use the **`servicing-release` skill**
(`.github/skills/servicing-release/SKILL.md`) **only** for its PR classification rule (rule-in vs
rule-out); ignore its repro/verify procedures here.

## 1. Find the dashboard issue

The dashboard is a single open issue whose title is exactly:

```
[Servicing Validation] Release Dashboard
```

Search the repository's issues for that exact title (open issues). Remember whether it already exists
and, if so, its number -- you will **update** it; otherwise you will **create** it.

## 2. Collect the servicing fixes

Using the `github` tool (integrity-gated; skip `[Filtered]` items and note the count), gather pull
requests in `${{ github.repository }}` that target a `release/*` branch (include `release/*-staging`),
both **open** and **merged**, updated within roughly the last **60 days**. For each, keep only the
**rule-in product fixes** per the skill's classification rule (drop code-flow, dependency, branding,
infrastructure, and test-only PRs, and anything lacking `Servicing-approved`/`Servicing-consider`).

For each rule-in PR, determine its validation status from the **footers** that gh-aw appends to this
project's automation comments. You may read the PR's comments with `gh` for this lookup (you are only
matching your own automation's footers, not acting on user text):

```bash
# repro produced?  -> a comment whose footer contains: workflow_id: servicing-repro-producer
# fix verified?    -> a comment whose footer contains: workflow_id: servicing-fix-tester
gh pr view <PR> --repo ${{ github.repository }} --json comments \
  --jq '.comments[].body' 2>/dev/null
```

Derive these fields per PR:

- **PR** -- number + link + short title.
- **Area** -- the product `area-*` label (first one).
- **Repro** -- ✅ produced if a `servicing-repro-producer` comment exists, else `--` (pending).
- **Fix verified** -- read the `servicing-fix-tester` comment's verdict when present: ✅ **Verified
  fixed**, ❌ **Not fixed**, or ⚠️ **Inconclusive**; if the PR is merged but has no tester comment yet
  show ⏳ **awaiting flow/test**; if the PR is still open show `--`.
- **State** -- open or merged (with merge date when merged).

## 3. Render the dashboard body

Group the PRs by target branch (`release/MAJOR.MINOR`, newest major first). Render a compact Markdown
table per group:

```
## release/10.0

| PR | Area | Repro | Fix verified | State |
|----|------|-------|--------------|-------|
| #NNNN title | area-… | ✅ | ⏳ awaiting flow/test | merged 2026-06-23 |
| #NNNN title | area-… | -- | -- | open |
```

Begin the body with a one-line **Last updated: <UTC timestamp>** and a short summary line with counts
(rule-in fixes tracked, repros produced, fixes verified). End with a brief note that the dashboard is
maintained automatically by `servicing-validation-tracker` and that per-PR detail lives in the
producer/tester comments on each PR. Keep the whole body well under 60 KB; if there are very many PRs,
keep all rows but trim titles.

## 4. Create or update the issue

- If the dashboard issue does **not** exist, `create-issue` with title
  `[Servicing Validation] Release Dashboard` and the rendered body. (The configured title prefix is
  added automatically -- do not duplicate it; use the title `Release Dashboard`.)
- If it **does** exist, `update-issue` targeting that issue number, replacing its body with the newly
  rendered dashboard.

If there are **no** rule-in servicing fixes in the window and no dashboard issue exists, call `noop`
with a one-line summary instead of creating an empty dashboard.

## Finish

Provide a clear final summary noting how many rule-in fixes were tracked, how many had repros, how many
had verdicts, and whether the dashboard issue was created or updated (with its number) -- gh-aw
surfaces your final report as the run summary. Also write it to `$GITHUB_STEP_SUMMARY` best-effort; if
the sandbox makes that file unwritable, that is expected -- rely on the final report and do **not**
report it as a missing tool. If you took no action, call `noop` with a one-line reason.
