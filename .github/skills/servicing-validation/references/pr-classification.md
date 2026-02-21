# PR Classification and Lead Resolution

## File Path Classification Rules

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

## Component Mapping

Based on the changed files and area labels, assign each product PR a component:

- **Libraries** — changes under `src/libraries/`
- **CoreCLR** — changes under `src/coreclr/`
- **Mono** — changes under `src/mono/`
- **Host** — changes under `src/native/corehost/` or `src/installer/`
- **Mixed** — changes spanning multiple components

## Lead Resolution

Using the PR's labels and the area ownership data in [docs/area-owners.md](../../../docs/area-owners.md), determine the **lead** responsible for each fix. Apply labels in this priority order (higher priority overrides lower):

1. **`os-` labels**: If the PR (or its main PR) has an `os-` label listed in the Operating Systems table (e.g., `os-android`, `os-browser`), use that table's Lead.
2. **`arch-` labels**: If the PR has an `arch-` label listed in the Architectures table (e.g., `arch-wasm`, `arch-riscv`), use that table's Lead.
3. **`area-` labels**: Use the Areas table's Lead for the PR's `area-` label. If the PR has multiple `area-` labels, prefer the one matching the most changed files.

If no label-to-lead mapping is found (e.g., the PR has no recognized labels, or the label has no lead assigned), record `lead` as `unknown` and note this in the output.

Record the lead as a GitHub username (e.g., `@karelz`) on each servicing PR and propagate it to the fix group.
