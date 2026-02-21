# Libraries Team — Servicing Validation Workflow

Team-specific guidance for validating libraries fixes in .NET servicing releases.

## Scope

The libraries team is responsible for validating fixes under:
- `src/libraries/*/src/**` — managed library source
- `src/libraries/*/gen/**` — source generators
- `src/libraries/Common/src/**` — shared source files

From the curated fix list, filter to fix groups where `component = 'Libraries'`. Present the libraries-scoped subset to the user for confirmation before generating tests.

CoreCLR, Mono, Host, and other component fixes are out of scope for the libraries team's validation and should be noted as deferred to other teams.

## Repro Discovery — Libraries

For each libraries fix group, search for a block of **C# code** that demonstrates the issue. The repro should be self-contained enough to run against an unfixed version and exhibit the bug.

### What qualifies as a repro

A usable repro is a block of C# code that:
- Can run as a **console app** (`Program.cs` with a `Main` method or top-level statements)
- Can run as a **unit test** (xUnit `[Fact]` or `[Theory]` method)
- Or is another **self-contained code snippet** that demonstrates the issue with clear expected vs. actual behavior

Look for patterns like:
- "**Expected Result**" / "**Actual Result**" or "**Expected**" / "**Actual**" headings
- "**Repro**" or "**Steps to Reproduce**" headings
- "**Minimal repro**" or "**Reproduction**" sections
- Code blocks with `Assert.*` calls showing expected behavior
- Code blocks followed by comments like `// throws InvalidOperationException` or `// returns incorrect result`

### Search strategy

For each fix group, search in this order:

1. **The linked issue body** — most customer-reported issues include a repro. Look for fenced code blocks (` ```csharp ` or ` ``` `) under headings like "Repro", "Steps to Reproduce", or "Description".

2. **The linked issue comments** — sometimes the original report is refined into a simpler repro in a follow-up comment, or a team member posts a simplified version. Fetch comments via `get_comments` and scan for code blocks.

3. **The main PR body** — the PR author may include a repro in the description, especially in the "Customer Impact" or "Testing" sections.

4. **The main PR review comments** — reviewers sometimes request or provide a repro during review. Fetch via `get_review_comments`.

5. **The servicing PR body** — the "Customer Impact" section in backport PRs sometimes includes a repro or links to one.

### Evaluating code blocks

When you find a code block, evaluate whether it's a usable repro:

- **Yes**: It uses the affected API, demonstrates the bug, and has clear expected/actual behavior
- **Partial**: It uses the affected API but doesn't clearly show expected vs. actual (note this; may still be usable)
- **No**: It's a code snippet showing the fix itself, a test helper, or unrelated code

Prefer the **simplest** repro that directly exercises the fixed code path. If multiple repros exist, pick the one closest to a standalone console app or unit test.

### Recording the repro

When a repro is found, record:
- **`repro_url`**: A GitHub permalink to the exact comment or issue body section containing the repro. Use anchored URLs when possible (e.g., `https://github.com/dotnet/runtime/issues/123586#issuecomment-1234567`)
- **`repro_notes`**: Brief description, e.g., "Console app in issue body demonstrating incorrect UInt128→double conversion for values ≥ 2^104"

When no repro is found, record:
- **`repro_found = false`**
- **`repro_notes`**: What was searched and why nothing qualified, e.g., "Issue has no code block; PR only references behavioral description"

## Validation Approach

### Repro Project Creation

For each libraries fix group with a discovered repro, create a repro project under `src/tests/Regressions/libraries/`.

#### Folder naming

Use the **deepest ancestor** in the PR lineage as the folder name:

1. **Issue number** (preferred): If the fix group has a linked issue, use it (e.g., `123586/`)
2. **Main PR number**: If no issue is linked, use the main PR number (e.g., `123594/`)
3. **Servicing PR number**: If neither issue nor main PR is clear, use the servicing PR number

When a fix group has multiple issues or PRs, pick the number that best represents the root cause — typically the original customer-reported issue.

#### File structure

For simple repros that can be expressed as a single file, create a `.cs` file alongside a `RESULTS_NNNN.md` tracking file:

```
src/tests/Regressions/libraries/
  123586/
    repro_123586.cs
    RESULTS_123586.md
  123422/
    repro_123422.cs
    RESULTS_123422.md
```

#### Source file format

Each repro file should follow this structure:

```csharp
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// Repro for: https://github.com/dotnet/runtime/issues/123586
// Main PR:   https://github.com/dotnet/runtime/pull/123594
// Servicing: https://github.com/dotnet/runtime/pull/124223 (10.0.4)
// Repro from: https://github.com/dotnet/runtime/issues/123586#issuecomment-...
//
// Vector2/3 EqualsAny returns incorrect results for certain input values.
// Expected: EqualsAny returns true when at least one component matches.
// Actual:   EqualsAny returns false even when components match.

using System;
using System.Numerics;

class Repro_123586
{
    static int Main()
    {
        // ... minimal repro code from the issue ...

        // Return 0 for success (issue is fixed), non-zero for failure (issue reproduced)
        return 0;
    }
}
```

Key conventions:
- **MIT license header** at the top
- **Lineage comments**: Link to the issue, main PR, servicing PR(s), and the repro source URL
- **Repro notes**: Brief description of expected vs. actual behavior
- **Class name**: `Repro_NNNN` matching the folder/file number
- **Return code convention**: Return `0` when the fix is working correctly; return non-zero (e.g., `1`) when the bug is reproduced. This means the repro should *fail* (return non-zero) on an unfixed version and *pass* (return `0`) on a fixed version.

#### Parallel creation

Use background agents or parallel tool calls to create all repro files simultaneously. Each repro is independent and can be created without waiting for others.

After all repros are created, present the results to the user with file paths and a brief description of each.

#### RESULTS file format

Create `RESULTS_NNNN.md` alongside each repro file during the repro project creation step. This file tracks which versions need testing and aggregates results as runs complete.

```markdown
# Results: repro_123586 — Vector2/3 EqualsAny

Issue: https://github.com/dotnet/runtime/issues/123586
Main PR: https://github.com/dotnet/runtime/pull/123594
Servicing PRs: #124223 (10.0.4)

## Baseline (expect failure — bug should reproduce)

- [ ] .NET 10.0.3 — *(not yet run)*

## Validation (expect pass — fix should resolve)

- [ ] .NET 10.0.4 — *(not yet run)*
```

The checklist includes every version pair needed: the last shipped version (baseline, expect failure) and the servicing version (validation, expect pass). For fixes targeting multiple versions, include all applicable pairs:

```markdown
## Baseline (expect failure — bug should reproduce)

- [ ] .NET 8.0.24 — *(not yet run)*
- [ ] .NET 9.0.13 — *(not yet run)*
- [ ] .NET 10.0.3 — *(not yet run)*

## Validation (expect pass — fix should resolve)

- [ ] .NET 8.0.25 — *(not yet run)*
- [ ] .NET 9.0.14 — *(not yet run)*
- [ ] .NET 10.0.4 — *(not yet run)*
```

As runs complete, update checkboxes and append the result:

```markdown
- [x] .NET 10.0.3 — ❌ FAIL (exit code 1) — bug reproduced as expected
- [x] .NET 10.0.4 — ✅ PASS (exit code 0) — fix confirmed
```

### Test Execution

See [references/test-execution.md](test-execution.md) for the detailed procedure for running repro apps against available .NET versions, SDK discovery, installation prompts, and result recording.
