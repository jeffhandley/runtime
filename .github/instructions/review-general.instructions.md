---
applyTo: "src/**"
---

# Code Review -- General Guidance (all source areas)

Cross-cutting review criteria for any change under `src/`. Language- and area-specific
rules live in sibling instruction files (`review-csharp`, `review-native`,
`review-tests`) and area files (`jit`, `system-net-*`, `extensions-*`, `compression`,
`cdac`). Apply every file whose `applyTo` matches a changed file; more specific files win
on conflict.

**Reviewer mindset:** Be polite but very skeptical. Treat the PR description and linked
issues as claims to verify, not facts to accept. Question the direction and the value of
the change itself, not just its implementation.

## Holistic PR Assessment

Evaluate the PR as a whole before individual lines, and produce a verdict on its
motivation, approach, and net value.

### Motivation & justification
- Every PR must articulate what problem it solves and why. Don't accept vague or absent motivation.
- Challenge every addition with "Do we need this?" New code, APIs, abstractions, and flags must justify their existence.
- Demand real-world use cases and customer scenarios. Hypothetical benefits don't justify new API surface.

### Evidence & data
- Require measurable data (BenchmarkDotNet or equivalent) before accepting optimization PRs; never accept perf claims at face value.
- Distinguish real wins from micro-benchmark noise; require realistic, varied inputs.
- Investigate and explain regressions before merging, even when the net result is positive.

### Approach & alternatives
- Check whether the PR solves the right problem at the right layer -- root cause vs. band-aid.
- When the approach is fundamentally wrong, redirect early; don't iterate on the details of a flawed design.
- Always ask "Why not just X?" -- the burden of proof is on the more complex solution.

### Cost-benefit & complexity
- Weigh whether the change is a net positive in the typical configuration, not just a narrow scenario.
- Reject overengineering -- complexity is a first-class cost.
- Every addition is a maintenance obligation; increased surface area needs stronger justification.

### Scope & focus
- Require large or mixed PRs to be split into focused, single-concern changes.
- Defer tangential improvements to follow-up PRs; police scope creep.

### Risk & compatibility
- Flag breaking changes and require the formal process (docs, API review, approval) even for internal-only improvements.
- Assess regression risk proportional to the change's blast radius.

### Codebase fit & history
- Ensure new code matches existing patterns and conventions.
- Check whether a similar approach was tried and rejected before; require a clear explanation of what's different.

## Correctness Philosophy (cross-cutting)
- **Fix root cause, not symptoms.** Investigate and fix the underlying cause rather than adding workarounds or suppressing warnings. Revert broken commits before layering fixes.
- **Challenge exception swallowing that masks unexpected errors.** Question `catch { continue; }` / `catch { return null; }` -- let unexpected exceptions propagate or fail fast so the real issue gets investigated.
- **Delete dead code and unnecessary wrappers** when the only caller changes.
- **Security is secure-by-default.** Opt-out must be explicit and documented. Concrete mechanics (overflow guarding, secret hygiene, credential handling, buffer bounds) live in the language files.

## PR Hygiene & Consistency
- Keep PRs focused on their stated scope -- no accidental edits, unrelated refactoring, whitespace noise, or build artifacts.
- Do large refactorings and renames in separate PRs from functional changes.
- Merge to main first, then backport via `/backport`; servicing backports are limited to security, regressions, and reliability.
- Extract duplicated logic into shared helpers; move shared code to shared files rather than duplicating across runtimes.
- Use existing APIs/types/helpers instead of creating parallel ones.
- Store error strings in `.resx` referenced via `SR`; delete unused entries when removing code.
- Maintain alphabetical order in lists (areas, resx entries, export lists, ref members).
- Don't hand-edit auto-generated files or `eng/common` (synced from arcade) -- change the generator/source instead.
- Match the existing style of a modified file; don't change code for style alone.

## Documentation & Comments
- Comments explain **why**, not restate code; delete comments that duplicate the code in English.
- Delete or update obsolete comments when code changes.
- Track deferred work with GitHub issues and searchable TODO prefixes; remove ancient TODOs.
- Put doc comments on interface definitions, not duplicated on implementations.
- Add XML doc comments on all new public APIs (not on test code); properties start with "Gets the ..." / "Gets or sets the ...".
- Use commit/SHA-based links in docs, not branch-relative links that break when files move.
- Retain copyright/license headers in all source files, including tests.
- File breaking-change documentation for behavioral changes.
