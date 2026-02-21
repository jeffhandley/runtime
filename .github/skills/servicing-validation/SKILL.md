---
name: servicing-validation
description: Collect and curate PRs shipping in upcoming .NET servicing releases, then produce validation tests. Supports team-specific validation workflows. Use when asked to validate servicing releases, review upcoming patches, or generate servicing validation tests.
---

# Servicing Release Validation

Collect, classify, and curate the list of PRs shipping in upcoming .NET servicing releases, then produce team-specific validation tests for the fixes.

## When to Use This Skill

Use this skill when:
- Preparing validation tests for an upcoming .NET servicing release
- Reviewing what fixes are shipping in the next 8.0, 9.0, or 10.0 patch
- Asked to "validate servicing", "list servicing PRs", or "what's shipping next patch Tuesday"
- Given a specific version like "validate 10.0.4" or "March servicing for 9.0 and 10.0"
- Asked about upcoming servicing for a specific month like "March servicing" or "April Patch Tuesday"

## Team-Specific References

This skill supports team-specific validation workflows through reference files. After collecting and curating PRs (Steps 1–7), load the appropriate team reference for validation test generation guidance.

Available team references:
- **Libraries**: [references/team-libraries.md](references/team-libraries.md) — validation workflow for libraries fixes

If the user specifies a team (e.g., "libraries servicing validation"), load the corresponding reference early to inform component filtering. If no team is specified, ask the user which team's validation workflow to use before proceeding to test generation.

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
- Servicing label state: check for `Servicing-approved`, `Servicing-consider`, or `Servicing-rejected` labels

The servicing label state indicates where the PR is in the servicing approval process:
- **Servicing-approved**: Approved for inclusion in a servicing release
- **Servicing-consider**: Under consideration; not yet approved
- **Servicing-rejected**: Rejected for servicing inclusion

Record the label as `servicing_state` with values `approved`, `consider`, `rejected`, or `none`. This state is informational and does **not** filter PRs out of scope — for example, the user may want to see open PRs still in `Servicing-consider` state when reviewing upcoming milestones.

## Step 2b: Trace PR Lineage (Servicing PR → Main PR → Issue)

For each collected servicing PR, trace back through the chain: **Issue(s) → Main PR → Servicing PR(s)**.

### Extract the main PR reference

Most servicing PRs are backports from `main`. Extract the main PR number from:

1. **PR body first line**: `Backport of #NNNN to release/X.0` (automated backport bot pattern)
2. **PR body metadata**: `**Main PR**: #NNNN` (manual backport pattern)
3. **PR title**: `(#NNNN)` suffix in the title sometimes references the cherry-picked commit's original PR
4. **Branch name**: `backport/pr-NNNN-to-release/X.0` in the head ref

If none of these yield a main PR reference, the fix went **directly to the release branch** without a corresponding main branch PR. Flag this with `direct_to_release = true`.

### Extract the issue reference

Once you have the main PR number (or the servicing PR itself for direct fixes), find the issue(s) being fixed by checking these sources **in order**:

#### From the servicing PR body:
1. `Reported in https://github.com/dotnet/runtime/issues/NNNN`
2. `**Related issue**: #NNNN`
3. `Fixes https://github.com/dotnet/runtime/issues/NNNN`
4. Any `https://github.com/dotnet/runtime/issues/NNNN` URL in the body

#### From the main PR (fetch it via GitHub API `get` method):
1. `fix #NNNN` / `fixes #NNNN` / `fixed #NNNN` / `close #NNNN` / `closes #NNNN` / `resolve #NNNN` / `resolves #NNNN` (GitHub auto-close keywords)
2. `fix https://github.com/dotnet/runtime/issues/NNNN`
3. `Fixes https://github.com/dotnet/runtime/issues/NNNN`
4. Any `https://github.com/dotnet/runtime/issues/NNNN` URL in the body

**Important**: Distinguish issue URLs (`/issues/NNNN`) from PR URLs (`/pull/NNNN`). Only collect issue numbers, not PR cross-references.

### Handle unknown issues

If no issue reference is found after checking both the servicing PR and the main PR, mark the fix with `issue_unknown = true`. These will be highlighted in the output so the user can:
1. Manually provide the issue number
2. Add the issue reference to one of the involved PRs, then ask the skill to reload that PR's data

### Handle direct-to-release fixes

In rare cases, a fix goes directly into the release branch without a main PR. This happens when:
- The issue no longer reproduces on `main` because it was fixed by different work
- The fix is specific to the release branch's version of the code
- The fix was made directly by a maintainer without the backport workflow

For these PRs, extract the issue reference from the servicing PR body itself using the same patterns above.

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

## Step 4: Group by Fix Lineage

Group servicing PRs into **fix groups** where each group represents a single logical fix applied to one or more release branches.

### Grouping rules

1. **By main PR**: All servicing PRs that reference the same main PR (via "Backport of #NNNN") belong to the same fix group.
2. **By issue**: If two servicing PRs reference the same issue but different main PRs (e.g., a fix was reimplemented differently per version), group them together.
3. **Direct-to-release PRs**: These form their own fix group unless they reference an issue shared with another group.

### Fix group record

Each fix group should contain:
- **Issue(s)**: The GitHub issue number(s) being fixed, with titles
- **Main PR**: The PR merged to `main` (if any)
- **Servicing PRs**: Map of version → PR number for each release branch
- **Component**: Libraries, CoreCLR, Mono, etc.
- **Area**: The area label
- **Fix description**: From the main PR title (stripped of `[release/X.0]` prefix)
- **Issue unknown**: Flag if no issue could be identified
- **Direct to release**: Flag if there is no main PR

## Step 5: Present Results to the User

Display the results organized by fix lineage.

### Product-Source Fixes (In Scope for Validation)

Present each fix group as a row, showing the full lineage. Annotate servicing PR cells with the servicing label state when it is not `approved`:

```
| Issue | Fix Description | Component | Area | Main PR | 8.0 | 9.0 | 10.0 |
|-------|----------------|-----------|------|---------|-----|-----|------|
| #123586 | Fix Vector2/3 EqualsAny | Libraries | System.Numerics | #123594 | - | - | #124223 |
| #121193 | Fix binding IEnumerable<T> with empty array | Libraries | Extensions.Configuration | #121249 | - | - | #121325 |
| #124071 | Fix missing release semantics in VolatilePtr | CoreCLR | VM | #124096 | - | - | #124070 ⚑ |
| ⚠️ ? | [mono][hotreload] Ignore empty update | Mono | Mono | #120333 | #123547 | - | - |
| #125000 | Fix timeout in HttpClient | Libraries | Networking | #124900 | - | - | #125100 🔵 |
```

Legend:
- **⚑** = Direct to release (no backport from main; main PR was filed separately or fix went directly to release branch)
- **⚠️ ?** = Issue could not be identified — the user should add the issue reference to the PR
- **🔵** = Servicing-consider (not yet approved)
- **🔴** = Servicing-rejected

PRs with `Servicing-approved` state (or `none` for already-merged PRs) are shown without annotation. When presenting open PRs still in `Servicing-consider` or `Servicing-rejected` state, include the annotation so the user can factor the approval status into their decisions.

### Fixes with Unknown Issues

If any fix groups have `issue_unknown = true`, highlight them prominently:

```
⚠️  The following fixes could not be linked to a GitHub issue:

  • [mono][hotreload] Ignore empty update (main: #120333, servicing: #123547 for 8.0)
    → Add a "Fixes #NNNN" reference to PR #120333 or #123547, then say "reload #120333"

  • Fix EH profiler notifications (servicing: #123564 for 10.0)
    → This is a direct-to-release fix. Add a "Fixes #NNNN" reference to PR #123564, then say "reload #123564"
```

Explain to the user:
> Some fixes could not be linked to a GitHub issue. To resolve this, add an issue reference
> (e.g., `Fixes #NNNN`) to one of the involved PRs, then ask me to "reload #PRNUM" and I'll
> re-fetch the PR data and try to extract the issue reference again.

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
5. **Reload PR data** — re-fetch one or more PRs to pick up updated issue references
6. **Re-scan with different parameters** — re-run collection with adjusted versions or date range

### Reloading PR data

When the user says "reload #NNNN" or selects the reload option:
1. Re-fetch the specified PR's details from GitHub
2. Re-run the lineage tracing (Step 2b) for that PR
3. If a main PR reference was found, also re-fetch the main PR
4. Update the issue reference in the SQL database
5. Re-display the updated table

This is primarily used after the user has added issue references to PRs that had `issue_unknown = true`.

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

## Step 8: Load Team-Specific Validation Workflow

After the user confirms the curated list, load the appropriate team reference file to guide validation test generation. If the user specified a team in their original prompt, load it directly. Otherwise, ask:

> Which team's validation workflow should I use?

Then load the corresponding reference (e.g., [references/team-libraries.md](references/team-libraries.md)) and follow its instructions for producing validation tests from the curated fix list.

The team reference file defines:
- Which components/areas are relevant for that team
- How to generate validation tests for the team's fixes
- Any team-specific conventions or patterns

## SQL Tracking

Use the session SQL database to track collected PRs and their lineage. Create these tables:

### servicing_prs table

```sql
CREATE TABLE servicing_prs (
    pr_number INTEGER PRIMARY KEY,
    version TEXT,
    milestone TEXT,
    title TEXT,
    area TEXT,
    author TEXT,
    component TEXT,
    has_product_source INTEGER DEFAULT 1,
    classification TEXT DEFAULT 'product',
    in_scope INTEGER DEFAULT 1,
    main_pr INTEGER,
    direct_to_release INTEGER DEFAULT 0,
    servicing_state TEXT DEFAULT 'none'
);
```

### fix_groups table

```sql
CREATE TABLE fix_groups (
    group_id INTEGER PRIMARY KEY AUTOINCREMENT,
    main_pr INTEGER,
    fix_description TEXT,
    component TEXT,
    area TEXT,
    issue_unknown INTEGER DEFAULT 0,
    direct_to_release INTEGER DEFAULT 0
);
```

### fix_group_issues table

```sql
CREATE TABLE fix_group_issues (
    group_id INTEGER,
    issue_number INTEGER,
    issue_title TEXT,
    PRIMARY KEY (group_id, issue_number)
);
```

### fix_group_servicing_prs table

```sql
CREATE TABLE fix_group_servicing_prs (
    group_id INTEGER,
    pr_number INTEGER,
    version TEXT,
    PRIMARY KEY (group_id, pr_number)
);
```

### Key queries

Final curated list with lineage:
```sql
SELECT
    fg.group_id,
    fg.fix_description,
    fg.component,
    fg.area,
    fg.main_pr,
    fg.issue_unknown,
    fg.direct_to_release,
    GROUP_CONCAT(DISTINCT fgi.issue_number) as issues,
    GROUP_CONCAT(DISTINCT fgsp.pr_number || '(' || fgsp.version || ')') as servicing_prs
FROM fix_groups fg
LEFT JOIN fix_group_issues fgi ON fg.group_id = fgi.group_id
JOIN fix_group_servicing_prs fgsp ON fg.group_id = fgsp.group_id
JOIN servicing_prs sp ON fgsp.pr_number = sp.pr_number
WHERE sp.in_scope = 1 AND sp.has_product_source = 1
GROUP BY fg.group_id
ORDER BY fg.component, fg.area;
```

Track curation state with the `in_scope` column. When the user removes a PR, set `in_scope = 0`. When they add one back, set `in_scope = 1`.

## Important Notes

- **Rate limiting**: GitHub search API has secondary rate limits. If you hit a 403, wait 60 seconds and retry. Space out parallel searches.
- **Branch naming**: .NET 8.0 and 9.0 use `release/X.0-staging` branches. .NET 10.0+ uses `release/X.0` (no `-staging` suffix).
- **Milestone closures**: Milestones may be closed before the release ships. A closed milestone does not mean it already shipped — check the downloads page for the actual latest version.
- **Bot PRs to skip**: Filter out PRs authored by `dotnet-maestro[bot]`, `github-actions[bot]` (for merge PRs only — backport PRs from `github-actions[bot]` should be kept), and `dependabot[bot]`.
- **Backport detection**: The automated backport bot (`github-actions[bot]`) creates PRs with "Backport of #NNNN" in the body. These are real code changes and should NOT be excluded just because the author is a bot.
- **Version prediction limits**: Monthly cadence predictions are reliable for +1 month, reasonable for +2 months, and unreliable beyond that. Out-of-band releases, skipped months, or end-of-support can alter the pattern. Always warn the user when using predicted versions beyond +1.
- **End-of-support versions**: .NET 8.0 reaches end of support in November 2026, .NET 9.0 in November 2026. Do not include versions past their end-of-support date in predictions. The downloads overview page shows end-of-support dates.
- **GA release months**: A major version's first servicing release (X.0.1) ships the month after GA. The GA release itself (X.0.0) does not follow the Patch Tuesday cadence — it ships on its own schedule (typically November for LTS/STS releases).
