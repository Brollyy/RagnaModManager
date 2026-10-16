# Azure Blockhead verification record

This is a test record for the sample package. The step-by-step authoring process is in the [custom hammer guide](../../../docs/custom-hammer-authoring-ue4-27.md).

## v53 package

`AzureBlockhead-v53.rhammer` passed RMM's hammer-library importer. RMM's `HammerPakBuildService` built and deployed a PAK with one custom hammer row. The package contains the authored mesh, body material, base-color texture, transparent icon, preview, and Windows shader archive; both gauge sections resolve to Ragnarock's stock material instances.

A fresh race confirmed the stock combo sequence on Azure Blockhead:

1. Before the deliberate miss, the handle gauges show blue fill while the head markers stay dark: [frame](Race-v53-before-miss.png).
2. A five-second no-input interval causes a miss and resets both gauge displays: [frame](Race-v53-after-miss-reset.png).
3. After play resumes and combo rebuilds, the handle gauges reach gold and both head markers light: [frame](Race-v53-full-after-reset.png).

The race uses the normal W/I keyboard bindings and deliberately includes missed notes, so it demonstrates fill, full combo, and reset rather than an uninterrupted clean streak.

## Visual notes

The bright cyan body face now has a dark recessed pocket around the stock head marker. The stock combo marker remains exposed and lights at full combo; the remaining masked edge speckles are confined to that dark inset instead of crossing the cyan face. UE 4.27 still reports degenerate tangent-basis warnings during FBX import.
