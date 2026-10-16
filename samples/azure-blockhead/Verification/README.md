# Azure Blockhead verification record

This is a test record for the sample package. The step-by-step authoring process is in the [custom hammer guide](../../../docs/custom-hammer-authoring-ue4-27.md).

## v50 package

`AzureBlockhead-v50.rhammer` passed RMM's hammer-library importer. RMM's `HammerPakBuildService` built and deployed a PAK with one custom hammer row. The package contains the authored mesh, body material, base-color texture, transparent icon, and shader archive; both gauge sections resolve to Ragnarock's stock material instances.

A fresh race confirmed the stock combo sequence on Azure Blockhead:

1. Before the deliberate miss, the handle gauges show blue fill while the head markers stay dark: [frame](Race-v50-before-miss.png).
2. A five-second no-input interval causes a miss and resets both gauge displays: [frame](Race-v50-after-miss-reset.png).
3. After play resumes and combo rebuilds, the handle gauges reach gold and both head markers light: [frame](Race-v50-full-after-reset.png).

The race route deliberately includes missed notes, so it demonstrates blue fill, full combo, and reset rather than an uninterrupted clean streak. A separate full-combo capture shows the same lit head markers and gold handle gauges: [frame](Race-v50-full-combo.png).

## Visual notes

The slightly reduced and offset head markers keep their stock UVs and stock material behavior. Fine masked patterning remains at the marker edges against the custom cyan face, though the smaller marker reduces the overlap. UE 4.27 also reports degenerate tangent-basis warnings during FBX import. These remain visual/import polish items; the stock combo stages and reset work.
