---
description: >
  Build and verify minimum reproductions for dotnet/runtime servicing release PRs (PRs targeting a
  release/* branch). Runs hourly to scan for rule-in servicing fixes that still need a repro, and can
  also be run manually for a single PR number. For each PR it installs the baseline GA SDK, authors a
  minimal repro, runs it to confirm the bug, uploads the repro + output.log as an artifact, writes a
  step summary, and posts a repro comment to the PR (unless run in dry-run mode).

on:
  schedule: hourly
  workflow_dispatch:
    inputs:
      pr_number:
        description: "Optional: a single servicing release PR number to build a repro for (scan mode runs when empty)"
        required: false
        type: string
      suppress_output:
        description: "Dry-run: produce the artifact + step summary but do NOT post a PR comment"
        required: false
        type: boolean
        default: false
  permissions: {}

if: ${{ (!github.event.repository.fork) }}

permissions:
  contents: read
  pull-requests: read
  issues: read

concurrency:
  group: "servicing-repro-producer-${{ github.event.inputs.pr_number || github.run_id }}"
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
  edit:
  bash: ["dotnet", "git", "curl", "jq", "tee", "sed", "awk", "grep", "head", "tail", "cat", "ls", "find", "mkdir", "rm", "cp", "mv", "chmod", "echo", "date", "env", "test", "bash", "sh", "mktemp", "wc", "cut", "tr", "sort", "uniq", "xargs", "basename", "dirname", "gh"]

checkout: false

network:
  allowed:
    - defaults
    - github
    - dotnet
    - "aka.ms"
    - linux-distros

safe-outputs:
  add-comment:
    target: "*"
    max: 3
    hide-older-comments: true
  upload-artifact:
    max-uploads: 3
    retention-days: 30
    allowed-paths:
      - "/tmp/gh-aw/agent/**"
    defaults:
      if-no-files: "ignore"
  noop:

timeout-minutes: 45
---

# Servicing Repro Producer

Produce and verify minimum reproductions for servicing release PRs in `${{ github.repository }}`,
using the **`servicing-release` skill** at `.github/skills/servicing-release/SKILL.md`. Read that
skill and follow **Procedure A -- Produce a minimum repro** for each PR you handle. You add no labels
and modify no files in the repository.

## Mode

- **Single-PR mode** -- if `${{ github.event.inputs.pr_number }}` is non-empty, handle exactly that
  one PR.
- **Scan mode** -- otherwise (the hourly schedule, or a manual run with no number), find the
  servicing PRs that still need a repro and handle up to **3** of them this run.

## Selecting PRs (scan mode)

Query `${{ github.repository }}` for **open** pull requests targeting `release/*` branches (include
`release/*-staging`), updated within roughly the last 30 days. Read PR metadata and bodies through
the integrity-gated `github` tool (skip `[Filtered]` items, record the count). Keep a PR only if:

- it is **rule-in** by the skill's PR classification rule (else skip it); **and**
- it has **no** prior `servicing-repro-producer` comment (identify such comments by the gh-aw footer
  containing `workflow_id: servicing-repro-producer`).

Take up to **3** such PRs (oldest-updated first). If none qualify, call `noop` with a one-line
summary and stop.

## For each selected PR

Use a per-PR working directory outside any checkout so each repro can be uploaded separately (replace
`<PR>` with the PR number):

```bash
export WORKDIR="/tmp/gh-aw/agent/servicing-repro/pr-<PR>"
rm -rf "$WORKDIR"; mkdir -p "$WORKDIR"; cd "$WORKDIR"
```

Determine the target `MAJOR.MINOR` from the PR's base branch (`release/MAJOR.MINOR` or
`release/MAJOR.MINOR-staging`), then:

1. **Classify** the PR (single-PR mode only -- in scan mode this was already done). If it is ruled
   out (code-flow, infrastructure, branding, test-only, missing `Servicing-approved`/
   `Servicing-consider`, etc.), skip it; in single-PR mode call `noop` with the reason.
2. **Dedup** (single-PR mode only): if a `servicing-repro-producer` comment already exists for the
   PR (gh-aw footer `workflow_id: servicing-repro-producer`), skip it (single-PR mode: `noop`
   "repro already posted").
3. **Produce + verify** the repro per Procedure A on the **baseline GA SDK** for the target major.
   Capture combined output to `$WORKDIR/output.log`. If the bug does **not** reproduce, record that
   for the step summary and do **not** post a comment for this PR.
4. **Upload the artifact** for this PR: call `upload_artifact` with name
   `servicing-repro-pr-<PR>` and path `$WORKDIR`.
5. **Comment (unless dry-run).** If the bug reproduced and `${{ github.event.inputs.suppress_output }}`
   is not `true`, post **one** comment on that PR via `add-comment` (set `pull_request_number`). The
   body must include, in order: (1) a 1-2 sentence description of the issue; (2) which repro approach
   was used (unit test / file-based app / csproj); (3) the isolating code snippet; (4) **Expected
   Result**; (5) **Actual Result** (quoted from `output.log`); (6) a link to the uploaded artifact.
   (gh-aw appends a footer identifying this workflow, used for dedup -- no manual marker needed.) If
   `suppress_output` is `true`, skip the comment.

## Finish

Write a `GITHUB_STEP_SUMMARY` summarizing each PR handled (repro form, reproduced yes/no). **If this
run posts no comments** (nothing qualified, all ruled out, or none reproduced), you MUST call `noop`
with a one-line summary. All repro work happens under the per-PR `$WORKDIR`.
