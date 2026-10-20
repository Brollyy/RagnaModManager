# Custom hammer package format

An `.rhammer` is a ZIP archive containing a `hammer.json` manifest, cooked Unreal Engine assets, and an optional preview image. RMM imports and installs finished packages; hammer authors cook the assets in UE 4.27.

## Archive contents

```text
hammer.json
Preview/hammer.png                       # Optional RMM library thumbnail
Assets/RMM/Hammers/<id>/Mesh/...         # Static Mesh and its cooked sidecars
Assets/RMM/Hammers/<id>/Materials/...    # Materials and referenced textures
Assets/RMM/Hammers/<id>/UI/...            # In-game icon Texture2D
Shaders/ShaderArchive-...-PCD3D_SM5.ushaderbytecode
```

Include every cooked `.uasset`, `.uexp`, `.ubulk`, and `.uptnl` file for the mesh, icon, materials, and their dependencies. Do not include source OBJ/FBX/PNG files as a substitute for cooked assets. RMM checks every manifest path and requires the shader archive to target Windows `PCD3D_SM5`.

## Manifest fields

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
  "meshAssetPath": "/Game/RMM/Hammers/example-hammer/Mesh/SM_Example",
  "iconAssetPath": "/Game/RMM/Hammers/example-hammer/UI/T_ExampleIcon",
  "silhouetteAssetPath": null,
  "symbolAssetPath": null,
  "shaderArchive": "Shaders/ShaderArchive-Project-PCD3D_SM5.ushaderbytecode",
  "dataAssetPath": "/Game/Data/Hammers/DA_RMM_EXAMPLE_HAMMER",
  "thumbnail": "Preview/hammer.png",
  "assets": [
    {
      "source": "Assets/RMM/Hammers/example-hammer/Mesh/SM_Example.uasset",
      "packagePath": "/Game/RMM/Hammers/example-hammer/Mesh/SM_Example"
    },
    {
      "source": "Assets/RMM/Hammers/example-hammer/Mesh/SM_Example.uexp",
      "packagePath": "/Game/RMM/Hammers/example-hammer/Mesh/SM_Example"
    }
  ]
}
```

`id` is a unique lowercase slug; keep it unchanged after release because it determines the stable `RMM_...` DataTable row and save reference. `rowName` and `dataAssetPath` must match the values derived from the ID. `displayName` appears in Ragnarock. `meshAssetPath` must resolve to a cooked Static Mesh. `iconAssetPath` points to the in-game icon Texture2D. `thumbnail` is optional and appears only in RMM's library.

Each `assets` entry maps an archive file to a UE package path without a file extension. Sidecars share the same package path as their `.uasset`. Keep all custom package paths unique across enabled hammers. Leave `silhouetteAssetPath` and `symbolAssetPath` null for the basic working package; custom selector assets are optional and should only be added after separate runtime validation. RMM creates the game-specific hammer data asset from supported game metadata when it builds the PAK.

For the full authoring workflow, see [Create a Ragnarock hammer with UE 4.27](custom-hammer-authoring-ue4-27.md).
