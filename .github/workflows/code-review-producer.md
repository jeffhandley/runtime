---
description: "Review a pull request's changes for correctness, performance, and consistency with project conventions. Dispatched per-PR by the code-review-manager workflow."

permissions:
  contents: read
  issues: read
  pull-requests: read

network:
  allowed:
    - defaults

tools:
  github:
    mode: remote
    toolsets: [default, search]
  web-fetch:

checkout:
  fetch-depth: 50

safe-outputs:
  add-comment:
    max: 1
    target: "*"
    hide-older-comments: true
    discussions: false
    issues: false

timeout-minutes: 30

concurrency:
  group: code-review-${{ github.event.inputs.pr_number }}
  cancel-in-progress: true
  # Fan-out safety: without a per-PR discriminator, the compiler-generated agent /
  # safe_outputs / conclusion job concurrency groups are static across all dispatched
  # runs, so concurrently dispatched producers for different PRs would cancel each
  # other. The discriminator makes each PR's jobs a distinct concurrency slot, while
  # cancel-in-progress above still cancels a stale review when the SAME PR is pushed.
  job-discriminator: ${{ github.event.inputs.pr_number }}

if: |
  github.event_name == 'workflow_dispatch' || !github.event.repository.fork

on:
  workflow_dispatch:
    inputs:
      pr_number:
        description: 'Pull request number to review'
        required: true
        type: number
  # The manager dispatches this producer with GITHUB_TOKEN, so the run's actor is
  # github-actions[bot]. gh-aw's default membership gate (roles admin/maintainer/write)
  # denies bot actors, so we allowlist the github-actions bot. gh-aw's check_membership
  # authorizes an allowlisted bot when GitHub's collaborator-permission API returns any
  # non-404 response for it; for github-actions[bot] that API returns HTTP 200 with
  # permission "none" (verified against dotnet/runtime), which counts as an active bot ->
  # authorized. Human manual dispatch still requires write access via the default role
  # check. (roles: [all] would be simpler but is rejected by the v0.81.6 frontmatter
  # schema.) NOTE: confirm activation with one live dispatch before relying on it.
  bots: [github-actions]
  permissions: {}

# ###############################################################
# Override COPILOT_GITHUB_TOKEN with a random PAT from the pool.
# This stop-gap will be removed when org billing is available.
# See: .github/workflows/shared/pat_pool.README.md for more info.
# ###############################################################
imports:
  - shared/pat_pool.md

engine:
  id: copilot
  model: claude-opus-4.6
  env:
    COPILOT_GITHUB_TOKEN: ${{ case(needs.pat_pool.outputs.pat_number == '0', secrets.COPILOT_PAT_0, needs.pat_pool.outputs.pat_number == '1', secrets.COPILOT_PAT_1, needs.pat_pool.outputs.pat_number == '2', secrets.COPILOT_PAT_2, needs.pat_pool.outputs.pat_number == '3', secrets.COPILOT_PAT_3, needs.pat_pool.outputs.pat_number == '4', secrets.COPILOT_PAT_4, needs.pat_pool.outputs.pat_number == '5', secrets.COPILOT_PAT_5, needs.pat_pool.outputs.pat_number == '6', secrets.COPILOT_PAT_6, needs.pat_pool.outputs.pat_number == '7', secrets.COPILOT_PAT_7, needs.pat_pool.outputs.pat_number == '8', secrets.COPILOT_PAT_8, needs.pat_pool.outputs.pat_number == '9', secrets.COPILOT_PAT_9, secrets.COPILOT_GITHUB_TOKEN) }}
---

# Code Review Producer

You are an expert code reviewer for the dotnet/runtime repository. Your job is to review pull request #${{ github.event.inputs.pr_number }} and post a thorough analysis as a comment.

This workflow is dispatched per-PR by the `code-review-manager` workflow (or manually via `workflow_dispatch`) whenever a pull request is new or has had commits pushed.

## Step 0: Prepare Workspace

This workflow is triggered via `workflow_dispatch`, so the PR branch is **not** automatically checked out — the workspace contains the default branch. Before reviewing, you **must** fetch and check out the PR branch so the workspace reflects the PR's code:

```bash
git fetch origin pull/${{ github.event.inputs.pr_number }}/head:pr-branch
git checkout pr-branch
```

When posting the review via `add-comment`, include `item_number` set to `${{ github.event.inputs.pr_number }}` so the comment targets the correct PR.

## Step 1: Load Review Guidelines

Read the file `.github/skills/code-review/SKILL.md` from the repository. This contains the comprehensive code review process, analysis categories, output format, and verdict rules for dotnet/runtime.

## Step 2: Review and Post

Follow the instructions in SKILL.md to perform a thorough code review of PR #${{ github.event.inputs.pr_number }}.

**Important:** Before performing any analysis, check whether the PR has any actual code changes (lines added, removed, or modified). If the diff is empty (e.g., a merge commit with no effective changes), do **not** post a review comment. Simply stop without producing any output.

When completed, post the review output as a regular comment on the PR using the `add-comment` safe output.
