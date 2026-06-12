# RagnaModManager

Cross-platform, Ragnarock-specific mod manager prototype implemented in .NET 9.

The current implementation is a graphical desktop app built with Avalonia. GitHub releases can ship a self-contained `RagnaModManager` executable for Linux or `RagnaModManager.exe` for Windows that users can open directly.

## Build

```sh
DOTNET_CLI_HOME="$PWD/.dotnet_home" DOTNET_CLI_USE_MSBUILD_SERVER=0 MSBUILDDISABLENODEREUSE=1 dotnet build RagnaModManager.sln -m:1
```

## Test

```sh
DOTNET_CLI_HOME="$PWD/.dotnet_home" DOTNET_CLI_USE_MSBUILD_SERVER=0 MSBUILDDISABLENODEREUSE=1 dotnet build tests/RagnaModManager.Tests/RagnaModManager.Tests.csproj -m:1
DOTNET_CLI_HOME="$PWD/.dotnet_home" dotnet tests/RagnaModManager.Tests/bin/Debug/net9.0/RagnaModManager.Tests.dll
```

## Run Desktop App

```sh
DOTNET_CLI_HOME="$PWD/.dotnet_home" DOTNET_CLI_USE_MSBUILD_SERVER=0 MSBUILDDISABLENODEREUSE=1 dotnet run --project src/ui/RagnaModManager.Desktop
```

Set `RMM_DATA_DIR` to override the platform app-data directory during development.

## Developer CLI

```sh
dotnet run --project src/ui/RagnaModManager.Cli
dotnet run --project src/ui/RagnaModManager.Cli -- init
dotnet run --project src/ui/RagnaModManager.Cli -- detect
dotnet run --project src/ui/RagnaModManager.Cli -- set-game /path/to/Ragnarock
dotnet run --project src/ui/RagnaModManager.Cli -- inspect BetterHitFeedback.rmod
dotnet run --project src/ui/RagnaModManager.Cli -- import BetterHitFeedback.rmod
dotnet run --project src/ui/RagnaModManager.Cli -- profile
dotnet run --project src/ui/RagnaModManager.Cli -- profile create development "Development"
dotnet run --project src/ui/RagnaModManager.Cli -- profile switch development
dotnet run --project src/ui/RagnaModManager.Cli -- enable better-hit-feedback --priority 500
dotnet run --project src/ui/RagnaModManager.Cli -- preview
dotnet run --project src/ui/RagnaModManager.Cli -- deploy
dotnet run --project src/ui/RagnaModManager.Cli -- rollback
dotnet run --project src/ui/RagnaModManager.Cli -- reset-deployment
dotnet run --project src/ui/RagnaModManager.Cli -- compat
dotnet run --project src/ui/RagnaModManager.Cli -- launch-plan
dotnet run --project src/ui/RagnaModManager.Cli -- diagnostics-json
dotnet run --project src/ui/RagnaModManager.Cli -- open-data-folder
dotnet run --project src/ui/RagnaModManager.Cli -- open-game-folder
dotnet run --project src/ui/RagnaModManager.Cli -- launch
```

## Publish A User Build

```sh
dotnet publish src/ui/RagnaModManager.Desktop/RagnaModManager.Desktop.csproj -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true -o artifacts/RagnaModManager-linux-x64
dotnet publish src/ui/RagnaModManager.Desktop/RagnaModManager.Desktop.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o artifacts/RagnaModManager-win-x64
```

Zip the matching `artifacts/RagnaModManager-*` folder for a GitHub release. Users run `RagnaModManager` on Linux or `RagnaModManager.exe` on Windows and get the desktop UI immediately.

## Implemented V1 Scope

- Platform app-data directory creation
- SQLite-backed local database through native `libsqlite3`
- Ragnarock Steam path discovery, `libraryfolders.vdf` parsing, and manual game path validation
- Proton-aware launch planning that can use `steam://rungameid/1345820` for Linux Steam installs
- Compatibility diagnostics for game structure, Steam detection, pak folder, and UE4SS status
- Diagnostics JSON export for support/debugging
- Cross-platform open data folder and open game folder actions
- Strict `.rmod` zip import with zip-slip/path traversal rejection
- Read-only `.rmod` inspection that validates and previews manifest contents before install
- Manifest validation for `ue4ss-lua`, `ue4ss-dll`, `pak`, `config`, and `loose-file`
- Manifest `requires.manager` and `requires.ue4ss` version checks
- Local mod library installation
- Default profile with enable/disable and priority
- Multiple profile records with create/switch support
- `profiles/default.json` written alongside SQLite state
- Deployment preview with same-target blocking conflicts and advisory asset/hook conflicts
- Blocking deployment conflicts for manifest-declared incompatible mods
- Copy deployment with checksums, `deployment/current.json`, and SQLite deployed file records
- Cleanup of previous manager-owned files only when checksums still match
- Deployment backups under `deployment/backups/` and latest-backup rollback
- Deployment reset that removes current manager-owned files and clears deployed state
- Switching profiles followed by deploy removes previous manager-owned profile files before copying the active profile
- Modified deployed files block redeploy instead of being silently overwritten
- UE4SS detection, GitHub release download/update checks, cached version rollback, user-supplied zip installation, Lua/DLL deployment, and `Mods/mods.txt` writing
- Disabling UE4SS mods retains deployed files and writes `Mods/mods.txt` entries as disabled; reset deployment removes retained files
- Pak deployment to `Ragnarock/Content/Paks/~mods` with deterministic load-order names
- Publishable desktop UI with screens for setup, installed mods, import, deployment, settings, logs, and launch
- Developer CLI for scripting and diagnostics

## Not Yet Implemented

- Native package installers such as `.deb`, `.rpm`, `.msi`, or MSIX
- Online mod marketplace or updater
- Pak merging, virtual filesystem deployment, or multi-game support
