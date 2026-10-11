# Azure Blockhead sample

This sample provides original source art and a UE 4.27 project for following the [custom hammer authoring guide](../../docs/custom-hammer-authoring-ue4-27.md).

## Source files

Run `python3 generate_source.py` to regenerate these editable files:

- `Source/SM_RMM_AzureBlockhead.obj` and its `.mtl`: low-poly hammer mesh with `M_Character`, `M_JaugeManche`, and `M_JaugeTete` slots in the same order used by stock meshes. The pivot is at the grip point.
- `Source/T_RMM_AzureBlockhead_BaseColor.png`: color atlas used by the cyan mesh section.
- `Source/T_RMM_AzureBlockhead_Icon.png`: separate changing-room icon.
- `Source/T_RMM_AzureBlockhead_Silhouette.png` and `Source/T_RMM_AzureBlockhead_Symbol.png`: optional selector art. The sample package leaves the game's `Silhouette` and `Symbol` fields at their stock defaults because custom marker materials have not been validated safely in-game.

## Build the sample package

1. Open `UEProject/RMMAzureBlockhead.uproject` in Unreal Engine 4.27 on Windows.
2. Run `UEProject/Scripts/create_hammer_materials.py` and then `UEProject/Scripts/reimport_azure_mesh.py` with UE's Python Script Plugin. These scripts import the source textures, create surface/accent materials, and assign them to their matching mesh slots. The body and handle/head materials define the game's `handleGauge` and `circularGauge` scalar parameters and blend visible surfaces toward a bright energized color as combo builds. The body scales and clamps the gauge values so early buildup is visible across a large surface. Emissive output adds a second response but is not the only indication.
3. Cook for `WindowsNoEditor` with the project's `PCD3D_SM5` target and shared material shader code enabled.
4. Run `build_rhammer.py` from `UEProject/Scripts/`, passing the cooked `Content` directory. The script checks shader metadata, packages cooked assets and sidecars, and includes the project's shader archive.

See the full guide for the Windows cook setup, manifest fields, RMM import/deploy steps, and game verification. The checked-in [`azure-blockhead.rhammer`](azure-blockhead.rhammer) includes the corrected cyan/gold/navy material assignments, mesh, icon, dependencies, and D3D shader archive.

## Verification status

The latest checked-in package SHA-256 is `7915d30ad735da682b21b2d9ad3cddc5c12d63c75d04b6518e4a2943184008d4`. The deployed PAK SHA-256 is `b18adc2a75a9010923233ff02f298e31045cd57f0c6ac544fbfb4ae2c70806d3`. The package and deployed PAK were verified to match these hashes. The Azure row, icon, and 3D preview loaded in the changing room.

Combo feedback was verified in a race on the `RMM COMBO METER FIXTURE` custom song with only Azure enabled and the normal W keyboard binding. The RagnaLoader stats hook recorded 33 hits out of 40 (28 normal, 5 perfect, 7 misses, and one boost event). During the song, the large cyan body panels visibly shifted to bright yellow as combo built. This confirms both the stock combo material parameters reach the custom hammer and the body material responds visibly.

## Related icon samples

`T_RagnaBlockhead_Preview.png` and `T_SummerCustom_Preview.png` are transparent icon sources for the Ragna Blockhead and Summer Custom library packages. Their imported Texture2D assets and `.rhammer` packages are local to the RMM hammer library, not part of this sample archive.
