# Custom hammer packages

RMM stores imported custom hammer packages in its `hammer-library` data folder. A package is a ZIP archive with the `.rhammer` extension.

## Package contents

```text
hammer.json
row.json
Assets/
  Meshes/Custom/SM_Example.uasset
  Meshes/Custom/SM_Example.uexp
  Data/DA_RMM_EXAMPLE_HAMMER.uasset
  Data/DA_RMM_EXAMPLE_HAMMER.uexp
```

`hammer.json` uses this format:

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
  "meshAssetPath": "/Game/Meshes/Custom/SM_Example",
  "dataAssetPath": "/Game/Data/Hammers/DA_RMM_EXAMPLE_HAMMER",
  "assets": [
    { "source": "Assets/Meshes/Custom/SM_Example.uasset", "packagePath": "/Game/Meshes/Custom/SM_Example" },
    { "source": "Assets/Meshes/Custom/SM_Example.uexp", "packagePath": "/Game/Meshes/Custom/SM_Example" },
    { "source": "Assets/Data/DA_RMM_EXAMPLE_HAMMER.uasset", "packagePath": "/Game/Data/Hammers/DA_RMM_EXAMPLE_HAMMER" },
    { "source": "Assets/Data/DA_RMM_EXAMPLE_HAMMER.uexp", "packagePath": "/Game/Data/Hammers/DA_RMM_EXAMPLE_HAMMER" }
  ],
  "rowData": "row.json"
}
```

Every package needs a unique lowercase package ID. RMM derives the stable row name as `RMM_` plus the uppercase ID with hyphens changed to underscores; it derives the data asset path under `/Game/Data/Hammers/DA_<row name>`. Package paths must be unique across the library. The mesh and hammer data asset paths must refer to listed cooked assets. The data asset must inherit the game's `DA_Hammers` class and reference the declared mesh. `rowData`, when present, is reserved for future package metadata.

RMM reads the installed `Ragnarock-WindowsNoEditor.pak` using a local AES key candidate file selected by the user. The key file path is stored locally; key values are never copied into the repository, generated package, or logs. RMM merges the installed `DT_Hammers` table at build time, keeps all stock and DLC rows, adds the enabled hammer rows, then validates the result and builds a PAK V11 overlay. Unsupported PAK versions or DataTable schemas stop the build safely.

The game owns Unreal cooked asset files. RMM packages and stores user-provided assets only; this repository does not include game assets, a cooked sample hammer, or a game table dump.
