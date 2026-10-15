# Azure Blockhead hammer sample

This sample contains the editable assets, UE 4.27 project, build scripts, and a cooked `.rhammer` package for a custom Ragnarock hammer made from scratch. The full human-followable workflow is in the [custom hammer authoring guide](../../docs/custom-hammer-authoring-ue4-27.md).

## Files

- `Source/SM_RMM_AzureBlockhead.obj` and `.mtl`: generated mesh and the three material slots `M_Character`, `M_JaugeManche`, and `M_JaugeTete`.
- `Source/T_RMM_AzureBlockhead_BaseColor.png`: four-swatch body atlas. Mesh UVs stay well inside each swatch; the imported texture keeps its normal mip chain to avoid shimmer on moving race meshes.
- `Source/T_RMM_AzureBlockhead_GaugeRamp.png`: grayscale U-coordinate ramp for the combo bars.
- `Source/T_RMM_AzureBlockhead_Icon.png`: separate transparent changing-room icon.
- `Source/T_RMM_AzureBlockhead_Silhouette.png` and `..._Symbol.png`: optional source experiments. The UE project does not import or package them; custom selector materials are not needed for this hammer.
- `UEProject/`: UE 4.27 project settings and Python scripts for texture/material import, mesh reimport, and `.rhammer` assembly.
- `hammer.json.template`: RMM package manifest template.
- `AzureBlockhead-v9.rhammer`: previous Windows `PCD3D_SM5` package.
- `AzureBlockhead-v10.rhammer`: previous Windows `PCD3D_SM5` package with 15 cooked UE assets and its shared shader archive.
- `AzureBlockhead-v11.rhammer`: earlier Windows `PCD3D_SM5` package with the corrected atlas UV orientation, normal base-color mip chain, and shared shader archive.
- `AzureBlockhead-v12.rhammer`: current Windows `PCD3D_SM5` package with corrected box winding and gauge bars moved clear of the head and shaft surfaces.

## Rebuild

1. Run `python3 generate_source.py` to regenerate the OBJ, color atlas, icon, and gauge ramp. Python 3 and Pillow are required.
2. Open `UEProject/RMMAzureBlockhead.uproject` in Unreal Engine 4.27 on Windows. Run `UEProject/Scripts/create_hammer_materials.py`, then `UEProject/Scripts/reimport_azure_mesh.py`, with the Python Editor Script and Editor Scripting Utilities plugins enabled.
3. Cook for `WindowsNoEditor` with `PCD3D_SM5` and shared material shader code enabled. The scripts and commands are documented in `UEProject/README.md`.
4. Run `build_rhammer.py` from `UEProject/Scripts/` with the cooked content directory as input. The builder checks the required mesh, icon, body/gauge materials, textures, shader metadata, manifest references, and ZIP integrity.
5. Import the resulting `.rhammer` into RMM, enable only Azure Blockhead, build/deploy the PAK, and select it in Ragnarock's Changing Room.

The included v9 archive SHA-256 is `61fad0f119cdfa6e424441ea719064efc767bdbb6d3948861033e870cda0d68f`. The v10 archive SHA-256 is `fe3261100923af47b88b3f8301719b41ad922d861b59374e2d51d228e4bb09ee`. The v11 archive SHA-256 is `e8e7d979800cea827ae4e905076fad1d38e279d1642133445c7a17ec3186f4b2`. The v12 archive SHA-256 is `f88d4927318c624a500b35cca9ae5a3bfc323f5db075ad2cb5fa189ad4ad15ae`.

The [v12 Changing Room capture](Verification/ChangingRoom-v12.png) shows the custom icon, selected Azure entry, and its upright 3D preview after deployment. The earlier [v11 capture](Verification/ChangingRoom-v11.png), [v10 capture](Verification/ChangingRoom-v10.png), and [v9 capture](Verification/ChangingRoom-v9.png) are kept for comparison.

The [v12 race capture](Verification/Race-Azure-v12-texture.png) confirms the custom mesh and corrected cyan/navy atlas colors load in play. It also shows visible black stippling on one head/gauge face. V12 fixes inverted box bounds and moves the gauge bars clear of intersecting geometry, but that visual artifact remains unresolved. The earlier [v11 race capture](Verification/Race-Azure-v11-texture.png), [v10 race capture](Verification/Race-Azure-v10-active.png), [v9 race capture](Verification/Race-Azure-active.png), and paired [stock active](Verification/Race-stock-active.png) and [stock after pause](Verification/Race-stock-after-pause.png) captures remain for comparison. The Changing Room and race captures do not prove blue buildup, the gold transition, or a reset matching stock; treat the combo appearance as unresolved.

## Combo materials

The body material stays fixed. `M_JaugeManche` responds to the `handleGauge` scalar and fills the dedicated handle bar blue. `M_JaugeTete` uses `handleGauge` for the blue first half and `circularGauge` for the gold second half of its dedicated head bar. Both use a dark unfilled track. The game writes these parameters to the hammer's dynamic materials.

V9 through v11 remain earlier comparison packages. V12 was rebuilt and cooked in the isolated Windows UE 4.27 project, imported into RMM, and deployed with Azure as the only enabled custom hammer. Its icon, label, and upright 3D preview are visible in [ChangingRoom-v12.png](Verification/ChangingRoom-v12.png), and [Race-Azure-v12-texture.png](Verification/Race-Azure-v12-texture.png) confirms its model and atlas colors load in a race. The deployed PAK SHA-256 is `0abb01d17bd80c3d5e6f10c21e2d00470e4ed3890213676f0738a0d5980a7f0d`; the v12 `.rhammer` SHA-256 is `f88d4927318c624a500b35cca9ae5a3bfc323f5db075ad2cb5fa189ad4ad15ae`. Black stippling remains visible on one head/gauge face and needs investigation. Combo progression, reset, and visual comparison against stock remain unverified; do not claim the combo meter works until Step 6 passes with valid input.
