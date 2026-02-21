---
name: validate-libraries-servicing
description: Produce servicing release validation tests for libraries fixes. Collects PRs merged into release branches for upcoming servicing releases, classifies them, and lets the user curate the list. Use when asked to validate servicing releases, review upcoming patches, or generate servicing validation tests.
---

# Servicing Release Validation — Libraries Fixes

Collect, classify, and curate the list of PRs shipping in the upcoming .NET servicing releases so that validation tests can be produced for libraries fixes.

## When to Use This Skill

Use this skill when:
- Preparing validation tests for an upcoming .NET servicing release
- Reviewing what fixes are shipping in the next 8.0, 9.0, or 10.0 patch
- Asked to "validate servicing", "list servicing PRs", or "what's shipping next patch Tuesday"

## Step 1: Detect Current and Upcoming Versions

Fetch the .NET downloads page to determine the latest shipped versions:

```
https://dotnet.microsoft.com/en-us/download/dotnet
```

Extract the latest release version for each active major version (currently 8.0, 9.0, and 10.0). Compute the upcoming patch version by incrementing the patch number by one. For example:

| Version | Latest Shipped | Upcoming |
|---------|---------------|----------|
| .NET 8.0 | 8.0.24 | 8.0.25 |
| .NET 9.0 | 9.0.13 | 9.0.14 |
| .NET 10.0 | 10.0.3 | 10.0.4 |

Present this version table to the user for confirmation before proceeding. The user may indicate that a version should be skipped or that the upcoming version number is different.

## Step 2: Collect PRs from Milestones and Branches

For each upcoming version, collect merged PRs using two strategies:

### Strategy A: Milestone Search

Search for merged PRs assigned to the upcoming milestone:

```
milestone:<version> is:merged repo:dotnet/runtime
```

For example: `milestone:10.0.4 is:merged`

Also check the next milestone after that (e.g., `10.0.5`) and the catch-all milestone (e.g., `10.0.x`) since PRs already merged into the release branch may ship in the upcoming release even if milestoned for a later release.

### Strategy B: Branch + Date Search

Search for PRs merged into the release branch after the last release date:

```
base:release/<version>-staging is:merged merged:>YYYY-MM-DD  (for 8.0 and 9.0)
base:release/<version>       is:merged merged:>YYYY-MM-DD  (for 10.0+)
```

Exclude dependency update bots (`-author:dotnet-maestro[bot]`, `-label:dependencies`).

### Deduplication

Merge results from both strategies, deduplicating by PR number. Record for each PR:
- PR number and title
- Target branch and milestone (if any)
- Author
- Area labels
- Original PR number (extract from "Backport of #NNNN" in the PR body)

## Step 3: Classify Each PR

For each collected PR, fetch the list of changed files using the GitHub API (`get_files`). Classify as:

| Classification | Criteria | Include? |
|---------------|----------|----------|
| **product** | Modifies files under `src/libraries/*/src/`, `src/libraries/*/gen/`, `src/coreclr/`, `src/mono/`, or other product source paths | ✅ Yes |
| **test-only** | Only modifies files under `*/tests/`, `src/tests/`, or test asset directories | ❌ No |
| **infra-only** | Only modifies files under `eng/`, `.github/`, or CI pipeline configs | ❌ No |
| **dep-version-bump** | Only modifies `eng/Versions.props`, `global.json`, or NuGet config files | ❌ No |

### File path classification rules

A file is **product source** if its path matches any of:
- `src/libraries/*/src/**`
- `src/libraries/*/gen/**`
- `src/coreclr/**` (excluding `src/coreclr/tests/`)
- `src/mono/**` (excluding test directories)
- `src/native/**`

A file is **test source** if its path matches any of:
- `src/libraries/*/tests/**`
- `src/libraries/Common/tests/**`
- `src/tests/**`
- `**/testassets/**`
- `**/Wasm.Build.Tests/**`

A file is **infrastructure** if its path matches any of:
- `eng/**`
- `.github/**`
- `*.yml` or `*.yaml` under pipeline directories

A PR has `product` classification if **at least one** changed file is product source. Otherwise, it gets the most specific non-product classification.

### Determine component

Based on the changed files and area labels, assign each product PR a component:
- **Libraries** — changes under `src/libraries/`
- **CoreCLR** — changes under `src/coreclr/`
- **Mono** — changes under `src/mono/`
- **Host** — changes under `src/native/corehost/` or `src/installer/`
- **Mixed** — changes spanning multiple components

## Step 4: Identify Cross-Version Fix Groups

Many servicing fixes are backported from `main` to multiple release branches. Group PRs that share the same original PR reference (from "Backport of #NNNN" in the body). This helps the user understand which fixes span multiple versions.

## Step 5: Present Results to the User

Display two tables:

### Product-Source PRs (In Scope for Validation)

Group by fix (cross-version), showing:

```
| Fix Description | Component | Area | 8.0 PR | 9.0 PR | 10.0 PR | Original PR |
|----------------|-----------|------|--------|--------|---------|-------------|
| Fix Vector2/3 EqualsAny | Libraries | System.Numerics | - | - | #124223 | #123594 |
```

### Excluded PRs (No Product Source Changes)

```
| PR | Version | Reason Excluded | Title |
|----|---------|-----------------|-------|
| #122570 | 9.0 | test-only | Fix ICustomQueryInterface test |
```

## Step 6: Interactive Curation

After presenting the tables, use `ask_user` to let the user curate the list. Offer these actions:

### Curation prompt

Ask the user:

> Here are the PRs identified for servicing validation. Would you like to adjust the list?

Provide choices:
1. **Looks good — proceed with this list**
2. **Remove PRs from scope** — then ask which PR numbers to remove
3. **Add excluded PRs back into scope** — then show the excluded list and ask which to add
4. **Add other PRs** — then ask for PR numbers (supports any dotnet/runtime PR: open, merged, any branch)
5. **Re-scan with different parameters** — re-run collection with adjusted versions or date range

### Adding arbitrary PRs

When the user provides arbitrary PR numbers:
1. Fetch the PR details from GitHub (`get` method)
2. Fetch changed files to classify the PR
3. If the PR is open or only merged to `main`, note this in the table with a status indicator
4. Add to the in-scope list with the appropriate classification

### Iterative curation

After each adjustment, re-display the updated table and ask again. Continue until the user selects "Looks good — proceed with this list."

## Step 7: Confirm Final List

Present the final curated list one more time with a summary:

```
Final servicing validation scope:
- N Libraries fixes across M versions
- N CoreCLR fixes
- N Mono fixes
- N total PRs

Proceed to generate validation tests?
```

Wait for explicit user confirmation before proceeding to test generation.

## SQL Tracking

Use the session SQL database to track collected PRs. Create a `servicing_prs` table:

```sql
CREATE TABLE servicing_prs (
    pr_number INTEGER PRIMARY KEY,
    version TEXT,
    milestone TEXT,
    title TEXT,
    area TEXT,
    original_pr INTEGER,
    author TEXT,
    component TEXT,
    has_product_source INTEGER DEFAULT 1,
    classification TEXT DEFAULT 'product',
    in_scope INTEGER DEFAULT 1
);
```

Track curation state with the `in_scope` column. When the user removes a PR, set `in_scope = 0`. When they add one back, set `in_scope = 1`.

Query the final curated list:
```sql
SELECT * FROM servicing_prs WHERE in_scope = 1 AND has_product_source = 1 ORDER BY component, area;
```

## Important Notes

- **Rate limiting**: GitHub search API has secondary rate limits. If you hit a 403, wait 60 seconds and retry. Space out parallel searches.
- **Branch naming**: .NET 8.0 and 9.0 use `release/X.0-staging` branches. .NET 10.0+ uses `release/X.0` (no `-staging` suffix).
- **Milestone closures**: Milestones may be closed before the release ships. A closed milestone does not mean it already shipped — check the downloads page for the actual latest version.
- **Bot PRs to skip**: Filter out PRs authored by `dotnet-maestro[bot]`, `github-actions[bot]` (for merge PRs only — backport PRs from `github-actions[bot]` should be kept), and `dependabot[bot]`.
- **Backport detection**: The automated backport bot (`github-actions[bot]`) creates PRs with "Backport of #NNNN" in the body. These are real code changes and should NOT be excluded just because the author is a bot.
