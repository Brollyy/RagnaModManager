# Custom hammer packages

The Hammer Library lets players collect custom Ragnarock hammer models in RagnaModManager, enable the ones they want, and add them to the game together. An `.rhammer` file is a ZIP archive with a different extension. It contains a `hammer.json` manifest, the hammer's cooked Unreal assets, and an optional preview image. The package is made by a hammer creator after cooking the assets for Ragnarock's Unreal Engine version; RMM does not cook source models.

## Archive layout

```text
hammer.json
Preview/hammer.png             # Optional PNG, JPG, or WebP shown in the library
Assets/Meshes/SM_Example.uasset
Assets/Meshes/SM_Example.uexp  # Include sidecar files produced by cooking
Assets/UI/T_ExampleHammer.uasset
Assets/UI/T_ExampleHammer.uexp  # If produced by the cooker
Assets/UI/MI_ExampleSilhouette.uasset
Assets/UI/MI_ExampleSilhouette.uexp  # If produced by the cooker
Assets/UI/MI_ExampleSymbol.uasset
Assets/UI/MI_ExampleSymbol.uexp     # If produced by the cooker
```

The manifest maps archive paths to Unreal package paths. Source paths are relative to the archive root; package paths start with `/Game/` and omit file extensions.

```json
{
  "formatVersion": 1,
  "id": "example-hammer",
  "name": "Example Hammer",
  "author": "Author",
  "version": "1.0.0",
  "description": "A custom hammer.",
  "rowName": "RMM_EXAMPLE_HAMMER",
  "displayName": "Example Hammer",
  "meshAssetPath": "/Game/Meshes/SM_Example",
  "iconAssetPath": "/Game/UI/Hammers/T_ExampleHammer",
  "silhouetteAssetPath": "/Game/UI/Hammers/MI_ExampleSilhouette",
  "symbolAssetPath": "/Game/UI/Hammers/MI_ExampleSymbol",
  "dataAssetPath": "/Game/Data/Hammers/DA_RMM_EXAMPLE_HAMMER",
  "thumbnail": "Preview/hammer.png",
  "assets": [
    { "source": "Assets/Meshes/SM_Example.uasset", "packagePath": "/Game/Meshes/SM_Example" },
    { "source": "Assets/Meshes/SM_Example.uexp", "packagePath": "/Game/Meshes/SM_Example" },
    { "source": "Assets/UI/T_ExampleHammer.uasset", "packagePath": "/Game/UI/Hammers/T_ExampleHammer" },
    { "source": "Assets/UI/T_ExampleHammer.uexp", "packagePath": "/Game/UI/Hammers/T_ExampleHammer" },
    { "source": "Assets/UI/MI_ExampleSilhouette.uasset", "packagePath": "/Game/UI/Hammers/MI_ExampleSilhouette" },
    { "source": "Assets/UI/MI_ExampleSilhouette.uexp", "packagePath": "/Game/UI/Hammers/MI_ExampleSilhouette" },
    { "source": "Assets/UI/MI_ExampleSymbol.uasset", "packagePath": "/Game/UI/Hammers/MI_ExampleSymbol" },
    { "source": "Assets/UI/MI_ExampleSymbol.uexp", "packagePath": "/Game/UI/Hammers/MI_ExampleSymbol" }
  ]
}
```

`formatVersion` identifies the package schema. `id` is a unique lowercase slug containing letters, numbers, and hyphens. RMM derives the game's stable DataTable row name from that ID, so creators should use the matching `RMM_...` value in `rowName`. `displayName` is shown in Ragnarock. `assets` lists each cooked file and its in-game package destination; all files for a package must be included, and destinations must be unique across the enabled collection. The declared mesh must contain a cooked StaticMesh. `iconAssetPath` names a cooked Texture2D included in `assets`; RMM uses it for the hammer's changing-room icon. `silhouetteAssetPath` and `symbolAssetPath` name cooked MaterialInstanceConstant assets included in `assets`; RMM assigns them to the generated hammer data asset's `Silhouette` and `Symbol` properties. Include each custom material's base material and texture dependencies as well. Existing packages may omit these two properties and retain the template visuals. The PNG/JPG/WebP `thumbnail` is a separate optional preview shown in the RMM library tile and is not used as an in-game icon. RMM creates the hammer data asset during PAK building from matching game-build metadata, then points it at the declared mesh and any declared custom presentation materials. Creators do not need to package a game-specific data asset.

RMM validates the archive paths, manifest, cooked assets, references, and duplicate destinations when importing. Keep the package small and include only the custom hammer's cooked files. Do not include Ragnarock's stock or DLC assets.

The generated game PAK must be merged with a matching `DT_Hammers` table for the installed game build. Metadata is keyed by the SHA-256 of Ragnarock's data PAK, so a game update with an unrecognized hash must be reported as unsupported until matching metadata is prepared. Never use a table from a different build, because it could hide stock or DLC hammers.

For the full UE 4.27 source-model-to-`.rhammer` workflow and the game's hammer data fields, see [Create a Ragnarock hammer package with UE 4.27](custom-hammer-authoring-ue4-27.md).
