# RagnaModManager

Cross-platform Ragnarock mod manager built with .NET 9 and Avalonia.

The desktop app helps you locate Ragnarock, install `.rmod` packages, manage
playsets, deploy mods safely, configure RE-UE4SS, and launch the game.

## Build

```sh
DOTNET_CLI_HOME="$PWD/.dotnet_home" DOTNET_CLI_USE_MSBUILD_SERVER=0 MSBUILDDISABLENODEREUSE=1 dotnet build RagnaModManager.sln -m:1
```

## Test

```sh
DOTNET_CLI_HOME="$PWD/.dotnet_home" DOTNET_CLI_USE_MSBUILD_SERVER=0 MSBUILDDISABLENODEREUSE=1 dotnet build tests/RagnaModManager.Tests/RagnaModManager.Tests.csproj -m:1
DOTNET_CLI_HOME="$PWD/.dotnet_home" dotnet tests/RagnaModManager.Tests/bin/Debug/net9.0/RagnaModManager.Tests.dll
```

## Run the desktop app

```sh
DOTNET_CLI_HOME="$PWD/.dotnet_home" DOTNET_CLI_USE_MSBUILD_SERVER=0 MSBUILDDISABLENODEREUSE=1 dotnet run --project src/ui/RagnaModManager.Desktop
```

Set `RMM_DATA_DIR` to override the platform app-data directory during
development.

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
dotnet run --project src/ui/RagnaModManager.Cli -- launch
```

## Publish a user build

```sh
dotnet publish src/ui/RagnaModManager.Desktop/RagnaModManager.Desktop.csproj -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true -o artifacts/RagnaModManager-linux-x64
dotnet publish src/ui/RagnaModManager.Desktop/RagnaModManager.Desktop.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o artifacts/RagnaModManager-win-x64
```

Zip the matching `artifacts/RagnaModManager-*` folder for distribution. Users
can run `RagnaModManager` on Linux or `RagnaModManager.exe` on Windows.

## Official mod registry

The desktop app includes an Official Mod Library backed by the separate
[`Brollyy/RagnaModManager-ModRegistry`](https://github.com/Brollyy/RagnaModManager-ModRegistry)
repository. It can browse releases, download packages over HTTPS, verify their
SHA-256 checksums, cache them, and install updates.

For development or testing, set `RMM_OFFICIAL_REGISTRY_URL` to another HTTPS
catalog. Manual imports remain available for packages outside the official
registry and should be treated as untrusted.

The catalog uses schema version `1`:

```json
{
  "schemaVersion": "1",
  "repository": "official",
  "mods": [
    {
      "id": "better-hit-feedback",
      "name": "Better Hit Feedback",
      "author": "Author",
      "description": "Improves hit feedback.",
      "releases": [
        {
          "version": "1.0.0",
          "packageUrl": "https://example.com/better-hit-feedback-1.0.0.rmod",
          "sha256": "64 hexadecimal characters"
        }
      ]
    }
  ]
}
```

See the [registry README](https://github.com/Brollyy/RagnaModManager-ModRegistry#readme)
for package structure, submission rules, maintainer review, and Ragnarock mod
development resources.
