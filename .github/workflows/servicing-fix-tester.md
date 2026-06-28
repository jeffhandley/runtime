---
description: >
  Verify that merged dotnet/runtime servicing fixes actually resolve their issues. Runs twice daily
  to scan for merged servicing fixes that have a repro and have flowed into a daily SDK build, and can
  also be run manually for a single PR number. For each, it reuses the repro built by
  servicing-repro-producer, runs it on a baseline SDK (still buggy) and a fixed SDK (contains the fix),
  compares to the expected result, uploads both version-named logs as an artifact, writes a step
  summary, and posts a verdict comment to the PR (unless run in dry-run mode).

on:
  schedule: every 12h
  workflow_dispatch:
    inputs:
      pr_number:
        description: "Optional: a single merged servicing release PR number to verify (scan mode runs when empty)"
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
  actions: read

concurrency:
  group: "servicing-fix-tester-${{ github.event.inputs.pr_number || github.run_id }}"
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
    toolsets: [pull_requests, repos, issues, actions]
    min-integrity: approved
  edit:
  bash: ["dotnet", "git", "curl", "jq", "tee", "sed", "awk", "grep", "head", "tail", "cat", "ls", "find", "mkdir", "rm", "cp", "mv", "chmod", "echo", "date", "env", "test", "bash", "sh", "mktemp", "wc", "cut", "tr", "sort", "uniq", "xargs", "basename", "dirname", "unzip", "gh"]

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

timeout-minutes: 60
---

# Servicing Fix Tester

Verify that merged servicing fixes in `${{ github.repository }}` resolve their issues, using the
**`servicing-release` skill** at `.github/skills/servicing-release/SKILL.md`. Read that skill and
follow **Procedure B -- Verify a fix** for each PR you handle. You add no labels and modify no files.

## Mode

- **Single-PR mode** -- if `${{ github.event.inputs.pr_number }}` is non-empty, handle exactly that
  one PR.
- **Scan mode** -- otherwise (the schedule, or a manual run with no number), find merged servicing
  fixes that are ready to verify and handle up to **3** of them this run.

## Selecting PRs (scan mode)

Query `${{ github.repository }}` for pull requests targeting `release/*` branches (include
`release/*-staging`) that were **merged** within roughly the last 120 days. Read PR metadata and
comments through the integrity-gated `github` tool (skip `[Filtered]` items, record the count). Keep
a PR only if **all** hold:

- it is **rule-in** by the skill's PR classification rule;
- it has a prior `servicing-repro-producer` comment (gh-aw footer
  `workflow_id: servicing-repro-producer`);
- it has **no** prior `servicing-fix-tester` comment (gh-aw footer `workflow_id: servicing-fix-tester`);
- its fix commit (the PR's **merge commit**) has **flowed into the latest daily SDK build** for the
  target band (apply the skill's *fix-flow detection*: resolve the band's daily `runtime_commit` and
  use the GitHub compare API -- include only when the status is `behind`/`identical`).

Take up to **3** such PRs. PRs not yet flowed are left for a later run. If none qualify, call `noop`
with a one-line summary and stop.

## For each selected PR

Use a per-PR working directory outside any checkout (replace `<PR>` with the PR number):

```bash
export WORKDIR="/tmp/gh-aw/agent/servicing-fix/pr-<PR>"
rm -rf "$WORKDIR"; mkdir -p "$WORKDIR"; cd "$WORKDIR"
```

Determine the target `MAJOR.MINOR` from the PR's base branch and the fix commit (the PR's **merge
commit** SHA) via the GitHub API, then:

1. **Preconditions** (single-PR mode only -- already checked in scan mode): the PR is **merged** and
   rule-in, has a `servicing-repro-producer` comment, and has no `servicing-fix-tester` comment;
   otherwise `noop` with the reason.
2. **Confirm the fix has flowed** (single-PR mode only -- already checked in scan mode) per the
   skill's *fix-flow detection*. If not flowed yet, `noop` ("fix not yet in a daily build; will
   retry"). Resolve `BASELINE_SDK` (latest GA, lacks the fix) and `FIXED_SDK` (daily build with the
   fix).
3. **Obtain the repro.** Reuse the producer's repro **unchanged**: download it from the
   `servicing-repro-pr-<PR>` artifact of the most recent successful `servicing-repro-producer` run
   for this PR (GitHub Actions API; unzip into `$WORKDIR`). If it cannot be retrieved, re-derive the
   identical repro via the skill (Procedure A steps 1-3).
4. **Run on both SDKs** without changing the repro: on `BASELINE_SDK` capturing
   `$WORKDIR/output-baseline-<BASELINE_SDK>.log`, then on `FIXED_SDK` capturing
   `$WORKDIR/output-fixed-<FIXED_SDK>.log`.
5. **Render the verdict** by comparing both runs to the Expected result: **Verified fixed** (buggy on
   baseline, correct on fixed), **Not fixed** (still buggy on the fixed SDK), or **Inconclusive**
   (e.g. baseline did not exhibit the bug).
6. **Upload the artifact** for this PR: call `upload_artifact` with name
   `servicing-fix-test-pr-<PR>` and path `$WORKDIR` (the repro + both version-named logs).
7. **Comment (unless dry-run).** If `${{ github.event.inputs.suppress_output }}` is not `true`, post
   **one** comment on that PR via `add-comment` (set `pull_request_number`). The body must include,
   in order: (1) a reference to the repro used, linking the prior `servicing-repro-producer` repro
   comment when possible; (2) the **Expected Result**; (3) the **Actual result before the fix**, with
   the `BASELINE_SDK` version; (4) the **Actual result with the new SDK bits**, with the `FIXED_SDK`
   version; (5) the **verdict**. (gh-aw appends a footer identifying this workflow, used for dedup --
   no manual marker needed.) If `suppress_output` is `true`, skip the comment.

## Finish

Provide a clear final summary of each PR handled (repro source, expected, actual-before, actual-after,
verdict) -- gh-aw surfaces your final report as the run summary. Also write it to
`$GITHUB_STEP_SUMMARY` **best-effort**: the agentic sandbox often makes that file unwritable, which is
expected -- when it is, rely on the final report and the `step-summary.md` included in the artifact,
and do **not** report it as a missing tool. **If this run posts no comments**, you MUST call `noop`
with a one-line summary. All work happens under the per-PR `$WORKDIR`.
