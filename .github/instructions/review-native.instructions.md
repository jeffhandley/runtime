---
applyTo: "**/*.c,**/*.cc,**/*.cpp,**/*.cxx,**/*.h,**/*.hpp,**/*.inc,**/*.S,**/*.asm"
---

# Code Review -- Native code (C/C++/asm) & interop

Rules for reviewing native runtime code (CoreCLR VM, JIT, `src/native`, Mono native). Also
apply `review-general`. For JIT specifics see `jit`; for networking interop see
`system-net-interop`.

## JIT-Specific Correctness
- Never call `LowerNode` on an already-lowered node (no double-lowering); return newly created nodes for the caller to lower. Constant folding belongs in import/morph, not lowering.
- Use `JITDUMP`/`LOG` macros, never `printf`/`Console.WriteLine`, in production native code.

## C++ Style
- Don't use `auto` (except unspeakable types like lambdas); use explicit types.
- Prefer `nullptr` over `NULL`, `void*` over `LPVOID`, `WCHAR` over `wchar_t` in Windows host code; use the `.inc` suffix for multiply-included files.
- Match `#endif`/`#else` comments to the `#ifdef`; use consistent brace placement and four-space indentation.
- Prefer `static_cast` over C-style casts (which can silently degrade to `reinterpret_cast`).
- Order struct fields by size (pointers first) to minimize padding.

## Runtime & VM Patterns
- Use correct VM contracts: throwing QCalls need `BEGIN_QCALL`/`END_QCALL`; simple ones use `QCALL_CONTRACT_NO_GC_TRANSITION`; VM methods need `STANDARD_VM_CONTRACT`/`WRAPPER_NO_CONTRACT`.
- Keep GC protection correct: `GCPROTECT` all managed references before GC-triggering calls; refresh via `ObjectFromHandle(handle)` afterward.
- Avoid dynamic allocation on fatal-error paths; use stack buffers and Interlocked+spin-wait, not Monitor/lock.
- Avoid thread-local objects with destructors in CoreCLR (arbitrary destruction order); tie lifetime to the Thread object; prefer minipal `PLATFORM_THREAD_LOCAL` over C++ `thread_local` on perf-critical paths.
- Use `SET_UNALIGNED_32/64` macros for potentially unaligned writes in codegen stubs.
- Zero-initialize arrays/buffers that may be partially used or whose elements have destructors (EH tables, C arrays).
- Add static asserts for hardcoded structural offsets (especially when accessed from assembly).
- Use minipal (not legacy PAL) for new platform abstractions; use `ALTERNATE_ENTRY` (not `LOCAL_LABEL`) for asm labels called from outside their function.
- Handle OOM via `ThrowOutOfMemory`/`EEPOLICY_HANDLE_FATAL_ERROR`; in interpreter loops use `nothrow new` and null-check. Use `_ASSERTE(!"message")` for native asserts.

## Platform & Portability
- Use `TARGET_*`/`HOST_*` defines (not compiler defines like `__wasm__`); `HOST_*` for build-machine code, `TARGET_*` for the target platform; `PORTABILITY_ASSERT` for unimplemented platform code.
- New runtime environment variables use the `DOTNET_` prefix (not `COMPlus_`).
- Keep interpreter behavior consistent with the JIT (same patterns, `CORJIT_BADCODE`, `NO_WAY`, `FEATURE_INTERPRETER` guards).

## P/Invoke & Marshalling
- Prefer 4-byte `BOOL` (`UnmanagedType.Bool`) for interop; verify P/Invoke return types match the native signature exactly (mismatches may work on 64-bit but fail on 32-bit/WASM).

## Performance (native / interpreter)
- Separate hot data from rarely-used data (GCInfo, DebugInfo) in runtime structures.
- Compute constant data at compile time (interpreter metadata lookups/type checks during compilation, not execution).
- Prefer table-driven approaches over large `case` statements for intrinsics and pattern-heavy code.
