---
applyTo: "**/*.cs"
---

# Code Review -- C# (managed code)

Rules for reviewing C# changes across `src/`. Also apply `review-general` (all changes),
`review-tests` (test files), and any matching area file (`jit`, `system-net-*`,
`extensions-*`, `compression`, `cdac`).

## Error Handling & Assertions
- Use `Debug.Assert` for internal invariants, not exceptions; prefer `Debug.Assert(x is not null)` over the null-forgiving `!`.
- Use `throw` for reachable error paths; `throw new UnreachableException()` for exhaustive switch defaults; `PlatformNotSupportedException` (not `NotSupportedException`) for platform gaps.
- Include actionable detail in exception messages; use `nameof`; never throw empty exceptions.
- Initialize all `out` parameters in every code path, including error paths.
- Handle OOM with exceptions or fail-fast, never asserts.
- Prefer `ThrowIf` helpers (`ArgumentOutOfRangeException.ThrowIfNegative`, `ObjectDisposedException.ThrowIf`, ...) over manual if-then-throw.

## Thread Safety
- Fields shared across threads need `Volatile`/`Interlocked`; `??=` is not thread-safe; `Nullable<T>` is not safe for caching (two-field struct tears).
- Use `Environment.TickCount64` (not `TickCount`) for timeout math.

## Security (C# mechanics)
- Guard multiplication in size computations against integer overflow; prefer correct-by-construction patterns.
- Clear key material with `CryptographicOperations.ZeroMemory`; use non-short-circuit `|` in verification code to avoid timing leaks.
- Don't send credentials (especially Basic auth) before receiving a challenge.
- Limit `stackalloc` to ~1KB and validate size; never stackalloc on user-controlled/large sizes; place it just before use, not before early returns.

## Correctness Patterns
- Prefer safe code over `Unsafe.As`/`Unsafe.AsRef`/raw pointers without a demonstrated need; prefer Span-based APIs; prefer `Unsafe.BitCast` over `Unsafe.As` for same-size punning.
- Check `SafeHandle.IsInvalid` (not null); capture the exception before calling `Dispose`.
- Seal classes whose `Equals` uses `GetType()` exact-type matching.
- Use `Environment.ProcessPath` and `AppContext.BaseDirectory` (NativeAOT/single-file safe), not `Process...MainModule` / `Assembly.Location`.
- File name casing must match csproj references exactly; list new files in the csproj when sibling files are listed.
- Prefer correct-by-construction designs over manually maintained parallel data structures.
- Backport small targeted fixes, not refactorings, to servicing branches.
- Consider NativeAOT parity when changing CoreCLR behavior.
- Source generators must be incremental (no `ISymbol`/`Compilation` in pipeline state; deterministic, Ordinal-sorted output); diagnostics come from a separate analyzer.

## Performance & Allocations
- Performance changes require benchmark evidence; justify binary-size increases with real-world measurements.
- Avoid premature object pools/caches without evidence they are needed.
- Avoid closures/allocations in hot paths (use a static delegate + state parameter); avoid string concatenation (use span-based operations).
- Pre-size `Dictionary`/`HashSet`/`List` when the expected count is known.
- Structs used as dictionary keys need `IEquatable<T>` + `GetHashCode` to avoid boxing.
- Avoid the Pinned Object Heap for non-permanent objects; suppress `ExecutionContext` flow for infrastructure timers.
- Order conditionals cheapest/most-common first; allocate expensive resources lazily.
- Extract throwing logic into `[DoesNotReturn]` helpers so the JIT can inline the success path.
- Avoid O(n^2) patterns; cache repeated accessor/getter calls in locals.
- Cache AppContext switches in `static bool Prop { get; } = AppContext.TryGetSwitch(...)`.
- Don't cache `typeof(...)` or store `ArrayPool.Shared` in a variable (both are de-optimizations).
- Use `CollectionsMarshal.GetValueRefOrAddDefault`/`GetValueRefOrNullRef` for large value-type dictionary lookups; `ValueListBuilder<T>` on hot paths.
- Use `sizeof` (not `Marshal.SizeOf`) for blittable structs; use the `(uint)index >= (uint)length` bounds-check idiom; slice spans before iterating.
- Avoid LINQ and records in low-level compiler codebases (CG2/ILC/AOT); use direct loops and readonly structs.

## API Design & Contracts
- New public APIs require an approved proposal before PR; use `internal` for APIs pending review. (The skill runs the blocking api-approval check when new public surface is detected.)
- Parameter names must match between `ref` and `src` (renames are source-breaking).
- Align exception types and validation order across platforms: `ArgumentNullException` -> `ArgumentException` -> `PNSE` -> `ObjectDisposedException` -> operation.
- `Try` APIs return `false` only for the common expected failure; throw for everything else, and always throw on invalid arguments.
- Don't expose a mutable options object after construction; don't leak private field/internal type names in user-facing messages.
- Follow the obsoletion process (next SYSLIB id, `[Obsolete]`, `[EditorBrowsable(Never)]` + `[OverloadResolutionPriority(-1)]`).
- Prefer `int`/`long` for length parameters in public APIs; named types over `ValueTuple` across file boundaries.
- New virtual methods must behave identically to the pre-existing equivalent when not overridden.

## Code Style & Formatting
- Well-named constants over magic numbers; PascalCase for constants; positive, descriptive boolean names (`_hasCurrent`, not `valid`).
- `var` only when the type is obvious from context; never for numeric types; explicit types for casts and method returns.
- Name methods for behavior (`Get*` implies a return value; `ThrowIf`, not `ThrowExceptionIf`).
- Prefer early return to reduce nesting (error case first, success return last).
- Avoid `using static` and `#region` in new code.
- Local functions at the end of the method; fields declared first in a type.
- Narrow warning suppressions to the smallest scope.
- Prefer pattern matching (`is`/`and`/`or`) and named arguments for booleans.
- Don't initialize managed fields to default values (CA1805).
- Sealed classes need only a simple `Dispose()`.
- Prefer `BinaryPrimitives` (`ReadInt32LittleEndian`/`BigEndian`) for endianness-safe reads; prefer cross-platform `Vector128/256/512` and `BitOperations` over ISA-specific intrinsics.
