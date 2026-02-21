# PR Lineage Tracing

Trace each servicing PR back through the chain: **Issue(s) → Main PR → Servicing PR(s)**.

## Extract the Main PR Reference

Most servicing PRs are backports from `main`. Extract the main PR number from these sources (in priority order):

1. **PR body first line**: `Backport of #NNNN to release/X.0` (automated backport bot pattern)
2. **PR body metadata**: `**Main PR**: #NNNN` (manual backport pattern)
3. **PR title**: `(#NNNN)` suffix in the title sometimes references the cherry-picked commit's original PR
4. **Branch name**: `backport/pr-NNNN-to-release/X.0` in the head ref

If none of these yield a main PR reference, the fix went **directly to the release branch** without a corresponding main branch PR. Flag this with `direct_to_release = true`.

## Extract the Issue Reference

Once you have the main PR number (or the servicing PR itself for direct fixes), find the issue(s) being fixed by checking these sources **in order**:

### From the servicing PR body:

1. `Reported in https://github.com/dotnet/runtime/issues/NNNN`
2. `**Related issue**: #NNNN`
3. `Fixes https://github.com/dotnet/runtime/issues/NNNN`
4. Any `https://github.com/dotnet/runtime/issues/NNNN` URL in the body

### From the main PR (fetch it via GitHub API `get` method):

1. `fix #NNNN` / `fixes #NNNN` / `fixed #NNNN` / `close #NNNN` / `closes #NNNN` / `resolve #NNNN` / `resolves #NNNN` (GitHub auto-close keywords)
2. `fix https://github.com/dotnet/runtime/issues/NNNN`
3. `Fixes https://github.com/dotnet/runtime/issues/NNNN`
4. Any `https://github.com/dotnet/runtime/issues/NNNN` URL in the body

**Important**: Distinguish issue URLs (`/issues/NNNN`) from PR URLs (`/pull/NNNN`). Only collect issue numbers, not PR cross-references.

## Handle Unknown Issues

If no issue reference is found after checking both the servicing PR and the main PR, mark the fix with `issue_unknown = true`. These will be highlighted in the output so the user can:

1. Manually provide the issue number
2. Add the issue reference to one of the involved PRs, then ask the skill to reload that PR's data

## Handle Direct-to-Release Fixes

In rare cases, a fix goes directly into the release branch without a main PR. This happens when:

- The issue no longer reproduces on `main` because it was fixed by different work
- The fix is specific to the release branch's version of the code
- The fix was made directly by a maintainer without the backport workflow

For these PRs, extract the issue reference from the servicing PR body itself using the same patterns above.
