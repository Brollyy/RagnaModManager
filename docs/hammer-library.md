# Hammer Library

Use the Hammer Library to add custom hammer packages to Ragnarock.

## Add a hammer

1. Choose **Add a hammer** and select an `.rhammer` file.
2. Enable the hammers you want to install.
3. Choose **Build and add to Ragnarock**.
4. Open the game's changing room and select the new hammer.

To remove custom hammers from the game, disable them all and choose **Remove from Ragnarock**. Your `.rhammer` files stay in the library unless you remove them there.

## Before building

RMM checks the installed game version and uses matching hammer data to preserve the game's existing hammer list. When the exact game build is not listed, RMM warns that it will use the newest available compatibility data; DLC hammers added after that data was published may not appear. Review that warning before continuing.

The Hammer Library installs cooked packages. It does not convert Blender/FBX/OBJ source files or compile Unreal assets. Authors should follow [Create a Ragnarock hammer with UE 4.27](custom-hammer-authoring-ue4-27.md). For archive fields and package paths, see the [`.rhammer` format reference](hammer-package-format.md).
