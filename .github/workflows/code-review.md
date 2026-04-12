---
description: "Review pull request changes for correctness, performance, and consistency with project conventions"

permissions:
  contents: read
  issues: read
  pull-requests: read

network: defaults

tools:
  github:
    mode: remote
    toolsets: [default, search]
    min-integrity: none
  web-fetch:

checkout:
  fetch-depth: 50

safe-outputs:
  report-failure-as-issue: false
  add-comment:
    max: 1
    target: ${{ github.event.pull_request.number || github.event.inputs.pr_number }}
    hide-older-comments: true
    allowed-reasons: [outdated]
    discussions: false
    issues: false
  hide-comment:
    max: 1
    allowed-reasons: [resolved]
    discussions: false

timeout-minutes: 45

concurrency:
  # Concurrency is evaluated before pre-activation runs, so it can't use the
  # exact authorization result. Only the reliable pre-activation signals are
  # used here: manual dispatch and whether the actor is the PR author.
  group: >-
    ${{
      (
        github.event_name == 'workflow_dispatch' ||
        github.actor == github.event.issue.user.login ||
        github.actor == github.event.pull_request.user.login
      ) &&
      format('code-review-{0}', github.event.pull_request.number || github.event.issue.number || github.event.inputs.pr_number) ||
      format('code-review-{0}-{1}', github.event.pull_request.number || github.event.issue.number || github.event.inputs.pr_number, github.actor)
    }}
  cancel-in-progress: true

if: github.event_name == 'workflow_dispatch' || needs.pre_activation.outputs.authorized == 'true'

on:
  # Allow all users to reach pre-activation so PR authors can invoke the workflow.
  # A custom pre-activation check below restricts actual activation to the PR
  # author or users with triage-or-higher repository access.
  roles: all
  slash_command:
    name: code-review
    events: [pull_request_comment,pull_request_review_comment]
  status-comment: false
  reaction: "eyes"

  workflow_dispatch:
    inputs:
      pr_number:
        description: 'Pull request number to review'
        required: true
        type: number

  steps:
    - name: Validate workflow_dispatch PR number
      if: ${{ github.event_name == 'workflow_dispatch' }}
      env:
        GH_TOKEN: ${{ github.token }}
        PR_NUMBER: ${{ github.event.inputs.pr_number }}
      run: |
        invalid_pr_message="The specified pr_number is not an open pull request number."

        if ! [[ "$PR_NUMBER" =~ ^[0-9]+$ ]]; then
          echo "::error::$invalid_pr_message"
          exit 1
        fi

        state="$(gh api "repos/${GITHUB_REPOSITORY}/pulls/${PR_NUMBER}" --jq .state 2>/dev/null)" || {
          echo "::error::$invalid_pr_message"
          exit 1
        }

        if [ "$state" != "open" ]; then
          echo "::error::$invalid_pr_message"
          exit 1
        fi

    - name: Authorize workflow invoker
      if: ${{ github.event_name == 'issue_comment' || github.event_name == 'pull_request_review_comment' }}
      id: authorize-invoker
      env:
        GH_TOKEN: ${{ github.token }}
        ACTOR: ${{ github.actor }}
        PR_NUMBER: ${{ github.event.pull_request.number || github.event.issue.number }}
      run: |
        set -euo pipefail

        echo "authorized=false" >> "$GITHUB_OUTPUT"

        if [ -z "${PR_NUMBER:-}" ]; then
          echo "::notice::Skipping code-review: unable to determine the target pull request."
          exit 0
        fi

        pr_info="$(gh api "repos/${GITHUB_REPOSITORY}/pulls/${PR_NUMBER}" --jq '[.state, .user.login] | @tsv' 2>/dev/null || true)"
        if [ -z "$pr_info" ]; then
          echo "::notice::Skipping code-review: unable to load pull request #${PR_NUMBER}."
          exit 0
        fi

        IFS=$'\t' read -r pr_state pr_author <<< "$pr_info"

        if [ "$ACTOR" = "$pr_author" ]; then
          echo "authorized=true" >> "$GITHUB_OUTPUT"
          exit 0
        fi

        role_name="$(gh api "repos/${GITHUB_REPOSITORY}/collaborators/${ACTOR}/permission" --jq .role_name 2>/dev/null || true)"
        case "$role_name" in
          admin|maintain|write|triage)
            echo "authorized=true" >> "$GITHUB_OUTPUT"
            ;;
          *)
            echo "::notice::Skipping code-review: only the PR author or users with triage or higher permission may invoke this workflow."
            ;;
        esac

  # ###############################################################
  # Override the COPILOT_GITHUB_TOKEN secret usage for the workflow
  # with a randomly-selected token from a pool of secrets.
  #
  # As soon as organization-level billing is offered for Agentic
  # Workflows, this stop-gap approach will be removed.
  #
  # See: /.github/actions/select-copilot-pat/README.md
  # ###############################################################

  # Add the pre-activation step of selecting a random PAT from the supplied secrets
    - if: ${{ github.event_name == 'workflow_dispatch' || steps.authorize-invoker.outputs.authorized == 'true' }}
      uses: actions/checkout@de0fac2e4500dabe0009e67214ff5f5447ce83dd # v6.0.2
      name: Checkout the select-copilot-pat action folder
      with:
        persist-credentials: false
        sparse-checkout: .github/actions/select-copilot-pat
        sparse-checkout-cone-mode: true
        fetch-depth: 1

    - if: ${{ github.event_name == 'workflow_dispatch' || steps.authorize-invoker.outputs.authorized == 'true' }}
      id: select-copilot-pat
      name: Select Copilot token from pool
      uses: ./.github/actions/select-copilot-pat
      env:
        SECRET_0: ${{ secrets.COPILOT_PAT_0 }}
        SECRET_1: ${{ secrets.COPILOT_PAT_1 }}
        SECRET_2: ${{ secrets.COPILOT_PAT_2 }}
        SECRET_3: ${{ secrets.COPILOT_PAT_3 }}
        SECRET_4: ${{ secrets.COPILOT_PAT_4 }}
        SECRET_5: ${{ secrets.COPILOT_PAT_5 }}
        SECRET_6: ${{ secrets.COPILOT_PAT_6 }}
        SECRET_7: ${{ secrets.COPILOT_PAT_7 }}
        SECRET_8: ${{ secrets.COPILOT_PAT_8 }}
        SECRET_9: ${{ secrets.COPILOT_PAT_9 }}

# Add the pre-activation output of the randomly selected PAT
jobs:
  pre-activation:
    outputs:
      authorized: ${{ steps.authorize-invoker.outputs.authorized }}
      copilot_pat_number: ${{ steps.select-copilot-pat.outputs.copilot_pat_number }}

# Override the COPILOT_GITHUB_TOKEN expression used in the activation job
# Consume the PAT number from the pre-activation step and select the corresponding secret
engine:
  id: copilot
  model: claude-opus-4.6
  env:
    # We cannot use line breaks in this expression as it leads to a syntax error in the compiled workflow
    # If none of the `COPILOT_PAT_#` secrets were selected, then the default COPILOT_GITHUB_TOKEN is used
    COPILOT_GITHUB_TOKEN: ${{ case(needs.pre_activation.outputs.copilot_pat_number == '0', secrets.COPILOT_PAT_0, needs.pre_activation.outputs.copilot_pat_number == '1', secrets.COPILOT_PAT_1, needs.pre_activation.outputs.copilot_pat_number == '2', secrets.COPILOT_PAT_2, needs.pre_activation.outputs.copilot_pat_number == '3', secrets.COPILOT_PAT_3, needs.pre_activation.outputs.copilot_pat_number == '4', secrets.COPILOT_PAT_4, needs.pre_activation.outputs.copilot_pat_number == '5', secrets.COPILOT_PAT_5, needs.pre_activation.outputs.copilot_pat_number == '6', secrets.COPILOT_PAT_6, needs.pre_activation.outputs.copilot_pat_number == '7', secrets.COPILOT_PAT_7, needs.pre_activation.outputs.copilot_pat_number == '8', secrets.COPILOT_PAT_8, needs.pre_activation.outputs.copilot_pat_number == '9', secrets.COPILOT_PAT_9, secrets.COPILOT_GITHUB_TOKEN) }}
---

# Code Review

You are an expert code reviewer for the dotnet/runtime repository. Your job is to review pull request #${{ github.event.pull_request.number || github.event.inputs.pr_number }} and post a thorough analysis as a comment.

{{#if github.event.inputs.pr_number}}
## Step 0: Prepare Workspace (workflow_dispatch only)

When this workflow is triggered via `workflow_dispatch`, the PR branch is **not** automatically checked out — the workspace contains the default branch. Before reviewing, you **must** fetch and check out the PR branch so the workspace reflects the PR's code:

```bash
git fetch origin pull/${{ github.event.inputs.pr_number }}/head:pr-branch
git checkout pr-branch
```

Additionally, when posting the review via `add-comment`, include `item_number` set to `${{ github.event.pull_request.number || github.event.inputs.pr_number }}` so the comment targets the correct PR.
{{/if}

## Step 1: Load Review Guidelines

Read the file `.github/skills/code-review/SKILL.md` from the repository. This contains the comprehensive code review process, analysis categories, output format, and verdict rules for dotnet/runtime.

## Step 2: Review and Post

Follow the instructions in SKILL.md to perform a thorough code review of PR #${{ github.event.pull_request.number || github.event.inputs.pr_number }}.

**Important:** Before performing any analysis, check whether the PR has any actual code changes (lines added, removed, or modified). If the diff is empty (e.g., a merge commit with no effective changes), do **not** post a review comment. Simply stop without producing any output.

When completed, post the review output as a regular comment on the PR using the `add-comment` safe output.

{{#if github.event.comment.id}}
## Step 3: Hide the slash_command Comment

If the triggering slash_command was from a `pull_request_comment` event, and the comment body **contained nothing except the slash command itself** (that is, after trimming whitespace it is exactly `/code-review`), then also call the `hide-comment` safe output to hide the invoking comment. First, use the triggering comment's REST id `${{ github.event.comment.id }}` to retrieve its GraphQL node ID via the GitHub tools, then use:

- `comment_id`: the triggering comment's GraphQL node ID
- `reason`: `"resolved"`

Do not hide anything for `pull_request_review_comment` or `workflow_dispatch` runs.
{{/if}}
