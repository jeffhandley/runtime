---
name: servicing-validation
description: Collect and curate PRs shipping in upcoming .NET servicing releases, then produce validation tests. Supports team-specific validation workflows. Use when asked to prepare, conduct, or run servicing validation, review upcoming patches, or generate servicing validation tests.
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

This skill supports team-specific validation workflows through reference files. After collecting and curating PRs (Steps 1–8), load the appropriate team reference for validation test generation guidance.

Available team references:
- **Libraries**: [references/team-libraries.md](references/team-libraries.md) — validation workflow for libraries fixes

If the user specifies a team (e.g., "libraries servicing validation"), load the corresponding reference early to inform component filtering. If no team is specified, ask the user which team's validation workflow to use before proceeding to test generation.

## Step 1: Resolve Target Versions

The user can specify which servicing versions to validate in several ways. Parse the user's prompt to determine the target versions, then confirm with the user before proceeding.

### Input formats

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

Fetch the .NET downloads overview page and each active version's page to get current release data:

```
https://dotnet.microsoft.com/en-us/download/dotnet       (overview — latest versions and dates)
https://dotnet.microsoft.com/en-us/download/dotnet/10.0   (version history for 10.0)
https://dotnet.microsoft.com/en-us/download/dotnet/9.0    (version history for 9.0)
https://dotnet.microsoft.com/en-us/download/dotnet/8.0    (version history for 8.0)
```

Extract for each active major version: latest release version, release date, and support phase. Only include versions in Active or Maintenance support.

.NET servicing releases ship monthly on **Patch Tuesday** (the second Tuesday of each month), incrementing the patch number by 1. Build a cadence model from the latest shipped versions to resolve the user's input:

- **Explicit version** (e.g., `10.0.4`): Use directly.
- **Major version** (e.g., `10.0 servicing`): Look up the next unshipped patch.
- **Month name** (e.g., `March servicing`): Map the month to the cadence model. Warn if predictions are beyond +1 month.
- **No input**: Default to the next unshipped patch for all active versions.

### Release branch mapping

| Major Version | Branch Pattern | Example |
|--------------|----------------|---------|
| 8.0, 9.0 | `release/X.0-staging` | `release/8.0-staging` |
| 10.0+ | `release/X.0` | `release/10.0` |

### Present and confirm

Show the resolved versions and use `ask_user` to confirm. The user may remove/correct versions or add new ones.

## Step 2: Collect PRs from Milestones and Branches

For each upcoming version, collect merged PRs using two strategies:

### Strategy A: Milestone Search

```
milestone:<version> is:merged repo:dotnet/runtime
```

Also check the next milestone (e.g., `10.0.5`) and the catch-all milestone (e.g., `10.0.x`) since PRs already merged into the release branch may ship in the upcoming release even if milestoned for a later release.

### Strategy B: Branch + Date Search

```
base:release/<version>-staging is:merged merged:>YYYY-MM-DD  (for 8.0 and 9.0)
base:release/<version>       is:merged merged:>YYYY-MM-DD  (for 10.0+)
```

Exclude dependency update bots (`-author:dotnet-maestro[bot]`, `-label:dependencies`).

### Deduplication

Merge results from both strategies, deduplicating by PR number. Record for each PR:
- PR number and title
- Target branch and milestone (if any)
- Author and area labels
- Servicing label state: `approved`, `consider`, `rejected`, or `none`

The servicing state is informational (does not filter PRs out of scope). It indicates approval status: `Servicing-approved`, `Servicing-consider`, or `Servicing-rejected`.

## Step 3: Trace PR Lineage

For each collected servicing PR, trace back through the chain: **Issue(s) → Main PR → Servicing PR(s)**. See [references/lineage-tracing.md](references/lineage-tracing.md) for the detailed extraction patterns.

In summary:
1. **Find the main PR**: Check for "Backport of #NNNN", `**Main PR**: #NNNN`, title suffix `(#NNNN)`, or branch name `backport/pr-NNNN-to-release/X.0`. If none found, flag as `direct_to_release`.
2. **Find the issue(s)**: Check both servicing PR and main PR bodies for GitHub auto-close keywords (`fixes #NNNN`), issue URLs, and metadata fields (`Reported in`, `**Related issue**`).
3. **Handle unknowns**: If no issue found, flag `issue_unknown = true` so it can be highlighted for the user to resolve.

## Step 4: Classify Each PR

For each PR, fetch changed files via the GitHub API (`get_files`) and classify. See [references/pr-classification.md](references/pr-classification.md) for detailed file path rules and lead resolution logic.

| Classification | Criteria | Include? |
|---------------|----------|----------|
| **product** | At least one changed file is product source | ✅ Yes |
| **test-only** | Only test files changed | ❌ No |
| **infra-only** | Only infrastructure files changed | ❌ No |
| **dep-version-bump** | Only dependency version files changed | ❌ No |

For each product PR, determine:
- **Component**: Libraries, CoreCLR, Mono, Host, or Mixed
- **Lead**: Resolved from `os-` → `arch-` → `area-` labels using [docs/area-owners.md](../../docs/area-owners.md)

## Step 5: Group by Fix Lineage

Group servicing PRs into **fix groups** where each group represents a single logical fix applied to one or more release branches.

1. **By main PR**: All servicing PRs that reference the same main PR belong to the same fix group.
2. **By issue**: If two servicing PRs reference the same issue but different main PRs, group them together.
3. **Direct-to-release PRs**: Form their own fix group unless they share an issue with another group.

Each fix group contains: issue(s), main PR, servicing PRs (version → PR number), component, area, lead, fix description, and flags for `issue_unknown` and `direct_to_release`.

## Step 6: Present Results to the User

Display the results organized by fix lineage.

### Product-Source Fixes (In Scope for Validation)

Present each fix group as a row. Annotate servicing PR cells with servicing label state when not `approved`:

```
| Issue | Fix Description | Lead | Component | Area | Main PR | 8.0 | 9.0 | 10.0 |
|-------|----------------|------|-----------|------|---------|-----|-----|------|
| #123586 | Fix Vector2/3 EqualsAny | @jeffhandley | Libraries | System.Numerics | #123594 | - | - | #124223 |
| #123422 | Fix IEnumerable<T> binding | @karelz | Libraries | Extensions.Configuration | #123663 | - | - | #123720 |
| ⚠️ ? | Fix EH profiler notifications | @agocke | CoreCLR | ExceptionHandling | - | - | - | #123564 ⚑ |
| #125000 | Fix timeout in HttpClient | @karelz | Libraries | Networking | #124900 | - | - | #125100 🔵 |
```

**Legend:** ⚑ Direct to release · ⚠️ ? Issue unknown · 🔵 Servicing-consider · 🔴 Servicing-rejected

### Fixes with Unknown Issues

Highlight prominently and explain how to resolve:

> Add a `Fixes #NNNN` reference to one of the involved PRs, then say "reload #PRNUM" to re-fetch.

### Excluded PRs (No Product Source Changes)

```
| PR | Version | Reason Excluded | Title |
|----|---------|-----------------|-------|
| #122570 | 9.0 | test-only | Fix ICustomQueryInterface test |
```

## Step 7: Interactive Curation

After presenting the tables, use `ask_user` to let the user curate the list:

1. **Looks good — proceed with this list**
2. **Remove PRs from scope** — ask which PR numbers to remove
3. **Add excluded PRs back** — show excluded list, ask which to add
4. **Add other PRs** — ask for PR numbers (any dotnet/runtime PR: open, merged, any branch)
5. **Reload PR data** — re-fetch PRs to pick up updated issue references
6. **Re-scan** — re-run collection with adjusted versions or date range

After each adjustment, re-display the updated table. Continue until the user confirms.

## Step 8: Confirm Final List

Present the final curated list with a summary:

```
Final servicing validation scope:
- N Libraries fixes across M versions
- N CoreCLR fixes
- N Mono fixes
- N total PRs

Proceed to generate validation tests?
```

Wait for explicit user confirmation before proceeding.

## Step 9: Load Team-Specific Validation Workflow

After the user confirms, load the appropriate team reference file. If the user specified a team in their original prompt, load it directly. Otherwise, ask which team's workflow to use.

The team reference (e.g., [references/team-libraries.md](references/team-libraries.md)) defines which components/areas are relevant, how to generate validation tests, and team-specific conventions.

## SQL Tracking

Use the session SQL database to track collected PRs and their lineage. See [references/sql-tracking.md](references/sql-tracking.md) for the full schema and key queries.

## Important Notes

- **Rate limiting**: GitHub search API has secondary rate limits. If you hit a 403, wait 60 seconds and retry.
- **Branch naming**: .NET 8.0 and 9.0 use `release/X.0-staging`. .NET 10.0+ uses `release/X.0` (no `-staging`).
- **Milestone closures**: Milestones may be closed before the release ships — check the downloads page for the actual latest version.
- **Bot PRs**: Filter out `dotnet-maestro[bot]` and `dependabot[bot]`. Keep `github-actions[bot]` backport PRs (they contain real code changes).
- **Version predictions**: Reliable for +1 month, reasonable for +2, unreliable beyond. Warn the user accordingly.
- **End-of-support**: Check downloads page for end-of-support dates. Do not include versions past their EOL.
- **GA releases**: A major version's first servicing (X.0.1) ships the month after GA. The GA release itself (X.0.0) has its own schedule.

## References

- **[references/lineage-tracing.md](references/lineage-tracing.md)** — Detailed patterns for extracting main PR and issue references from PR bodies
- **[references/pr-classification.md](references/pr-classification.md)** — File path classification rules, component mapping, and lead resolution from area-owners.md
- **[references/sql-tracking.md](references/sql-tracking.md)** — SQL table schemas and key queries for session tracking
- **[references/team-libraries.md](references/team-libraries.md)** — Libraries team validation workflow
