# SkyCoop

# Game Version Compatibility

This branch targets **The Long Dark 2.55** on **MelonLoader 0.7.x**, and supports the content of the
*Tales from the Far Territory* DLC in multiplayer.

| Branch | Game Versions | Status |
|--------|--------------|--------|
| this branch | 2.55 | Current port |
| [BeforeTFTFT](https://github.com/Filigrani/SkyCoop/tree/BeforeTFTFT) | 2.01 – 2.02 | Legacy (MelonLoader 0.5.3) |
| [main](https://github.com/Filigrani/SkyCoop/tree/main) | — | Legacy codebase (abandoned) |

For setup help, join the [Discord](https://discord.gg/ydmufU2HHj).

## Tales from the Far Territory

The DLC content is multiplayer aware. The DLC's *challenges* are single player content and are
deliberately left alone.

| Content | How it is shared |
|---------|------------------|
| Cougar | Synced like other wildlife; territory threat is shared per region and merges to the highest level any player has seen |
| Ptarmigan | Synced like other wildlife |
| Travois | Position and whoever is hauling it are streamed; its cargo rides on the normal container sync |
| Trader (Signal Void) | One trust level, stock and delivery for the whole server; every trade action is pushed immediately |
| Noisemakers | Lighting one sets it off on every client, so wildlife reacts for everyone |
| New regions | Forsaken Airfield, Transfer Pass, Zone of Contamination, Sundered Pass and Far Range Branch Line are recognised by name for spawning, weather and expeditions |
| New gear, weather and afflictions | Carried by the existing generic gear and weather sync, including toxic and electrostatic fog |

## Building

The project builds against the Il2Cpp interop assemblies published on NuGet, so **no game
installation is required**:

```
dotnet build -c Release
```

The game version being targeted is the `TLDVersion` property in `SkyCoop.csproj`; bump it when
Hinterland ships a new build. To build against your own installation instead:

```
dotnet build -c Release -p:UseLocalGameAssemblies=true -p:TLDPath="C:\Path\To\TheLongDark"
```

`SkyCoop.dll` goes in the game's `Mods` folder, and `Steamworks.NET.dll` alongside it (MelonLoader
0.6 and newer prefer plain dependencies in `UserLibs`).

Multiplayer for The Long Dark game

We don't own The Long Dark! The Long Dark belong to Hinterland Studio Inc. 
This is a free modification that loads using MelonLoader.

# How to install mod & Play Online?

Everything you need, you can find here: https://discord.gg/ydmufU2HHj

Support project: https://www.patreon.com/Filigrani

(One time donation https://www.paypal.com/paypalme/FiligraniGhost )


# Used stuff:

BuildAssetBundles script for Unity

ModComponent by Original dll by WulfMarius, rework by ds5678 
AssetLoader by Original dll by WulfMarius, rework by ds5678 

Some models used in the mod is free models from site https://sketchfab.com/
Human RIG https://assetstore.unity.com/packages/3d/characters/survival-stylized-characters-5-weapons-115559
Glowing outlines https://assetstore.unity.com/packages/tools/particles-effects/quick-outline-115488

----------------------------------------------------------------------

Co-Creating

Players characters models and all clothing for them by NativeCodeMake https://www.patreon.com/NativeCodeMaker

----------------------------------------------------------------------

Some code references used from:

JumpMod by DigitalzombieTLD https://github.com/DigitalzombieTLD/JumpMod
FoxCompanion by DigitalzombieTLD https://github.com/DigitalzombieTLD/FoxCompanion

Tutorial how to write multiplayer https://www.youtube.com/playlist?list=PLXkn83W0QkfnqsK8I0RAz5AbUxfg3bOQ5

----------------------------------------------------------------------

Used for debugging:

UnityExplorer https://github.com/sinai-dev/UnityExplorer
DeveloperConsole https://github.com/FINDarkside/TLD-Developer-Console

(For custom gears spawns)

Coordinates-Grabber https://github.com/ds5678/Coordinates-Grabber
PlacingAnywhere https://github.com/Xpazeman/tld-placing-anywhere
KeyboardUtilities https://github.com/ds5678/KeyboardUtilities


----------------------------------------------------------------------

Big thanks for The long Dark Modding Discord Community!

Special thanks:
Digitalzombie for explanation how to load custom models, animation and resources works.
ds5678 for help with some problems, and help with ModComponent.
