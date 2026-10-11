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
2. Run `UEProject/Scripts/create_hammer_materials.py` and then `UEProject/Scripts/reimport_azure_mesh.py` with UE's Python Script Plugin. These scripts import the source textures, create surface/accent materials, and assign them to their matching mesh slots. The handle/head materials define and use the game's `handleGauge` and `circularGauge` scalar parameters to brighten the corresponding surfaces as combo builds.
3. Cook for `WindowsNoEditor` with the project's `PCD3D_SM5` target and shared material shader code enabled.
4. Run `build_rhammer.py` from `UEProject/Scripts/`, passing the cooked `Content` directory. The script checks shader metadata, packages cooked assets and sidecars, and includes the project's shader archive.

See the full guide for the Windows cook setup, manifest fields, RMM import/deploy steps, and game verification. The checked-in [`azure-blockhead.rhammer`](azure-blockhead.rhammer) includes the corrected cyan/gold/navy material assignments, mesh, icon, dependencies, and D3D shader archive.

## Verification status

The checked-in package and deployed PAK load in the changing room: the Azure row, icon, and corrected 3D preview were visible together. The package SHA-256 is `c18dcde615aff0c5a6040d719c2bd8117c69c6a90a82d36e0426f37c07382301`; the deployed PAK SHA-256 is `d804d90c8d87986f0e37b7beb296f292a3b05abac75a0b4133556f704fb22f28`.

Earlier race testing showed the Azure model in both hands, but the corrected material build has not yet been independently verified during a race. Combo progression and in-race charge feedback also remain unverified. Do not treat the sample as fully runtime-validated until those checks pass using the normal keyboard bindings and accurate hits.

## Related icon samples

`T_RagnaBlockhead_Preview.png` and `T_SummerCustom_Preview.png` are transparent icon sources for the Ragna Blockhead and Summer Custom library packages. Their imported Texture2D assets and `.rhammer` packages are local to the RMM hammer library, not part of this sample archive.
