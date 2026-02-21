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

*(To be defined — this section will describe how the libraries team produces validation tests from the discovered repros, including test patterns, project structure, and execution.)*
