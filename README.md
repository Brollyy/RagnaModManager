# RagnaModManager

Cross-platform Ragnarock mod manager built with .NET 9 and Avalonia.

The desktop app helps you locate Ragnarock, install `.rmod` packages, manage
setups, deploy mods safely, configure RE-UE4SS, and launch the game.

## Install and set up on Windows

1. Download the Windows x64 build from the [RMM releases page](https://github.com/Brollyy/RagnaModManager/releases/tag/v0.2.1). This alpha release is distributed as a ZIP file.
2. Right-click the ZIP, choose **Extract All**, open the extracted folder, and run `RagnaModManager.exe`.
3. On the **HOME** tab, click **Set up automatically**. If the game is not found, open **SETTINGS** → **Game folder** and click **Find Automatically**. You can also click **Choose Folder**, select the Ragnarock installation folder, and click **Save Folder**.
4. Open **DISCOVER**. Check the box beside each mod you want, then click the **Install** button above the list. Selecting mods this way also includes their required catalog dependencies.
5. Newly installed mods are enabled in the active setup. When the bottom banner says there are unapplied changes, click **Apply changes** to deploy the setup.
6. If RMM says UE4SS is needed, choose **Install UE4SS** and follow the prompts. If RMM asks to configure Steam launch options, allow the change so UE4SS can load when you start the game through Steam.
7. Click **Launch Ragnarock**.

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
dotnet run --project src/ui/RagnaModManager.Cli -- inspect ExampleMod.rmod
dotnet run --project src/ui/RagnaModManager.Cli -- import ExampleMod.rmod
dotnet run --project src/ui/RagnaModManager.Cli -- profile
dotnet run --project src/ui/RagnaModManager.Cli -- profile create development "Development"
dotnet run --project src/ui/RagnaModManager.Cli -- profile switch development
dotnet run --project src/ui/RagnaModManager.Cli -- enable example-mod --priority 500
dotnet run --project src/ui/RagnaModManager.Cli -- version example-mod 1.0.0
dotnet run --project src/ui/RagnaModManager.Cli -- catalog list
dotnet run --project src/ui/RagnaModManager.Cli -- catalog install example-mod
dotnet run --project src/ui/RagnaModManager.Cli -- catalog update
dotnet run --project src/ui/RagnaModManager.Cli -- profile export setup.json
dotnet run --project src/ui/RagnaModManager.Cli -- profile import setup.json imported "Imported setup"
dotnet run --project src/ui/RagnaModManager.Cli -- revert
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

## Community mod catalog

The desktop app includes a Community Mod Library backed by the separate
[RagnaModManager-ModRegistry repository](https://github.com/Brollyy/RagnaModManager-ModRegistry).
That repository contains the catalog format, package rules, submission process,
and maintainer guidance.

Manual imports remain available for packages outside the community registry and
should be treated as untrusted.

## License

RagnaModManager is licensed under the MIT License. See [LICENSE](LICENSE).
