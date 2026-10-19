# Azure Blockhead verification record

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
