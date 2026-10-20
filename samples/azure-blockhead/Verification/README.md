# Azure Blockhead verification record

## Current result: stock-aligned head geometry

The stock donor's `M_Character` head spans X −16..16, Y −13.6..13.5, and Z 37.5..55.25. The custom replacement had used X −10.5..10.5 and Y −15.875..25, making it narrower and 13.8 units deeper than the stock head. That put its rear face beyond the donor head and made the front and rear textures appear detached as the preview turned.

The builder now keeps the front just proud of the stock head gauge at Y −15.875 and places the rear at the donor's Y=13.5 boundary; it uses the full stock X and Z extents. The full UE 4.27 cook completed and the RMM package builder included 25 cooked files. The RagnaLoader Changing Room case passed, and the [front view](ChangingRoom-v158-stock-aligned-front.png) plus [12-frame rotation sweep](ChangingRoom-v158-stock-aligned-sweep.jpg) show the custom face staying on the hammer head as it turns. This verifies the flat-mode preview; physical VR head-motion behavior has not been tested in this run.

## Stock comparison and atlas diagnosis

The stock Default Hammer remains the baseline: [stock capture](ChangingRoom-v72-stock.png). Its `M_Character` surface and both combo-gauge sections use donor mesh UVs/materials. Azure retains the stock handle and the two gauge sections, with their stock materials and all four UV channels. Those stock combo parts render correctly.

The custom Azure head is the difference: it projects new UVs into four regions of one atlas. The atlas material samples cooked `TexCoord[0]`. A flat-color ice tile still showed the same dark streaks, so the artwork details are not the cause. Increasing the atlas padding to 320 pixels also left angle-dependent streaks in the full rotation: [v106 sweep](ChangingRoom-v106-padded-atlas-sweep.jpg). At that point cross-region filtering was a working hypothesis, but the v107 split-texture capture below shows it cannot explain all of the artifacts.

I then built v107 with one clamp-addressed Texture2D and material section per painted tile, retaining the original three stock slots first. UE 4.27 cooked the package and RMM built a one-row PAK. The first launch attempt stayed on a black screen, but that was a transient launch failure: rerunning the same v107 package reached the Changing Room and completed all 16 interaction captures. The [v107 rotation sweep](ChangingRoom-v107-split-textures-sweep.jpg) and [two full-size frames](ChangingRoom-v107-split-textures-angle-07.png) / [frame 09](ChangingRoom-v107-split-textures-angle-09.png) record the result. Compare those with the [stock hammer capture](ChangingRoom-v72-stock.png): the stock surfaces remain clean while Azure's custom head has severe dark streaks and patches at several rotation angles. Splitting the atlas into separate clamp-addressed textures did not remove them, so atlas tile bleed alone is not the cause. The retained stock handle and head gauges continue to render. This narrows the defect to the custom mesh surface UV/material assignment or geometry; it does not identify which of those is responsible. The known-good v87 package was later restored and deployed; its PAK SHA-256 is `c9d50410ed4b8a924540a80147d0507f909f1bb447a1fac3371a8f6ffa3cff4b`.

## v87 stock-gauge interaction investigation

The stock comparison remains clean: [stock Default Hammer](ChangingRoom-v72-stock.png). Azure's atlas stays attached to its faces, but black marks remain at the first preview angle: [Azure angle 1](ChangingRoom-v87-angle-1.png). A second angle is clean across the head face: [Azure angle 2](ChangingRoom-v87-angle-2.png). v87 adds inner walls around the recessed head-marker pocket and samples a small solid patch from the navy tile on those walls. This avoids the stretched stripes from the full-tile wall experiment, but does not resolve the marks at every angle.

The v89 constant-body/no-gauges capture is not valid evidence: the preview did not render a usable hammer, so it cannot isolate the atlas from gauge geometry. Separate marker-hidden and gauge-section probes still showed the atlas artifact. The stock gauge sections and combo response are independently verified and remain the working comparison path.

## Stock UV versus custom atlas mapping

The stock `M_Character` body uses donor UVs; the custom Azure head uses new per-face UV projections into four atlas tiles. The body material samples `TexCoord[0]`. The donor gauge sections retain their stock UVs and material assignments, so their working combo response does not validate the custom atlas mapping. The builder additionally reflects U within each atlas tile and stores FBX V pre-flipped; the generated atlas image is also vertically flipped for UE's texture-coordinate convention. Those custom mapping transforms are the relevant comparison points for the reported reversed-looking atlas in VR.

## v94 per-tile horizontal atlas diagnostic

I exported the freshly imported UE 4.27 StaticMesh and inspected its first UV channel. The custom head UVs are present on every custom body face with non-degenerate triangles; the stock handle and head gauge UV/material sections remain separate. This rules out the earlier stale cooked export as evidence about the current atlas channel.

For a direct orientation comparison, v94 flips the artwork horizontally inside each atlas tile while keeping the mesh, material, and gauge assets unchanged. The cyan emblem moves to the opposite side, but the dark stippling remains on the ice panel. A simple left-right artwork flip does not resolve the rendering defect. The interaction case passed and the captured Changing Room view is [here](ChangingRoom-v94-horizontal-atlas-diagnostic.png). This flat preview still does not test physical VR head movement.

The diagnostic `.rhammer` SHA-256 is `6a74071f57424df45c7e32a57d0262ce0f09a82bdf0446f4246f89bbf3665222`; its deployed test PAK SHA-256 was `ce3c25136433a082fb7f4ed065b88c6b7a0c6d799ea1c47ee2b93c914136e956`. The normal v87 atlas was rebuilt and restored after the capture.

## v95 current source rebuild

After the flip diagnostic, I restored the standard atlas, recreated the UE texture/material, reimported the checked-in mesh, and cooked a fresh Windows package. Both Changing Room interaction cases passed. The first view shows the ice panel without the dense stippling seen in older captures: [angle 1](ChangingRoom-v95-current-rebuild-angle-1.png). A later preview angle still shows strong black diagonal banding across that panel: [angle 2](ChangingRoom-v95-current-rebuild-angle-2.png). The issue is therefore angle-dependent and remains unresolved; the atlas and mesh still need a fix that holds across the preview rotation.

The current standard-texture `.rhammer` SHA-256 is `688e9158f4e45f62378243f200ae876b99278118e1a07d41835a4aa8a2b20da0`; its deployed PAK SHA-256 is `b04b37a31915f31d138db61ccdb88ad612eac15e6cc59c454274821eddab52b1`. This is the normal package restored in Ragnarock after the temporary marker-hidden diagnostic.

## v98 texture-group filter probe

Because the atlas banding changes with preview angle, I rebuilt the same mesh and texture with the texture's filter set to `TF_DEFAULT` so Unreal would use the World texture group's filter, instead of forcing trilinear filtering. The interaction cases passed. The previously banded angle 2 became visually cleaner: [angle 2](ChangingRoom-v98-texture-group-default-angle-2.png). However, angle 1 then rendered the whole ice side black: [angle 1](ChangingRoom-v98-texture-group-default-angle-1.png). The setting shifts the failure between views and is not a fix. The diagnostic `.rhammer` SHA-256 is `8fb7aec4a362a7bc12824dcb2e42960b7972c435cc01934c5c9e8dc99f674c1c`; its temporary PAK SHA-256 was `1c0e16148bf20d472f9a823825b86df2bf8388b156126d8aeb1490f991887951`. The normal trilinear package was redeployed afterward.

## v96 stock-marker isolation

To separate the custom atlas from the stock head marker, I imported a temporary copy of the current UE mesh with only the marker section's vertices recessed into the head. The atlas, material, and handle gauge were unchanged. The Changing Room capture still shows the same black diagonal banding across the custom ice face: [marker-hidden angle 2](ChangingRoom-v96-marker-hidden-angle-2.png). This rules out the stock head-marker geometry as the sole source of the bands; it does not rule out texture LOD/mip sampling or the custom side-face UV projection. The diagnostic `.rhammer` SHA-256 is `53908dfdd406a4349e29a1e3e2b5530cd9c521bb48a8ce1347f43855d5104e9e`, and its temporary deployed PAK SHA-256 was `34e42de1e68609162f5b0654acadf54fdf6ca1bdb9eecb9f1f6a395186f53090`. The normal package was redeployed immediately afterward.

The stock Default Hammer comparison confirms the gauge sections use their donor UVs/materials and render cleanly. Azure's custom `M_Character` faces instead use projected UVs into four inset atlas regions, with the surface material sampling `TexCoord[0]`; changing marker geometry does not alter the artifact. The next useful controlled comparison is a custom atlas build with mip bleed prevented or a single untextured-color side face, keeping the stock handle and gauges identical.

## v97 handle-gauge isolation attempt (invalid)

A temporary build moved the handle-gauge vertices into the head to see whether that stock section overlaps the custom atlas. The preview then showed no hammer at all: [invalid capture](ChangingRoom-v97-handle-gauge-hidden-invalid.png). Although the UE import, cook, and RagnaLoader interaction case completed, the missing mesh makes this test unusable for causal comparison. Do not infer that the handle gauge causes the atlas artifact from this run. Its temporary PAK SHA-256 was `07d4acb8a6451acb41f8c01649027680ce73a597fdddaf8377feb74ce08e045a`; the normal package was restored afterward with PAK SHA-256 `b04b37a31915f31d138db61ccdb88ad612eac15e6cc59c454274821eddab52b1`.

UE 4.27 cooked the textured v87 build with zero errors and one unrelated MLSDK warning. The rebuilt `.rhammer` SHA-256 is `59ff9ed227ef8330b1add20c8aa29fd3b2dd1da3ffbe7a159c9cd18a57e45414`; RMM deployed the one-row PAK with SHA-256 `bd22160c175803d8fa151606c8eaadf8164a509060e4752a676b2f2a2e70f607`. Both stock gauge sections are enabled in the deployed package. The black marks remain, so this is still an investigation build.

The earlier v77 capture showed atlas tiles bleeding across the retained handle: [v77](ChangingRoom-v77-hammer-preview.png). Exporting the UE-imported mesh back to FBX showed that the old material sampled UV map 1, whose retained-body coordinates span unrelated atlas regions. Switching to cooked `TexCoord[0]` corrected that separate tile-bleed issue. v82 also patched degenerate retained-body UV triangles and reduced some edge artifacts: [v82 angle 1](ChangingRoom-v82-angle-1.png), [angle 2](ChangingRoom-v82-angle-2.png). The stock Stairway baseline is [here](ChangingRoom-v74-stock-stairway.png).

## v79 atlas channel correction

The earlier v77 capture showed bleeding because the body material sampled UV map 1 instead of the remapped UV map 0. v79 switched the material to `TexCoord[0]`; the atlas artwork appeared on the intended head faces as the preview rotated, though edge stipple remained. The subsequent v82 body UV patch removed the tangent-basis warnings and reduced those marks.

## v76 atlas U-axis correction (runtime visual check pending)

The v76 source FBX stores the custom front face with x=-10.5 at U=0.42 and x=+10.5 at U=0.08 in all four UV channels. The builder now emits that same tile-local U reflection; the stock gauge sections and their UVs remain unchanged. UE 4.27 cooked the package for `WindowsNoEditor` / `PCD3D_SM5` with zero errors and one unrelated MLSDK warning. The `.rhammer` SHA-256 is `02157062c92c449a53ee1dc35f70842c89e5d880efc277de95aac9b7f6425973`; RMM built and deployed a one-row PAK with SHA-256 `48eb6703bbc2e4bc73eecf07308087d8b785aab2e2fe64fe315c22088c45562e`.

The game log records the deployed PAK in the precacher. A fresh RagnaLoader keyboard case launched the game to its initial menu, but the game window stayed at 228×212 despite the requested 948×988 size. The case stopped before it could navigate to the Changing Room, so this still does not verify the atlas in game. A clear Changing Room capture is required.

## v74 atlas orientation comparison (not yet resolved in VR)

The v73 Changing Room capture showed the asymmetric cyan mark rotated 180 degrees from the authored tile. v74 flips both normalized UV axes within each atlas tile. I deployed the v74 package and compared it in the live Changing Room against the stock Stairway to Valhalla hammer. The stock mesh and its combo marker render normally. Azure's cyan front and gold side tiles are visible as the preview rotates, but the cyan mark is obscured by the retained head-gauge area in the current angles; this does not yet establish that the atlas orientation or VR head-motion behavior is correct.

- [Stock Stairway to Valhalla](ChangingRoom-v74-stock-stairway.png)
- [Azure angle 1](ChangingRoom-v74-azure-angle-1.png)
- [Azure angle 2](ChangingRoom-v74-azure-angle-2.png)
- [Azure angle 3](ChangingRoom-v74-azure-angle-3.png)

These are 468×472 host captures of a 948×944 logical game window under 2× scaling. The flat preview confirms that custom atlas colors render on the mesh and rotate with it; physical VR tracking remains unverified.

This is a test record for the sample package. The step-by-step authoring process is in the [custom hammer guide](../../../docs/custom-hammer-authoring-ue4-27.md).

## v72 face-continuous atlas mapping

The v69 Changing Room captures showed that the FBX V flip moved the assigned atlas regions vertically: the front face sampled the navy artwork instead of cyan. Pre-flipping the authored UVs corrected the quadrant selection, but each piece around the stock head-marker opening still stretched the entire tile independently. The captured [first angle](ChangingRoom-v69-angle-1.png) and [second angle](ChangingRoom-v69-angle-2.png) show the UV layout problem clearly.

The v72 mesh builder projects the four front panels through their complete box face, so the artwork continues around the stock marker opening instead of restarting on each cutout panel. Other box sides use their full atlas regions, and the gold trim retains the known-good full-tile mapping. The builder remaps all four retained body UV channels into the navy swatch while preserving the stock gauge UV channels and materials. UE 4.27 cooked v72 with zero errors or warnings. Its package SHA-256 is `132bc57436afdc7e865b6cd511cdef40b8e4e441668a89b47db918f2ab31f852`; RMM built and deployed a one-row PAK with SHA-256 `7f18e9d82fd0a8b1c4deab655cf8a34b9832674595915b176e3d68117a470bcb`.

Three v72 Changing Room captures show the cyan atlas artwork staying on the head as the preview rotates: [first angle](ChangingRoom-v72-angle-1.png), [second angle](ChangingRoom-v72-angle-2.png), [third angle](ChangingRoom-v72-angle-3.png). A stock Silver hammer captured in the same room has clean surfaces: [stock comparison](ChangingRoom-v72-stock.png). In Dewey, keyboard W/I swings confirmed the custom atlas stays attached to the moving hammer at two race poses: [first pose](Race-v72-pose-1.png), [second pose](Race-v72-pose-2.png). Azure's custom head still shows dark stippling at some edges in the preview and race; that remains under investigation. The v71 split-trim UV experiment did not load consistently, so v72 retains the full-tile gold trim mapping that works in the room. The known-working v70 baseline is also recorded [here](ChangingRoom-v70-baseline.png). Physical VR head movement remains unverified.

## v73 high-quality atlas compression

The v72 atlas uses UE's default BC1 compression. v73 switches the body atlas to BC7 at highest compression quality; its cooked texture export grows from 175,491 to 350,307 bytes. UE 4.27 cooked v73 without errors or warnings. The `.rhammer` SHA-256 is `9696b15ead7f6c9f17394a559f3898904c9ce5d8a31ede829671a44888b4e6af`; RMM deployed a one-row PAK with SHA-256 `8e32b33de3ad53860c3f31d81d77e1b80f276967c98ee8cc1edf7acf76a9d536`.

The v73 captures show the cyan art remaining on the mesh in the Changing Room and across two keyboard-driven Dewey race poses: [Changing Room angle 1](ChangingRoom-v73-angle-1.png), [angle 2](ChangingRoom-v73-angle-2.png), [angle 3](ChangingRoom-v73-angle-3.png), [race pose 1](Race-v73-pose-1.png), [race pose 2](Race-v73-pose-2.png). Dark edge marks are still visible on the custom mesh after the BC7 change. The stock Silver hammer remains clean in the v72 side-by-side capture. Physical VR head movement is not covered by these flat-screen captures.

## v60 atlas package

`AzureBlockhead-v60.rhammer` was cooked in UE 4.27 with nine package assets, imported into the RMM hammer library, built as a one-row custom-hammer PAK, and deployed to Ragnarock. The package uses the new detailed 2×2 enamel atlas while retaining the stock handle and head gauge material references.

The RagnaLoader `flat-ragna-blockhead-hammer-hover-custom-visual` interaction case passed. A follow-up case used the same menu path and captured the live preview at three points in its rotation. The cyan, navy, and ice atlas details remain on their respective mesh faces as the model turns:

1. [Initial angle](ChangingRoom-v60-angle-1.png)
2. [Second angle](ChangingRoom-v60-angle-2.png)
3. [Third angle](ChangingRoom-v60-angle-3.png)

This confirms the atlas is attached to the mesh in the flat changing-room preview. It does not establish how the texture behaves during physical head movement in VR. The headsetless Monado cloakroom case did not reach the door actor (`BP_DoorCR_3` was not found in its starting map), so the direct VR view remains unverified.

## v64 head-marker clearance

The v60 captures showed dark marker-shaped patches on top of the atlas in oblique views. To isolate the cause, I built a temporary mesh with the stock head-marker section removed; the atlas rendered cleanly. A production-preserving build then retained the stock marker material and geometry, moved its frontmost point 0.75 Blender units ahead of the atlas plane, and widened the opening around its measured bounds from 2 to 3 units. The v64 package was imported into RMM and deployed as the only enabled custom hammer.

Three captures across the Changing Room preview rotation show the cyan, navy, and ice atlas faces without the previous patches. The stock handle gauge remains on the mesh. This verifies the atlas in the flat preview; the race combo fill and physical VR head movement have not been rechecked with v64.

1. [First angle](ChangingRoom-v64-angle-1.png)
2. [Second angle](ChangingRoom-v64-angle-2.png)
3. [Third angle](ChangingRoom-v64-angle-3.png)

The v64 `.rhammer` contains nine cooked package assets. Its SHA-256 is `3158bfa7bd2bf5c1dd07a4730336756fab95a50ebc0a8b6e86dec8031dbeb647`; the deployed one-row PAK SHA-256 is `01ce28fa6cfc8d545b90fbcc3129ebcb592ffdef54205bc4887156a555c197ad`.

## v65 atlas mipmaps

The race capture of v64 showed severe black and white aliasing on the moving atlas faces. The UE project asset still had `TMGS_NO_MIPMAPS`, despite the builder comment claiming it generated mips. The texture creation script now explicitly uses the World texture group's mip generation and trilinear filtering. The UE 4.27 editor reports `TMGS_FROM_TEXTURE_GROUP`, `TF_TRILINEAR`, and `TA_CLAMP` for both address axes after the rebuild. The cooked texture export grew from 131,463 bytes to 175,491 bytes, consistent with the additional mip levels.

`AzureBlockhead-v65.rhammer` was imported into RMM and deployed with exactly one custom hammer row. Its SHA-256 is `9c6641a5cbb45271a97e6e964711e715c60ed58e70cd6b29530b569f6d7fd476`; the deployed PAK SHA-256 is `6905d757171e7b5b8acb865bd4d203383d3dc82ccb0af34666c6b8a7d671eb8e`. The host Steam launch did not produce a game process or window, so v65's in-game appearance and physical VR motion remain unverified.

## v55 package

`AzureBlockhead-v55.rhammer` passed RMM's hammer-library importer. RMM's `HammerPakBuildService` built and deployed a PAK with one custom hammer row. The package contains the authored mesh, body material, base-color texture, transparent icon, preview, and Windows shader archive; both gauge sections resolve to Ragnarock's stock material instances.

A fresh race confirmed the stock combo sequence on Azure Blockhead:

1. Before the deliberate miss, the handle gauges show blue fill while the head markers stay dark: [frame](Race-v55-before-miss.png).
2. A five-second no-input interval causes a miss and resets both gauge displays: [frame](Race-v55-after-miss-reset.png).
3. After play resumes and combo rebuilds, the handle gauges reach gold and both head markers light: [frame](Race-v55-full-after-reset.png).

The race uses the normal W/I keyboard bindings and deliberately includes missed notes, so it demonstrates fill, full combo, and reset rather than an uninterrupted clean streak.

## Visual notes

The stock head marker sits on the narrow end face, with the head body extending behind it along the head's long axis. The stock marker remains exposed and lights at full combo. UE 4.27 still reports degenerate tangent-basis warnings during FBX import.
