# Test Execution

Run repro apps against available .NET SDK versions to establish baseline failures and confirm fixes.

## Step 1: Discover Installed SDKs

Run `dotnet --list-sdks` to discover all installed .NET SDKs. Parse the output to build a map of available versions:

```
10.0.100    [C:\Program Files\dotnet\sdk]
10.0.101    [C:\Program Files\dotnet\sdk]
9.0.200     [C:\Program Files\dotnet\sdk]
8.0.404     [C:\Program Files\dotnet\sdk]
```

Map SDK versions to runtime versions (the SDK `X.0.1xx` series ships with the `X.0.N` runtime where N corresponds to the patch level). The exact runtime version can be confirmed with `dotnet --list-runtimes`.

Also run `dotnet --list-runtimes` to get the precise runtime versions installed:

```
Microsoft.NETCore.App 8.0.24 [C:\Program Files\dotnet\shared\Microsoft.NETCore.App]
Microsoft.NETCore.App 9.0.13 [C:\Program Files\dotnet\shared\Microsoft.NETCore.App]
Microsoft.NETCore.App 10.0.3 [C:\Program Files\dotnet\shared\Microsoft.NETCore.App]
```

## Step 2: Determine Which Runs Are Possible

For each repro's RESULTS file, compare the needed versions (baseline + validation) against installed runtimes:

- **Baseline versions** (e.g., 10.0.3): The last shipped release *before* the fix. These are publicly available and should be installable.
- **Validation versions** (e.g., 10.0.4): The servicing release *with* the fix. These may not yet be published.

Classify each needed version:
- **Available**: Runtime is already installed → can run immediately
- **Installable**: Runtime is publicly released but not installed → can be installed
- **Unreleased**: Runtime has not yet been published → user must install manually (e.g., from a private build)

## Step 3: Inform the User

Present the execution plan:

```
SDK/Runtime availability for repro testing:

  Baseline tests (expect failure):
    ✅ .NET 10.0.3 — installed, ready to run
    ⬇️  .NET 9.0.13 — not installed, can be installed automatically
    ⬇️  .NET 8.0.24 — not installed, can be installed automatically

  Validation tests (expect pass):
    ❌ .NET 10.0.4 — not yet released, manual install required
    ❌ .NET 9.0.14 — not yet released, manual install required
    ❌ .NET 8.0.25 — not yet released, manual install required

I'll run the baseline tests now using available versions.
```

Explain which tests will be run and which are blocked. If some baseline versions are missing but installable, note that.

## Step 4: Run Available Tests

For each repro app and each available version, run the repro:

```powershell
dotnet run --framework net10.0 -- repro_123586.cs
```

Or for a standalone .cs file without a project, create a temporary project or use `dotnet-script` if available. The simplest approach:

```powershell
# Create a temp directory, copy the repro, create a minimal csproj, and run
$tempDir = New-TemporaryFile | ForEach-Object { Remove-Item $_; New-Item -ItemType Directory -Path $_ }
Copy-Item repro_123586.cs $tempDir/Program.cs

# Create minimal csproj targeting the specific framework
@"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
</Project>
"@ | Set-Content $tempDir/repro.csproj

# Run with the specific SDK version using global.json
@{ sdk = @{ version = "10.0.100"; rollForward = "latestPatch" } } | ConvertTo-Json | Set-Content $tempDir/global.json

dotnet run --project $tempDir/repro.csproj
$exitCode = $LASTEXITCODE
```

To target a specific runtime version, use a `global.json` with the appropriate SDK version or use `--fx-version` with `dotnet exec`.

Record the exit code:
- **Exit code 0**: Fix is working (pass)
- **Non-zero exit code**: Bug is reproduced (fail)

## Step 5: Record Results

After each run, update the RESULTS file:

```markdown
- [x] .NET 10.0.3 — ❌ FAIL (exit code 1) — bug reproduced as expected ✓
```

For baseline tests, a **FAIL is the expected outcome** (the bug should reproduce on the unfixed version). Flag unexpected results:

```markdown
- [x] .NET 10.0.3 — ✅ PASS (exit code 0) — ⚠️ UNEXPECTED: bug did not reproduce on baseline
```

For validation tests, a **PASS is the expected outcome** (the fix should resolve the bug). Flag unexpected results:

```markdown
- [x] .NET 10.0.4 — ❌ FAIL (exit code 1) — ⚠️ UNEXPECTED: fix did not resolve the issue
```

## Step 6: Handle Missing Versions

After running all available tests, check if any versions are still needed.

### Installable versions (publicly released)

Check if `dotnet-install.ps1` is available:

```powershell
Get-Command dotnet-install.ps1 -ErrorAction SilentlyContinue
```

If available, prompt the user:

> The following .NET versions are needed but not installed:
> - .NET 9.0.13
> - .NET 8.0.24
>
> These are publicly released and can be installed automatically using `dotnet-install.ps1`.

Offer choices:
1. **Install the next needed version** — install one at a time
2. **Install all needed versions** — install all missing publicly-released versions
3. **Skip** — skip these tests

To install a specific version:

```powershell
dotnet-install.ps1 -Channel 9.0 -Version 9.0.13 -Runtime dotnet
```

After installation, re-run the repro apps against the newly installed versions.

### Unreleased versions

For versions that have not yet been published (typically the servicing versions being validated):

> The following .NET versions are not yet publicly released:
> - .NET 10.0.4
>
> To run validation tests, you'll need to install the SDK manually from a private build.
> Once installed, say "rerun" and I'll execute the remaining tests.

These cannot be installed via `dotnet-install.ps1`. The user must obtain the SDK from internal build artifacts or a pre-release feed.

## Step 7: Present Summary

After all possible runs are complete, present a summary:

```
Test execution summary:

  repro_123586 (Vector2/3 EqualsAny):
    Baseline:   ❌ .NET 10.0.3 — FAIL (expected) ✓
    Validation: ⏳ .NET 10.0.4 — awaiting SDK install

  repro_123422 (IEnumerable<T> binding):
    Baseline:   ❌ .NET 10.0.3 — FAIL (expected) ✓
    Validation: ⏳ .NET 10.0.4 — awaiting SDK install

Overall: 2/2 baselines confirmed, 0/2 validations complete
```

If all tests pass as expected (baselines fail, validations pass), report success. If any results are unexpected, highlight them for investigation.
