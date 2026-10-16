# Azure Blockhead verification record

This is a test record for the sample package. The step-by-step authoring process is in the [custom hammer guide](../../../docs/custom-hammer-authoring-ue4-27.md).

## v46 package

`AzureBlockhead-v46.rhammer` passed RMM's hammer-library importer. RMM's `HammerPakBuildService` built and deployed a PAK with one custom hammer row. The package contains the authored mesh, body material, base-color texture, transparent icon, and shader archive; both gauge sections resolve to Ragnarock's stock material instances.

A fresh race confirmed the stock combo sequence on Azure Blockhead:

1. Before the deliberate miss, the handle gauge shows blue fill while both head markers stay dark: [frame](Race-v46-before-miss.png).
2. A five-second no-input interval causes a miss and resets the gauge: [frame](Race-v46-after-miss-reset.png).
3. After play resumes and combo rebuilds, the handle gauge reaches gold and both head markers light: [frame](Race-v46-full-after-reset.png).

UE4SS recorded 104 hits, 16 misses, and `boosts=1` in this deliberately interrupted run. It verifies the visual states and reset, not an uninterrupted clean-streak score.

## Remaining visual notes

The authored atlas displays the intended cyan, gold, navy, and ice regions in game. Fine masked patterning remains around the stock head marker where it crosses the custom face. UE 4.27 also reports degenerate tangent-basis warnings during FBX import. These are recorded as visual/import polish items; the stock combo stages and reset work.

The Blender source builder duplicates the donor's shared head-marker vertices before moving that section outward by 0.75 units. This prevents the adjustment from moving vertices shared with the hammer body.
