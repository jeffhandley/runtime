---
applyTo: "src/tests/**,**/tests/**"
---

# Code Review -- Tests

Rules for reviewing test changes. Also apply `review-general` plus the language file for
the code under test.

- Always add regression tests for bug fixes and behavior changes; prefer new `[InlineData]` cases on existing test files over new files; list new files in the csproj.
- Use `[PlatformSpecific]`, `[ConditionalFact]`, or `[ActiveIssue]` for skip logic, not runtime if-checks (`ConditionalFact` is required for `SkipTestException` to work).
- Test edge cases, error paths, and all affected types (empty strings, negatives, boundaries, Turkish 'i', surrogate pairs); test both true and false for boolean options; choose inputs that can't accidentally pass if the output wasn't touched.
- Assert exact expected values (exact `OperationStatus`, exact byte counts), not broad conditions; ensure the test fails when the fix is reverted.
- Delete flaky/low-value tests rather than patching them; don't add known-flaky tests.
- Make test data deterministic and culture-independent (create `CultureInfo` with explicit format settings); prefer `[Theory]` + `[InlineData]` over many `[Fact]` methods.
- Use `PLACEHOLDER` for test passwords (avoids credential-scanning false positives).
- Use checked (not debug) CoreCLR builds for CI; new JIT regression tests are typically `CLRTestPriority 1`.
- Use `RemoteExecutor` for tests that touch process-wide shared state; avoid hardcoded paths (use temp files); don't add heavy dependencies (e.g. `Microsoft.CodeAnalysis.CSharp`) to test assemblies.
- Catch only expected exceptions in fuzz tests (catching all masks bugs like undocumented exceptions escaping the API).
- In xUnit projects use modern patterns: `Assert.*` (not the `return 100` convention), `[Fact]`/`[Theory]`, `ThrowsAnyAsync<OperationCanceledException>` for cancellation, and name regression classes after the issue (e.g. `Runtime_117605`). Legacy tests under `src/tests` may keep `return 100`.
- Mark collectible-ALC test methods `[MethodImpl(MethodImplOptions.NoInlining)]` so the JIT doesn't keep references alive.
- Reduce test output volume; prefer `Thread.Sleep` with fewer iterations over busy loops.
- In `src/tests/Regressions/coreclr/`, use `GitHub_<issue_number>` for the directory and `test<issue_number>` for the test name.
- Interop tests for compression must use files created by external tools, not just round-trips with the same implementation.
