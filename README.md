# Trade System Mod

In the `Trade.ini` file, you can freely add items for buying and selling.

### Icon Configuration
Currently, icons are loaded from:
```cpp
$TradeCfg::IconRoot = "mods/client/Trade/icon/";

This means the mod looks for icon files inside the mods/client/Trade/icon/ folder.
You can replace or add your own icons there — just make sure the filenames match what’s defined in the Trade.ini.

NPC Configuration
In the Trigger_gui.cs file, the admin can change the .dts model of the NPC that triggers the trade window.
To do this, edit the following line:

_targetShape = "assel.dts";
You can replace "assel.dts" with any other .dts model you want to use as the trader NPC.

Modding & Editing
You’re free to modify, edit, expand, and use this mod however you like.
Everything is open for the community — feel free to build on top of it!
