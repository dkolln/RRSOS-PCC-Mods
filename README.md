# RRSOS-PCC-Mods

*Red Rocket Solutions Operating System - Planet Crafter Companion - Mods*

Live-write mods for [The Planet Crafter](https://store.steampowered.com/app/1284190/The_Planet_Crafter/), as BepInEx 5 plugins. They place and change things in the **running** game. This repository is separate from the read-only dashboard ([RRSOS_PCC_Live](https://github.com/dkolln/RRSOS_PCC_Live)), which only observes the game and never writes to it.

The game has no official mod support or loader; the developers point players at BepInEx, at their own risk. These plugins are community mods in that sense.

## What is here

One plugin so far, `RRSOS.PCC.Build` (`src/Build`), which will be split into separate mods in this repository:

| Feature | What it does |
|---|---|
| **Base placement** | At planetfall, or when asked, places stage 1 of the main base (the anchor lamp on its foundation, one more foundation, one pod) at the Prime template location. Once per save, and only on an empty site. |
| **Chat commands** | Added to the game's own chat: `/build` (place the base now), `/undo` (take back what the last command placed), `/loadout` (fit the top tier of every piece of personal gear; a second call only does the difference). `/help` lists them. |
| **Stacked crate** | A build-menu item next to the storage crate: 3 Iron, places three Container1 on top of each other. The save only ever holds ordinary Container1 pieces. |

## Principles

- **Live writes, deliberately.** These mods change the running game. They are off until `Enabled = true` in `BepInEx\config\com.rrsos.pcc.build.cfg`, and each feature has its own switch.
- **The game's own paths.** Pieces are brought to life the way the game loads a saved piece (a world object with its position, panels and colour, then `InstantiateWorldObject`), so the game saves them itself.
- **Logged and undoable.** Every placement is written to `build-log.txt`; `/undo` and `build-undo-now.txt` remove what the plugin placed.
- **Host only.** Nothing runs for a client in someone else's world.
- **Nothing of the game is redistributed.** The project references the game's assemblies in place, from your Steam folder, and takes BepInEx from NuGet. Game files and the game's decompiled source are never copied into this repository.
- **Fragile by nature.** Some features use the game's private members (the chat command list, the group lists). A game update can break them; each failure is logged and the plugin keeps going without that feature.

## Setup

1. Install the .NET 10 SDK and the game (Steam). Install BepInEx 5.4 (win x64) into the game folder and run the game once.
2. `powershell -ExecutionPolicy Bypass -File tools\setup.ps1` finds the game in any Steam library and writes `solution_private.targets`.
3. Close the game, then `dotnet build src/Build -c Release`. The DLL is copied to `BepInEx\plugins\RRSOS-PCC-Build\`. Start the game fully (a running game does not pick up a changed DLL).
4. Set `Enabled = true` in `BepInEx\config\com.rrsos.pcc.build.cfg` (created on the first start).

To check a build without touching the game folder: `dotnet build src/Build -c Release -p:PluginsFolder=<scratch folder>\`.

## Files the plugin writes

In `%LOCALAPPDATA%\RRSOS-PCC-Live` (shared with the dashboard for now): `build-log.txt`, `build-done.txt` (which saves were handled), `build-undo.txt` (what was placed), and the request files `build-now.txt` and `build-undo-now.txt`, which act within two seconds while a world is loaded and are then deleted.

## Status

First working version, tested by hand in the game. Planned: split into separate mods, a per-mod description file, an anchor per planet instead of the hard-coded Prime location, and a dashboard "command center" to install and switch mods.

## License

MIT, see [LICENSE](LICENSE).
