# Create a Ragnarock hammer package with UE 4.27

This guide covers a self-contained custom hammer package for RagnaModManager (RMM). It starts with new source files, imports and cooks them in Unreal Engine 4.27, and packages the cooked assets as an `.rhammer` library archive. RMM then creates the game-specific data asset and merged PAK for the installed Ragnarock build.

The repository includes original source geometry, material art, and an icon for the [Azure Blockhead sample](../samples/azure-blockhead-source/README.md). Those source files are ready for the UE import/cook steps below; they are not mislabeled as cooked game assets.

Ragnarock's inspected build uses UE 4.27 and a V11 PAK. Use the same engine version and cook for `WindowsNoEditor`, even when playing through Proton. The public RMM app does not import FBX/OBJ or cook Unreal assets; the creator must provide cooked UE assets. For materials, use a Windows UE 4.27 cooker: Linux UE builds cook Vulkan shader maps, while this game requests `PCD3D_SM5` shader maps at runtime.

## What the game expects

The installed `DT_Hammers` DataTable row has these fields:

| Field | Purpose in the custom row |
| --- | --- |
| `Title` | In-game hammer name |
| `Description` | In-game description |
| `Icon` | `Texture2D` shown in the changing-room hammer list |
| `EntitlementId` | Empty for a selectable, non-DLC custom hammer |
| `Data` | Reference to the hammer's data asset |

The game hammer data asset is an instance of the shared `DA_Hammers_C` class. Its inspected fields are `Mesh`, `Silhouette`, and `Symbol`; the stock `DA_SummerHammer` references a Static Mesh and two Material Instances for these fields. The common class default object also supplies `DrumHitSound`, an FMOD `DrumHit` event. RMM preserves this class and its default when it generates each hammer data asset, so creators do not need to add a combo counter or a new sound class.

There is no combo-meter field in `DT_Hammers` or the custom hammer data asset schema inspected for this game build. The extracted `BP_HammerSelector` contains the `Silhouette` and `Symbol` references, while the shared hammer class default supplies `DrumHitSound`; none of these is a custom combo-counter asset. A package should keep the `DA_Hammers_C` parent, provide a valid mesh, and leave `EntitlementId` empty. The game should continue to use its normal combo logic when hits register; confirm this by playing a song with the custom hammer equipped. `DrumHitSound` is the shared hit sound, not the combo counter. A custom hit sound is outside the current RMM package format.

For complete custom presentation, include all four kinds of assets:

1. A cooked `StaticMesh` and its custom material/texture dependency graph.
2. A cooked `Texture2D` for the changing-room list icon.
3. A cooked `MaterialInstanceConstant` for the `Silhouette` data asset property, plus its material and texture dependencies.
4. A cooked `MaterialInstanceConstant` for the `Symbol` data asset property, plus its material and texture dependencies.

`Silhouette` and `Symbol` are `UMaterialInterface` references. This RMM format expects their assigned package exports to be `MaterialInstanceConstant` assets. Base Materials and textures can be shared between these instances if the visual design needs it, but every custom dependency must be included in the archive. A library `thumbnail` is only a desktop preview and does not become the in-game `Icon`.

## 1. Make the source model and texture art

Create the hammer in a 3D application such as Blender. Use a single Static Mesh unless the model needs separate pieces. Keep the grip and head proportions close to Ragnarock's existing hammers so the item reads clearly at the in-game size.

Before export:

- Put the mesh at the origin and set its pivot where the hand should grip it. Apply scale and rotation.
- Triangulate the mesh and check face normals.
- Create a non-overlapping UV set for the surface textures. Add a second UV set only if the mesh needs baked lighting.
- Assign stable material slot names and keep the number of slots small.
- Inspect the collision generated for the Static Mesh and keep it simple. The hammer Data Asset has no per-hammer collision field, so collision settings come from the mesh asset itself.
- Export FBX 2018, which matches UE 4.27's FBX importer. Export only the intended mesh and material assignments.

Create the texture art for the body and optional normal, roughness, metallic, or mask maps. Keep the icon art as a separate square PNG with a transparent or solid background and a strong silhouette at small size. Create separate silhouette/symbol art if those in-game material effects should differ from the surface material.

## 2. Import and author assets in UE 4.27

Create a blank UE 4.27 project and keep the project content under unique folders that will not collide with Ragnarock's `/Game/` assets. For example:

```text
Content/RMM/Hammers/<hammer-id>/Mesh/SM_<Hammer>
Content/RMM/Hammers/<hammer-id>/Materials/M_<Hammer>
Content/RMM/Hammers/<hammer-id>/Materials/MI_<Hammer>
Content/RMM/Hammers/<hammer-id>/Textures/T_<Hammer>_BaseColor
Content/RMM/Hammers/<hammer-id>/UI/T_<Hammer>_Icon
Content/RMM/Hammers/<hammer-id>/UI/M_<Hammer>_Silhouette
Content/RMM/Hammers/<hammer-id>/UI/MI_<Hammer>_Silhouette
Content/RMM/Hammers/<hammer-id>/UI/M_<Hammer>_Symbol
Content/RMM/Hammers/<hammer-id>/UI/MI_<Hammer>_Symbol
```

Import the FBX as a Static Mesh. Inspect scale, orientation, pivot, normals, material slots, UVs, and collision in the Static Mesh Editor. Set up the surface Material and Material Instances and assign them to the mesh sections. Import the icon and any silhouette/symbol textures as Texture assets. Build base Materials and create the two `MaterialInstanceConstant` assets assigned to `Silhouette` and `Symbol`.

Keep all custom dependencies in the project. Do not use a Material Instance whose parent, texture, or other referenced package is in the project unless that package is included in the hammer archive too. References to Ragnarock's stock content may resolve from the installed game, but a package intended to work independently should own its mesh and visual dependencies.

Use the editor to preview the mesh and UI materials and fix broken references before cooking. The changing-room 3D preview, list icon, silhouette material, and symbol material are separate outputs; verify each one separately.

## 3. Cook the assets for the game

Unreal stores editor assets in source formats and converts them to platform-specific formats during cooking. Cook for `WindowsNoEditor`, which is the platform used by the installed PC game:

```text
UE4Editor-Cmd.exe <ProjectPath>/<ProjectName>.uproject -run=cook -targetplatform=WindowsNoEditor -cookall
```

Alternatively, use **File → Package Project → Windows (64-bit)** in UE 4.27. The command-line cooker writes under the project `Saved/Sandboxes/Cooked-WindowsNoEditor` directory. Packaging writes into `Saved/StagedBuilds/WindowsNoEditor`. Locate the generated `.uasset` files and their sidecars (`.uexp`, `.ubulk`, or `.uptnl` when present) under the project's cooked `Content` tree. Keep each file belonging to an asset together.

Do not rename cooked package internals by renaming files. Make package names and folders in the editor before cooking. For each manifest `packagePath`, copy the matching cooked package files and preserve their relative sidecars. The RMM archive path is independent of the in-game package path.

## 4. Build the `.rhammer` archive

Create `hammer.json` at the archive root. RMM derives the DataTable row name and generated hammer Data Asset path from `id`. The `iconAssetPath`, `silhouetteAssetPath`, and `symbolAssetPath` values must each name a cooked asset included in `assets`.

```json
{
  "formatVersion": 1,
  "id": "my-hammer",
  "name": "My Hammer",
  "author": "Creator",
  "version": "1.0.0",
  "description": "A custom Ragnarock hammer.",
  "rowName": "RMM_MY_HAMMER",
  "displayName": "My Hammer",
  "meshAssetPath": "/Game/RMM/Hammers/my-hammer/Mesh/SM_MyHammer",
  "iconAssetPath": "/Game/RMM/Hammers/my-hammer/UI/T_MyHammer_Icon",
  "silhouetteAssetPath": "/Game/RMM/Hammers/my-hammer/UI/MI_MyHammer_Silhouette",
  "symbolAssetPath": "/Game/RMM/Hammers/my-hammer/UI/MI_MyHammer_Symbol",
  "dataAssetPath": "/Game/Data/Hammers/DA_RMM_MY_HAMMER",
  "thumbnail": "Preview/my-hammer.png",
  "assets": [
    { "source": "Assets/Mesh/SM_MyHammer.uasset", "packagePath": "/Game/RMM/Hammers/my-hammer/Mesh/SM_MyHammer" },
    { "source": "Assets/Mesh/SM_MyHammer.uexp", "packagePath": "/Game/RMM/Hammers/my-hammer/Mesh/SM_MyHammer" },
    { "source": "Assets/Materials/M_MyHammer.uasset", "packagePath": "/Game/RMM/Hammers/my-hammer/Materials/M_MyHammer" },
    { "source": "Assets/Materials/MI_MyHammer.uasset", "packagePath": "/Game/RMM/Hammers/my-hammer/Materials/MI_MyHammer" },
    { "source": "Assets/Textures/T_MyHammer_BaseColor.uasset", "packagePath": "/Game/RMM/Hammers/my-hammer/Textures/T_MyHammer_BaseColor" },
    { "source": "Assets/UI/T_MyHammer_Icon.uasset", "packagePath": "/Game/RMM/Hammers/my-hammer/UI/T_MyHammer_Icon" },
    { "source": "Assets/UI/M_MyHammer_Silhouette.uasset", "packagePath": "/Game/RMM/Hammers/my-hammer/UI/M_MyHammer_Silhouette" },
    { "source": "Assets/UI/MI_MyHammer_Silhouette.uasset", "packagePath": "/Game/RMM/Hammers/my-hammer/UI/MI_MyHammer_Silhouette" },
    { "source": "Assets/UI/M_MyHammer_Symbol.uasset", "packagePath": "/Game/RMM/Hammers/my-hammer/UI/M_MyHammer_Symbol" },
    { "source": "Assets/UI/MI_MyHammer_Symbol.uasset", "packagePath": "/Game/RMM/Hammers/my-hammer/UI/MI_MyHammer_Symbol" }
  ]
}
```

The example omits sidecar entries for brevity. Add an `assets` entry for every cooked sidecar and every transitive custom dependency, using the same `packagePath` as its `.uasset`. ZIP `hammer.json`, the assets, and optional preview into a `.zip`, then rename it to `.rhammer`.

## 5. Import and verify in RMM and Ragnarock

1. Import the `.rhammer` in RMM. Reject and fix any missing-file, invalid-class, duplicate-path, or invalid-reference error rather than omitting dependencies.
2. Enable the hammer and build the overlay PAK against the installed game build. RMM verifies the build hash so the merged DataTable matches the game version.
3. Deploy through RMM. The merged DataTable row sets `Title`, `Description`, `Icon`, clears `EntitlementId`, and points `Data` to the generated `DA_RMM_MY_HAMMER` asset. That generated asset points to `meshAssetPath`, `silhouetteAssetPath`, and `symbolAssetPath`.
4. Open the changing room. Confirm the custom icon appears in the list, the correct 3D model appears in the preview, and the symbol/silhouette visuals load.
5. Equip the hammer, start an offline song, and hit a short run of notes. Confirm the model appears in song, hits register, the combo counter rises, and the normal `DrumHit` sound plays. The hit and combo behavior comes from Ragnarock's common input/gameplay class; it is not a custom asset in this package.

## Current RMM support and known limits

RMM validates cooked Static Mesh, Texture2D, and Material Instance exports, copies the declared package files into its PAK, creates the hammer Data Asset, and merges the row into a matching `DT_Hammers`. At PAK build time it also checks each declared cooked package's `/Game/` imports against the enabled package collection and the installed game build. An unresolved project dependency stops the build with the missing package path. Source meshes and textures must be cooked before import. RMM does not invoke Unreal Editor or create cooked meshes/materials/textures.

Stock build evidence used for this guide: UE 4.27 metadata; `DT_Hammers` row fields `Title`, `Description`, `Icon`, `EntitlementId`, and `Data`; `DA_SummerHammer` fields `Mesh`, `Silhouette`, and `Symbol`; `Default__DA_Hammers_C.DrumHitSound` references the shared FMOD event `DrumHit`.

The Azure Blockhead source model and textures in this repository were imported and packaged for a runtime check. Its icon and mesh load in the changing room, but its custom material does not render correctly: the Linux-cooked asset has no `PCD3D_SM5` shader map, so Ragnarock logs that it is using the default material. A trial reassignment to stock game material instances did not resolve the checker rendering. Treat this sample as source art, not as a finished working hammer package, until it has been cooked with Windows UE 4.27 and rechecked in game. Stock material assets cannot be inspected from the encrypted game PAK in this environment.

## UE 4.27 references

- [FBX Static Mesh Pipeline](https://dev.epicgames.com/documentation/en-us/unreal-engine/fbx-static-mesh-pipeline?application_version=4.27)
- [Texture Import Guide](https://dev.epicgames.com/documentation/en-us/unreal-engine/texture-import-guide?application_version=4.27)
- [Content Cooking](https://dev.epicgames.com/documentation/unreal-engine/content-cooking?application_version=4.27)
