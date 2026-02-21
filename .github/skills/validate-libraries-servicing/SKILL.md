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
- Given a specific version like "validate 10.0.4" or "March servicing for 9.0 and 10.0"
- Asked about upcoming servicing for a specific month like "March servicing" or "April Patch Tuesday"

## Step 1: Resolve Target Versions

The user can specify which servicing versions to validate in several ways. Parse the user's prompt to determine the target versions, then confirm with the user before proceeding.

### Input formats

The user may specify targets in any of these formats:

| User says | Meaning |
|-----------|---------|
| `10.0.4` | Explicit patch version |
| `10.0.4, 9.0.14, 8.0.25` | Multiple explicit versions |
| `10.0 servicing` | Next upcoming patch for .NET 10.0 |
| `March servicing` | All active versions' patches for March |
| `March 10.0 servicing` | .NET 10.0 patch for March specifically |
| `next servicing` or `upcoming servicing` | Next patch for all active versions |
| `April servicing` | Predicted versions for a future month |
| *(no version specified)* | Default to next patch for all active versions |

### Resolving versions

#### 1. Fetch current release data

Fetch the main .NET downloads page and each active version's page:

```
https://dotnet.microsoft.com/en-us/download/dotnet       (overview — latest versions and dates)
https://dotnet.microsoft.com/en-us/download/dotnet/10.0   (version history for 10.0)
https://dotnet.microsoft.com/en-us/download/dotnet/9.0    (version history for 9.0)
https://dotnet.microsoft.com/en-us/download/dotnet/8.0    (version history for 8.0)
```

From the overview page, extract for each active major version:
- **Latest release version** (e.g., `10.0.3`)
- **Latest release date** (e.g., `February 10, 2026`)
- **Support phase** (Active, Maintenance, or End of life)

Only include versions in Active or Maintenance support phase.

#### 2. Build the release cadence model

.NET servicing releases ship monthly on **Patch Tuesday** (the second Tuesday of each month). Each release increments the patch number by 1. Use this to build a version-to-month mapping:

From the latest shipped version and its release date, compute:
- **Current month**: the latest shipped version
- **Next month (+1)**: patch + 1
- **Month after (+2)**: patch + 2

Example (from February 2026 baseline):

| Month | .NET 8.0 | .NET 9.0 | .NET 10.0 |
|-------|----------|----------|-----------|
| Feb 2026 (shipped) | 8.0.24 | 9.0.13 | 10.0.3 |
| Mar 2026 (next) | 8.0.25 | 9.0.14 | 10.0.4 |
| Apr 2026 (predicted) | 8.0.26 | 9.0.15 | 10.0.5 |

For predictions beyond +1, note that they are **predicted** and the actual version may differ if a month is skipped or an out-of-band release occurs.

#### 3. Resolve the user's input

- **Explicit version** (e.g., `10.0.4`): Use directly. Validate it exists as a GitHub milestone.
- **Major version** (e.g., `10.0 servicing`): Look up the next unshipped patch from the cadence model.
- **Month name** (e.g., `March servicing`): Map the month to the cadence model. If the month is the current month or earlier, those versions are already shipped. If the month is 1-2 months ahead, use the predicted versions. If further out, warn that predictions become less reliable.
- **No input**: Default to the next unshipped patch for all active versions.

#### 4. Determine the release branch for each version

| Major Version | Branch Pattern | Example |
|--------------|----------------|---------|
| 8.0, 9.0 | `release/X.0-staging` | `release/8.0-staging` |
| 10.0+ | `release/X.0` | `release/10.0` |

#### 5. Present and confirm

Show the resolved versions to the user:

```
Target servicing versions:
  .NET 8.0  → 8.0.25  (branch: release/8.0-staging, last shipped: 8.0.24 on Feb 10)
  .NET 9.0  → 9.0.14  (branch: release/9.0-staging, last shipped: 9.0.13 on Feb 10)
  .NET 10.0 → 10.0.4  (branch: release/10.0, last shipped: 10.0.3 on Feb 10)
```

Use `ask_user` to confirm. The user may:
- Remove a version from scope (e.g., "skip 8.0")
- Correct a version number (e.g., "10.0 should be 10.0.5 not 10.0.4")
- Add a version not initially included

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
- **Version prediction limits**: Monthly cadence predictions are reliable for +1 month, reasonable for +2 months, and unreliable beyond that. Out-of-band releases, skipped months, or end-of-support can alter the pattern. Always warn the user when using predicted versions beyond +1.
- **End-of-support versions**: .NET 8.0 reaches end of support in November 2026, .NET 9.0 in November 2026. Do not include versions past their end-of-support date in predictions. The downloads overview page shows end-of-support dates.
- **GA release months**: A major version's first servicing release (X.0.1) ships the month after GA. The GA release itself (X.0.0) does not follow the Patch Tuesday cadence — it ships on its own schedule (typically November for LTS/STS releases).
