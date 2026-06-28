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
5. **Sandbox-robust commands (workflow context).** When running inside an agentic workflow, the bash
   harness gates ad-hoc network and complex shell, even for allow-listed tools: a bare `curl <url>`,
   `;`-chained compound commands, and pipelines may be **denied**. Therefore:
   - **Read GitHub-hosted data via the `github` MCP**, not `curl`: commit existence/ancestry
     (`get_commit`, compare) and raw files such as `dotnet/dotnet`'s `src/source-manifest.json`
     (`get_file_contents`). This is the robust primary path for fix-flow detection.
   - **Check pre-provisioned SDKs first** (`dotnet --list-sdks`); the runner usually already has the
     latest GA of each major, which is exactly the baseline. Only install when a needed SDK is absent.
   - **Run installs through the script, not bare curl:** invoke `bash "$WORKDIR/dotnet-install.sh" …`
     (a single, simple command); its internal downloads reach the firewall-allowed dotnet domains.
   - Prefer **one simple command per step**; avoid `;`-chaining, inline `$(…)` you can replace by
     writing to a file and reading it back, and long pipelines.
   Interactively (Copilot CLI), these gates do not apply -- use whatever commands you need.

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
   2. a **minimal console csproj** (`dotnet new console`, `<UseAppHost>false</UseAppHost>`) that prints
      `Expected` vs `Actual` -- this is the most portable form because it builds **offline** from the
      SDK's bundled ref/host packs and so runs on **daily/servicing SDKs** as well as GA;
   3. a **standalone file-based C# app** (`dotnet run app.cs`, .NET 10+ only) -- convenient, but **only
      when the repro will run exclusively on a public GA SDK**. Do **not** use it for a fix that will be
      fix-tested (Procedure B): on a daily SDK `dotnet run app.cs` tries to restore ILLink/ILCompiler at
      the unreleased patch version (not on public feeds) and fails.
   Author it under `"$WORKDIR"`. Keep it minimal -- only the APIs/types the fix touches. Because the
   tester reuses the producer's repro **unchanged** on a daily SDK, prefer forms 1 or 2.
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
   (`$WORKDIR` contents incl. `output.log`). In a workflow this becomes the step summary and the PR
   comment body; interactively it is reported to the user. Also save this report as
   `"$WORKDIR/step-summary.md"` so it travels with the artifact. **Never post anything yourself.**

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
   **verdict**, plus the artifact list (both version-named logs + the repro). Save this report as
   `"$WORKDIR/step-summary.md"` too. This becomes the workflow's step summary and verdict comment, or
   the interactive report. **Never post anything.**

---

## Reference: SDK installation

Use the official `dotnet-install` script; install side-by-side, never globally. **First check whether
a suitable SDK is already pre-provisioned** -- in a workflow the runner usually already has the latest
GA of each major (that GA is the baseline), so no download is needed:

```bash
dotnet --list-sdks            # is the baseline GA already here?
```

If you must install, fetch the script once and run installs **through `bash`** (a single simple
command -- the agentic harness gates bare `curl <url>`):

```bash
curl -fsSL https://builds.dotnet.microsoft.com/dotnet/scripts/v1/dotnet-install.sh -o "$WORKDIR/dotnet-install.sh"
chmod +x "$WORKDIR/dotnet-install.sh"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1

# Baseline = latest public GA of the target major (still has the bug). If not pre-provisioned:
GA_VERSION="$(curl -fsSL https://builds.dotnet.microsoft.com/dotnet/Sdk/${MAJOR}.${MINOR}/latest.version)"
bash "$WORKDIR/dotnet-install.sh" --version "$GA_VERSION" --install-dir "$WORKDIR/sdk-baseline" --no-path

# Fixed = latest daily build of the servicing feature band (may contain the just-merged fix):
#   band: 8 -> 8.0.4xx, 9 -> 9.0.3xx, 10 -> 10.0.1xx
bash "$WORKDIR/dotnet-install.sh" --channel "$BAND" --quality daily --install-dir "$WORKDIR/sdk-fixed" --no-path

# Use a specific SDK for a run:
export DOTNET_ROOT="$WORKDIR/sdk-baseline"; export PATH="$DOTNET_ROOT:$PATH"
dotnet --version
```

> In a workflow, if `curl` to fetch the script is itself denied, use a pre-provisioned baseline GA SDK
> (which covers the producer entirely). The **fixed daily** SDK is only needed by the fix-tester; if the
> runner cannot install it, the run should defer (report "fixed SDK not installable in this
> environment") rather than fabricate a result.

Notes: the install script and SDK tarballs are served from `builds.dotnet.microsoft.com`; daily
builds resolve via `aka.ms` → `ci.dot.net`. The runner is **linux-x64** in the workflows.

> **Daily-SDK package restore.** A daily/servicing SDK's matching runtime/ref packs are **not** on
> nuget.org yet. Keep repros restore-free (a `UseAppHost=false` console csproj or a unit test builds
> from the SDK's **bundled** packs). Only if a repro genuinely needs extra package refs at the
> unreleased patch version, add the daily feed to a local `nuget.config`:
> `https://pkgs.dev.azure.com/dnceng/public/_packaging/dotnet10/nuget/v3/index.json` (band-specific;
> `dotnet9`/`dotnet8` for the others).

## Reference: fix-flow detection (has the fix shipped in a daily SDK?)

Goal: given a fix commit `C` on `release/$MAJOR.0`, decide whether the **latest daily SDK** for the
band already contains it, and identify that SDK version.

**Sandbox-robust ordering.** First confirm `C` even exists in the repo via the `github` MCP
(`get_commit`); a release-branch PR whose merge commit is unknown to the product repo (e.g. a mirror
or simulated repo) can never have flowed -- report "not flowed" and stop. Resolve `dotnet/dotnet`'s
`src/source-manifest.json` with the `github` MCP `get_file_contents`, and run the ancestry check with
`gh api .../compare` (or the MCP compare). The only step that needs a non-GitHub fetch is reading the
daily build's `productCommit` from `ci.dot.net`; keep that as a single simple `curl` and, if it is
denied, treat the fix as "not yet verifiable in this environment" rather than guessing.

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

# 3) For .NET 10 (VMR), runtime_commit is a dotnet/dotnet SHA -> resolve the real dotnet/runtime commit.
#    Prefer the github MCP get_file_contents for dotnet/dotnet@${RUNTIME_COMMIT}:src/source-manifest.json;
#    repositories[path=="runtime"].commitSha is the dotnet/runtime commit. (curl shown as the fallback.)
if [ "$MAJOR" = 10 ]; then
  RUNTIME_COMMIT="$(curl -fsSL "https://raw.githubusercontent.com/dotnet/dotnet/${RUNTIME_COMMIT}/src/source-manifest.json" \
    | jq -r '.repositories[] | select(.path=="runtime") | .commitSha')"
fi

# 4) Has fix C flowed in? Prefer the github MCP compare; the gh api form is equivalent:
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
  passes once fixed. Run with `dotnet test`. Reusable across GA and daily SDKs (xunit/test-sdk
  packages are version-independent and on nuget.org).
- **Minimal console csproj (most portable).** `dotnet new console -o repro` with
  `<UseAppHost>false</UseAppHost>` (and no AOT/trim). It builds **offline** from the SDK's bundled
  `Microsoft.NETCore.App.Ref`/`.Host` packs, so the **same** project runs on a GA baseline **and** on a
  daily/servicing fixed SDK. Print `Expected: ...` / `Actual: ...`; optionally exit non-zero when buggy.
  Pin `<TargetFramework>` to the target major (e.g. `net10.0`). Example:
  ```xml
  <Project Sdk="Microsoft.NET.Sdk">
    <PropertyGroup>
      <OutputType>Exe</OutputType>
      <TargetFramework>net10.0</TargetFramework>
      <UseAppHost>false</UseAppHost>
    </PropertyGroup>
  </Project>
  ```
  Run framework-dependent: `dotnet build -c Release` then `dotnet bin/Release/<tfm>/repro.dll`.
- **File-based app (GA-only convenience).** A single `.cs` run via `dotnet run app.cs` (only on .NET
  10+ SDKs). **Avoid for anything that will be fix-tested:** on a daily/servicing SDK it tries to
  restore `Microsoft.DotNet.ILCompiler`/`Microsoft.NET.ILLink.Tasks` at the unreleased patch version
  (absent from public feeds) and fails restore. Safe only when the run targets a public GA SDK.
- **Output.** Always capture combined stdout+stderr to `output.log` (Procedure A) or
  `output-<role>-<sdkversion>.log` (Procedure B). The report must quote the **Actual** result
  directly from these logs -- never paraphrase a result you did not capture.
- **Trimming/AOT/runtime-config bugs** typically require a csproj (publish with `dotnet publish`),
  not a file-based app.
