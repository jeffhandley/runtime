---
description: "Orchestrator (OrchestratorOps): polls open pull requests every 10 minutes and dispatches the code-review-worker for each PR that is new or has had commits pushed. Deterministic steps compute the work; the agent only fans out via the dispatch-workflow safe output."

on:
  schedule: every 10m
  workflow_dispatch:
  permissions: {}

if: |
  github.event_name == 'workflow_dispatch' || !github.event.repository.fork

permissions:
  contents: read
  pull-requests: read

concurrency:
  # Serialize orchestrator runs; queue overlapping ticks rather than cancelling so the
  # reviewed-SHA cache is never written by two runs at once.
  group: code-review-orchestrator
  cancel-in-progress: false

timeout-minutes: 15

network:
  allowed:
    - defaults

tools:
  # The agent only reads the precomputed dispatch list (a deterministic step already
  # queried PRs via `gh`) and relays it to the dispatch-workflow safe output, so it needs
  # no GitHub MCP tool -- avoiding the remote MCP gateway dependency entirely.
  bash: [cat, jq]

# OrchestratorOps fan-out: the agent dispatches the worker via this safe output (workflow_dispatch),
# up to MAX per run (BatchOps throttle). The compiler validates code-review-worker exists and
# declares workflow_dispatch.
safe-outputs:
  # Threat detection is disabled: this orchestrator only relays a deterministic list of PR
  # numbers to a trusted same-repo worker via dispatch-workflow. There is no untrusted content
  # being turned into a write, and the detection LLM pass (whose parse failures conclude
  # "warning") would otherwise block dispatch under the WTD3 non-reviewable-output policy.
  threat-detection: false
  dispatch-workflow:
    workflows: [code-review-worker]
    max: 20

# DeterministicOps + WorkQueueOps: the durable per-PR reviewed-SHA map lives in an OWN
# actions/cache (NOT gh-aw `cache-memory`, whose restore is injected AFTER frontmatter steps and
# is only visible to the agent -- that ordering would make pre-agent dedup read empty state every
# run). Restore -> compute -> save are ordered explicitly here so the state is read before, and
# persisted after, the compute. The agent below only relays the precomputed list to
# dispatch-workflow. (Pin actions/cache to a SHA before production per repo policy.)
steps:
  - name: Restore reviewed-SHA state
    uses: actions/cache/restore@v4
    with:
      path: .review-state
      key: code-review-state-${{ github.run_id }}
      restore-keys: |
        code-review-state-

  - name: Build dispatch list and advance reviewed-SHA state
    shell: bash
    env:
      GH_TOKEN: ${{ github.token }}
      MAX_DISPATCH: '20'
    run: |
      set -euo pipefail
      mkdir -p .review-state /tmp/gh-aw/agent
      STATE=".review-state/reviewed-shas.json"
      [ -f "$STATE" ] || echo '{}' > "$STATE"

      # POLL: open, non-draft PRs with their current head SHA.
      #
      # SECURITY NOTE (fork PRs): dispatching the worker runs it in BASE-REPO context on the
      # default branch WITH secrets (the Copilot PAT pool), and the worker then checks out the
      # PR's (possibly fork-owned) head to review it. This is broader than a pull_request
      # trigger, under which fork PRs run without secrets. The worker is read-only,
      # egress-firewalled to `defaults`, posts a single sanitized comment, and the PATs are
      # Copilot-Requests-only -- so the exposure is bounded but non-zero. Fork PRs are included
      # by default (goal: review EVERY PR). To auto-review only same-repo branches, add
      # `and .isCrossRepository == false` to the select below.
      gh pr list --repo "$GITHUB_REPOSITORY" --state open --limit 1000 \
        --json number,headRefOid,isDraft,updatedAt,isCrossRepository \
        --jq '[.[] | select(.isDraft == false)]' > /tmp/open_prs.json
      echo "Open non-draft PRs: $(jq 'length' /tmp/open_prs.json) (forks included)"

      # QUEUE (WorkQueueOps): PRs whose current head SHA differs from the last-dispatched SHA
      # (new PRs match because they have no recorded SHA -- covers any update path: push,
      # force-push, rebase, reopen, base merge). Oldest-updated first for fairness; throttle to
      # MAX_DISPATCH (BatchOps).
      SEEN="$(cat "$STATE")"
      jq -n \
        --slurpfile prs /tmp/open_prs.json \
        --argjson seen "$SEEN" \
        --argjson max "$MAX_DISPATCH" '
          $prs[0]
          | [ .[] | select( ($seen[(.number | tostring)] // "") != .headRefOid ) ]
          | sort_by(.updatedAt)
          | .[:$max]
          | map({pr_number: .number, head_sha: .headRefOid})
        ' > /tmp/gh-aw/agent/dispatch_list.json
      echo "Queued this run: $(jq 'length' /tmp/gh-aw/agent/dispatch_list.json)"
      cat /tmp/gh-aw/agent/dispatch_list.json

      # Advance state optimistically for queued PRs, and prune closed PRs so state stays bounded.
      # NOTE (fire-and-forget): state records dispatch INTENT, not review completion, and it is
      # advanced here BEFORE the agent fans out. If the agent under-dispatches (fails to emit a
      # dispatch-workflow call for an item) or a worker run later fails, that PR is marked
      # reviewed-at-this-SHA and is not retried until its head changes again. Acceptable for this
      # polling model: the next real push re-queues the PR, and a maintainer can manually dispatch
      # code-review-worker for a specific PR. If missed reviews are observed, use a more capable
      # engine.model below, or emit the dispatch-workflow safe outputs deterministically from this
      # step instead of relying on the agent to fan out.
      tmp="$(mktemp)"
      jq \
        --slurpfile queued /tmp/gh-aw/agent/dispatch_list.json \
        --slurpfile prs /tmp/open_prs.json '
          ($prs[0] | map(.number | tostring)) as $open
          | reduce $queued[0][] as $q (.; .[($q.pr_number | tostring)] = $q.head_sha)
          | with_entries(select(.key | IN($open[])))
        ' "$STATE" > "$tmp" && mv "$tmp" "$STATE"

  - name: Save reviewed-SHA state
    if: always()
    uses: actions/cache/save@v4
    with:
      path: .review-state
      key: code-review-state-${{ github.run_id }}

# ###############################################################
# Override COPILOT_GITHUB_TOKEN with a random PAT from the pool.
# This stop-gap will be removed when org billing is available.
# See: .github/workflows/shared/pat_pool.README.md for more info.
# ###############################################################
imports:
  - shared/pat_pool.md

engine:
  id: copilot
  # The agent only relays a small precomputed list to the dispatch-workflow safe output, so the
  # subscription default model is sufficient (leaving model unset avoids tier-entitlement issues).
  env:
    COPILOT_GITHUB_TOKEN: ${{ case(needs.pat_pool.outputs.pat_number == '0', secrets.COPILOT_PAT_0, needs.pat_pool.outputs.pat_number == '1', secrets.COPILOT_PAT_1, needs.pat_pool.outputs.pat_number == '2', secrets.COPILOT_PAT_2, needs.pat_pool.outputs.pat_number == '3', secrets.COPILOT_PAT_3, needs.pat_pool.outputs.pat_number == '4', secrets.COPILOT_PAT_4, needs.pat_pool.outputs.pat_number == '5', secrets.COPILOT_PAT_5, needs.pat_pool.outputs.pat_number == '6', secrets.COPILOT_PAT_6, needs.pat_pool.outputs.pat_number == '7', secrets.COPILOT_PAT_7, needs.pat_pool.outputs.pat_number == '8', secrets.COPILOT_PAT_8, needs.pat_pool.outputs.pat_number == '9', secrets.COPILOT_PAT_9, secrets.COPILOT_GITHUB_TOKEN) }}
---

# Code Review Orchestrator

You dispatch code-review workers. A deterministic step has already computed the work for this run.

Read the JSON array at `/tmp/gh-aw/agent/dispatch_list.json`. Each element is `{ "pr_number": <number>, "head_sha": "<sha>" }` for a pull request that is new or has had commits pushed and therefore needs a (re)review.

1. If the array is empty, do nothing and stop.
2. Otherwise, for **every** element in the array, call the **`code_review_worker`** tool (the dispatch-workflow tool for the `code-review-worker` workflow), passing `pr_number` set to that element's `pr_number`. Call the tool once per element. Do **not** hand-write or `echo` any JSON yourself and do **not** use any shell command to emit output -- only invoke the `code_review_worker` tool, which records the dispatch for you.
3. You **must** dispatch **every** element in the list -- the reviewed-SHA state has already been advanced for all of them, so any element you skip will not be re-queued until its head changes again. Do **not** dispatch any pull request that is not in the list, and do **not** review pull requests yourself -- the worker performs the actual review.

After calling the tool for every element, briefly confirm how many workers you dispatched.
