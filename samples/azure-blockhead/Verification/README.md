# Azure Blockhead verification record

This is a test record for the sample package. The step-by-step authoring process is in the [custom hammer guide](../../../docs/custom-hammer-authoring-ue4-27.md).

## v71 face-continuous atlas mapping

The v69 Changing Room captures showed that the FBX V flip moved the assigned atlas regions vertically: the front face sampled the navy artwork instead of cyan. Pre-flipping the authored UVs corrected the quadrant selection, but each piece around the stock head-marker opening still stretched the entire tile independently. The captured [first angle](ChangingRoom-v69-angle-1.png) and [second angle](ChangingRoom-v69-angle-2.png) show the UV layout problem clearly.

The v71 mesh builder now projects all pieces through their complete box face, so the art should continue behind the marker opening instead of restarting on every cutout panel. It also remaps all four retained body UV channels to the navy swatch, while preserving the stock gauge UV channels and materials. UE 4.27 cooked the package with zero errors or warnings. The v71 package SHA-256 is `64fc33ec8abb16b64810ca3b327ab39d66d14bc7a961d0f0985726b000ef6233`; RMM built and deployed a one-row PAK with SHA-256 `298326ceae53314747f4a4116a44c43d79e76980ad89871306b0c354807c5904`.

The current v71 runtime has loaded the main-menu map, but its viewport remains on `Checking Internet connectivity…`; no Changing Room image is available for this build yet. The atlas projection change therefore remains visually unverified in game. Physical VR head movement is also unverified.

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
