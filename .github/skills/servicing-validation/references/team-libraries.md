# Libraries Team — Servicing Validation Workflow

Team-specific guidance for validating libraries fixes in .NET servicing releases.

## Scope

The libraries team is responsible for validating fixes under:
- `src/libraries/*/src/**` — managed library source
- `src/libraries/*/gen/**` — source generators
- `src/libraries/Common/src/**` — shared source files

From the curated fix list, filter to fix groups where `component = 'Libraries'`. Present the libraries-scoped subset to the user for confirmation before generating tests.

CoreCLR, Mono, Host, and other component fixes are out of scope for the libraries team's validation and should be noted as deferred to other teams.

## Validation Approach

*(To be defined — this section will describe how the libraries team produces validation tests for servicing fixes, including test patterns, project structure, and execution.)*
