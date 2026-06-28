---
name: servicing-release
description: >
  Produce and verify minimal reproductions for .NET servicing-release fixes (dotnet/runtime
  release/* pull requests). USE FOR: deciding whether a release/* PR needs a repro, building a
  minimum reproduction from a servicing PR/issue, installing specific .NET SDKs locally to
  reproduce a bug, and verifying that a merged fix resolves the issue across a baseline SDK
  (exhibits the bug) and a fixed SDK (contains the fix). DO NOT USE FOR: posting to GitHub (the
  calling workflow owns comments; this skill never writes to GitHub), general code review (use the
  code-review skill), breaking-change documentation (use breaking-change-doc), or validating
  non-servicing changes that target main.
---

# Servicing Release Repro & Fix-Validation Skill

This skill turns a dotnet/runtime **servicing** pull request (one that targets a `release/*`
branch) into a **minimum reproduction** of the bug it fixes, runs that repro to confirm the bug,
and -- once the fix has shipped in a daily SDK build -- re-runs the same repro on a baseline SDK
(still buggy) and a fixed SDK (contains the fix) to verify the fix.

It is used in two execution contexts that share all of the core logic below:

- **Interactive** (the `servicing-release` Copilot CLI agent): read PRs/issues for context, produce
  **local artifacts only**, and report findings to the user. **Never** write to GitHub.
- **Agentic workflows** (`servicing-repro-producer`, `servicing-fix-tester`): do the same core work;
  the **workflow** posts the PR comment via safe-outputs. This skill itself never posts.

## Operating contract (applies in every context)

1. **Never write to GitHub from this skill.** No comments, issues, PRs, labels, or edits to any
   repository. Reading PR/issue/comment/code content for understanding is fine.
2. **Produce local artifacts only.** Author the repro and capture its output under a fresh, empty
   working directory **outside any git repository**:
   ```bash
   WORKDIR="${WORKDIR:-$(mktemp -d)}"; cd "$WORKDIR"   # caller may pre-set WORKDIR; never author inside a checkout
   ```
   Keep the repro sources, the project, and `output.log` here so the caller can collect them.
3. **Respect a user-supplied SDK.** If the caller has already provisioned an SDK (a path is given,
   or `DOTNET_ROOT`/`PATH` already point at a `dotnet` to use), **do not** discover, download, or
   install any SDK, and **do not** inspect that SDK's bits, version, or embedded commit. Just run
   the repro with it, capture the output, and report. (This supports validating an unreleased build.)
   Otherwise, install SDKs locally as described in *Reference: SDK installation*.
4. **Determinism & isolation.** Install SDKs with `--install-dir` + `--no-path` (no global machine
   changes). Set `DOTNET_CLI_TELEMETRY_OPTOUT=1`, `DOTNET_NOLOGO=1`,
   `DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1`. Never modify the runtime repo or global SDK.

---

## When to build a repro -- PR classification rule

A `release/*` PR earns a repro only if it is a **product bug fix**. Most release-branch PRs are
code-flow, infrastructure, branding, or test-only churn and must be **ignored**. Apply the rule
below using the PR's **labels, author, title, changed files, and body** (read user content through
the integrity-gated GitHub tool; skip `[Filtered]` results). Reading labels is fine -- this rule
never *adds* labels.

**Gate:** the PR's base branch matches `release/*` (including `release/*-staging`). **Ignore** if the
PR does **not** carry `Servicing-approved` **or** `Servicing-consider` (treat `Servicing-consider`
the same as approved -- a real fix awaiting formal approval). The defunct `blocking-servicing`
label carries no signal.

**EXCLUDE (ignore the PR) if any of the following match:**

- Label `area-codeflow` (≈50% of release PRs: Maestro dependency updates, VMR source updates,
  branding bumps, automated branch merges).
- Author `dotnet-maestro[bot]` or `dotnet-maestro-bot`.
- Title matches any of: `^\[automated\] Merge branch .* => .*`, `^Update branding to \d+\.\d+\.\d+`,
  `Source code updates from dotnet/dotnet`, `^\[release/[^\]]+\] Update dependencies from`,
  `^Revert ".*(Update dependencies|Source code updates).*"`.
- Infrastructure: any `area-Infrastructure*` or `area-Build-mono` label, **or** every changed file
  is under `eng/`, ends in `.yml`/`.yaml`, or is `global.json`/`NuGet.config`.
- Test-only: body says "testcode only" / "test-only change", **or** every changed file is under
  `src/tests/**`, `src/**/tests/**`, or `src/libraries/**/{tests,test,ref}/**`.
- "Merging internal commits for release/X.0" (bulk branch-flow with an empty body): not a single
  mappable fix -- skip in the automated path and report it as needing manual review.

**INCLUDE (build a repro) when, after the exclusions above:**

- The PR carries a **product area** label -- e.g. `area-GC-coreclr`, `area-CodeGen-coreclr`,
  `area-VM-coreclr`, `area-Interop-coreclr`, `area-Diagnostics-coreclr`, `area-NativeAOT-coreclr`,
  `area-AssemblyLoader-coreclr`, `area-PAL-coreclr`, `area-ExceptionHandling-coreclr`,
  `area-ILTools-coreclr`, mono runtime areas (`area-Codegen-*-mono`, `area-GC-mono`,
  `area-Interop-mono`, `area-Debugger-mono`), any `area-System.*`, `area-Tools-ILLink`, `area-Host`,
  `area-ReadyToRun`, `area-DependencyModel`; **and**
- Changed files include **product source**: `src/libraries/*/src/**`, `src/coreclr/**`
  (excluding tests/tools), `src/mono/**` (excluding tests), or `src/native/**`; **and**
- (Strongest signal) the body has a servicing **Customer Impact** section, `Fixes #NNNN` /
  `fixes https://github.com/.../issues/NNNN`, or a servicing milestone like `9.0.x`/`8.0.18`.

**Default:** if a PR survives the exclusions but the product-fix signal is weak/opaque, treat it as a
**low-confidence** candidate; it is acceptable to stop with a clear "no concrete repro could be
derived" result (a `noop` in workflow context).

> **Branch awareness.** For .NET 8 and .NET 9, servicing fixes land on `release/X.0-staging` first
> and later auto-merge into `release/X.0`; monitor both. .NET 10 has no staging branch and flows
> through the VMR ("Source code updates from dotnet/dotnet").

---

## Procedure A -- Produce a minimum repro (and confirm the bug)

Inputs: a release/* PR number (and its target major.minor, e.g. `8.0`).

1. **Classify** the PR with the rule above. If ruled out, report the reason and stop.
2. **Understand the bug.** Read the PR title, body (the *Customer Impact* template: "Customer
   reported", "Regression?", `Fixes #NNNN`, expected vs actual), linked issues, review comments, and
   the fix diff. **Trace the backport:** release PRs are usually backports -- follow the original
   `main` PR (title `(#NNNNN)` or body "Backport of #NNNNN") and its linked issue for the clearest
   bug description, customer scenario, and any shared code snippet. Distill:
   - a 1-2 sentence **issue summary**;
   - the **Expected** result;
   - the **Actual** (buggy) result;
   - the smallest **call site** that triggers it.
3. **Choose the repro form** (most-preferred first; pick the simplest that isolates the bug *and* is
   runnable on the target SDK -- see *Reference: repro forms*):
   1. a **unit test** (`dotnet new xunit`) whose assertion encodes the **Expected** behavior, so it
      **fails** on the buggy baseline and **passes** once fixed;
   2. a **standalone file-based C# app** that prints/writes `Expected` vs `Actual` (use single-file
      `dotnet run app.cs` only when the SDK is .NET 10+; otherwise use a minimal console project);
   3. a **csproj + source** app, only when the simpler forms cannot express the scenario.
   Author it under `"$WORKDIR"`. Keep it minimal -- only the APIs/types the fix touches.
4. **Provision the baseline SDK** (the latest public GA of the target major, which still exhibits the
   bug) unless a user-supplied SDK is in effect -- see *Reference: SDK installation*.
5. **Run and capture.** Build/run the repro with the baseline SDK, capturing combined stdout+stderr:
   ```bash
   { <run command>; echo "exit=$?"; } 2>&1 | tee "$WORKDIR/output.log"
   ```
   For a unit-test repro, `dotnet test`; for an app, `dotnet run`.
6. **Verify the bug reproduces.** Confirm `Actual` matches the buggy behavior (test fails / app prints
   the wrong result). If it does **not** reproduce, do not fabricate a result -- report that the bug
   could not be reproduced and what you tried, then stop.
7. **Report.** Produce: which repro **form** was used, the isolating **code snippet**, the **Expected**
   result, the **Actual** result (quoted from `output.log`), and the list of local artifacts
   (`$WORKDIR` contents incl. `output.log`). In a workflow this becomes the `GITHUB_STEP_SUMMARY` and
   the PR comment body; interactively it is reported to the user. **Never post anything yourself.**

---

## Procedure B -- Verify a fix (baseline vs fixed SDK)

Run this only after a repro exists for the PR and the **merged** PR's fix has **flowed into a daily
SDK build** (see *Reference: fix-flow detection*). Inputs: the PR number, its major.minor, the fix
commit SHA (the PR's merge commit), and the existing repro (reuse it unchanged).

1. **Obtain the repro.** Reuse the exact repro the producer authored (download it from the producer's
   uploaded artifact when available; otherwise re-derive it identically via Procedure A steps 1-3).
   **Do not change the repro** between the two runs.
2. **Confirm the fix has flowed** (skip + retry later if not) -- see *Reference: fix-flow detection*.
   Resolve `BASELINE_SDK` (latest GA, lacks the fix) and `FIXED_SDK` (the daily build that contains
   the fix commit).
3. **Run on the baseline SDK** (still buggy). Capture to a version-named log:
   ```bash
   { <run command>; echo "exit=$?"; } 2>&1 | tee "$WORKDIR/output-baseline-${BASELINE_SDK}.log"
   ```
4. **Install the fixed SDK and re-run the unchanged repro.** Capture to its own version-named log:
   ```bash
   { <run command>; echo "exit=$?"; } 2>&1 | tee "$WORKDIR/output-fixed-${FIXED_SDK}.log"
   ```
5. **Compare to Expected and render a verdict:** the bug must be present on `BASELINE_SDK` and
   absent on `FIXED_SDK` (the actual now equals Expected). Verdicts: **Verified fixed** (buggy →
   correct), **Not fixed** (still buggy on the fixed SDK), or **Inconclusive** (e.g. baseline did not
   exhibit the bug -- baseline selection may be off).
6. **Report.** Produce: a reference to the repro used, the **Expected** result, the **Actual before**
   (with the baseline SDK version), the **Actual after** (with the fixed SDK version), and the
   **verdict**, plus the artifact list (both version-named logs + the repro). This becomes the
   workflow's step summary and verdict comment, or the interactive report. **Never post anything.**

---

## Reference: SDK installation

Use the official `dotnet-install` script; install side-by-side, never globally:

```bash
curl -fsSL https://builds.dotnet.microsoft.com/dotnet/scripts/v1/dotnet-install.sh -o "$WORKDIR/dotnet-install.sh"
chmod +x "$WORKDIR/dotnet-install.sh"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1

# Baseline = latest public GA of the target major (still has the bug):
GA_VERSION="$(curl -fsSL https://builds.dotnet.microsoft.com/dotnet/Sdk/${MAJOR}.${MINOR}/latest.version)"
"$WORKDIR/dotnet-install.sh" --version "$GA_VERSION" --install-dir "$WORKDIR/sdk-baseline" --no-path

# Fixed = latest daily build of the servicing feature band (may contain the just-merged fix):
#   band: 8 -> 8.0.4xx, 9 -> 9.0.3xx, 10 -> 10.0.1xx
"$WORKDIR/dotnet-install.sh" --channel "$BAND" --quality daily --install-dir "$WORKDIR/sdk-fixed" --no-path

# Use a specific SDK for a run:
export DOTNET_ROOT="$WORKDIR/sdk-baseline"; export PATH="$DOTNET_ROOT:$PATH"
dotnet --version
```

Notes: the install script and SDK tarballs are served from `builds.dotnet.microsoft.com`; daily
builds resolve via `aka.ms` → `ci.dot.net`. The runner is **linux-x64** in the workflows.

## Reference: fix-flow detection (has the fix shipped in a daily SDK?)

Goal: given a fix commit `C` on `release/$MAJOR.0`, decide whether the **latest daily SDK** for the
band already contains it, and identify that SDK version.

```bash
case "$MAJOR" in 8) BAND=8.0.4xx;; 9) BAND=9.0.3xx;; 10) BAND=10.0.1xx;; esac

# 1) Resolve the latest daily SDK build version via the aka.ms redirect:
REDIRECT="$(curl -fsSIL --max-redirs 5 "https://aka.ms/dotnet/${BAND}/daily/productCommit-linux-x64.txt" \
  | awk 'tolower($1)=="location:"{print $2}' | tail -1 | tr -d '\r')"
FULL_VER="$(printf '%s' "$REDIRECT" | grep -oE 'Sdk/[^/]+' | head -1 | cut -d/ -f2)"   # e.g. 8.0.423-servicing.26316.4
FIXED_SDK="${FULL_VER%%-*}"                                                              # e.g. 8.0.423

# 2) Read the runtime commit baked into that SDK build:
PRODUCT="$(curl -fsSL "https://ci.dot.net/public/Sdk/${FULL_VER}/productCommit-linux-x64.txt")"
RUNTIME_COMMIT="$(printf '%s' "$PRODUCT" | grep -oE 'runtime_commit="[0-9a-f]{40}"' | head -1 | grep -oE '[0-9a-f]{40}')"

# 3) For .NET 10 (VMR), runtime_commit is a dotnet/dotnet SHA -> resolve the real dotnet/runtime commit:
if [ "$MAJOR" = 10 ]; then
  RUNTIME_COMMIT="$(curl -fsSL "https://raw.githubusercontent.com/dotnet/dotnet/${RUNTIME_COMMIT}/src/source-manifest.json" \
    | jq -r '.repositories[] | select(.path=="runtime") | .commitSha')"
fi

# 4) Has fix C flowed in? Use the GitHub compare API (no full clone needed):
#    status "behind"/"identical" => C is included in RUNTIME_COMMIT (fix HAS flowed); "ahead" => not yet.
STATUS="$(gh api "repos/${OWNER}/${REPO}/compare/${RUNTIME_COMMIT}...${C}" --jq '.status' 2>/dev/null || echo unknown)"
# behind|identical -> flowed: proceed to Procedure B. ahead -> not yet: skip and retry on a later run.
```

After installing, an SDK's runtime commit is the first line of
`shared/Microsoft.NETCore.App/<ver>/.version` (for .NET 10 that is a VMR SHA → resolve via the
`source-manifest.json` hop above). A lightweight alternative to step 1-3 is the SDK branch's
`eng/Version.Details.xml` `<Sha>` for `Microsoft.NETCore.App.Ref`.

> **Servicing timing.** Public flow takes ~6-12h minimum after merge, and .NET 8/9 servicing fixes
> often stay on the **internal** branch until Patch Tuesday -- so "not yet flowed" is normal and
> expected; skip and let a later run pick it up. If the SDK's runtime commit is not reachable in the
> public repo, the patch simply has not been publicly released yet.

## Reference: repro forms & output conventions

- **Unit test (preferred).** `dotnet new xunit -o repro`; write one `[Fact]`/`[Theory]` that asserts
  **Expected**. It fails on the buggy baseline (the assertion message shows the actual value) and
  passes once fixed. Run with `dotnet test`.
- **File-based app.** A single `.cs` run via `dotnet run app.cs` (only on .NET 10+ SDKs, which
  support file-based apps). Print `Expected: ...` and `Actual: ...`; optionally exit non-zero when
  buggy. For .NET 8/9 targets use a minimal console project instead.
- **csproj + source.** `dotnet new console -o repro` (or a small multi-file project) when the bug
  needs project settings (runtime config, trimming/AOT, target framework, package refs). Pin
  `<TargetFramework>` to the target major (e.g. `net8.0`).
- **Output.** Always capture combined stdout+stderr to `output.log` (Procedure A) or
  `output-<role>-<sdkversion>.log` (Procedure B). The report must quote the **Actual** result
  directly from these logs -- never paraphrase a result you did not capture.
- **Trimming/AOT/runtime-config bugs** typically require a csproj (publish with `dotnet publish`),
  not a file-based app.
