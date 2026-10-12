# RRSOS-PCC-Mods

*Red Rocket Solutions Operating System - Planet Crafter Companion - Mods*

Live-write mods for [The Planet Crafter](https://store.steampowered.com/app/1284190/The_Planet_Crafter/), as BepInEx 5 plugins. They place and change things in the **running** game. This repository is separate from the read-only dashboard ([RRSOS_PCC_Live](https://github.com/dkolln/RRSOS_PCC_Live)), which only observes the game and never writes to it.

The game has no official mod support or loader; the developers point players at BepInEx, at their own risk. These plugins are community mods in that sense.

## The mods

Each mod is its own project, builds to its own DLL, and installs into its own folder under `BepInEx\plugins`. None depends on another.

| Mod | Folder in `plugins` | What it does |
|---|---|---|
| **Base** (`src/Base`) | `RRSOS-PCC-Base` | At planetfall, or on request, places stage 1 of the main base (the anchor lamp on its foundation, one more foundation, one pod) at the Prime template location. Once per save, and only on an empty site. Chat: `/build` places it now, `/undo` takes back what it placed in this save. |
| **Loadout** (`src/Loadout`) | `RRSOS-PCC-Loadout` | Chat: `/loadout` fits the top tier of every piece of personal gear through the game's own equip steps. A second call only adds what is missing and swaps a lower tier for its top tier. |
| **Auto fill** (`src/AutoFill`) | `RRSOS-PCC-AutoFill` | Label a storage crate (Container1, 2 or 3) with an item's name and its **empty** slots are filled with that item. Only at a rename; what is in the crate stays. The label must equal a whole name, ignoring case, spaces and punctuation: the item id, its name in the game, an extra name from the built-in table (for example `O2` for the oxygen capsule), or a name you add: in the game's chat `/addname <item id> <name>` (for example `/addname waterbottle1 water`; it answers `Name: water added.` or says the name already exists), or by editing `%LOCALAPPDATA%\RRSOS-PCC-Mods\aliases.txt` (`item id = name, name, ...`; yours win). A name that two items share is not guessed at. If the Commands mod's terminal is installed, this mod also puts **`/resupply`** on it: every labelled crate on the planet is topped up the same way (only empty slots). |
| **Commands** (`src/Commands`) | `RRSOS-PCC-Commands` | A **Terminal** in the build menu (1 Iron, 1 Silicon, 1 Titanium, 1 Cobalt). Interact with it, type at the `C:\>` prompt, and the other mods' commands run and answer on the screen. The terminal knows nothing about other mods: any installed mod can register commands with it (see `TerminalApi`), and `/help` lists what has registered; `/cls` clears the screen. A placed terminal is saved in the world; without this mod the game drops it at the next load. |
| **Stacked crate** (`src/StackedCrate`) | `RRSOS-PCC-StackedCrate` | A build-menu item next to the storage crate: 3 Iron, places three Container1 on top of each other. The save only ever holds ordinary Container1 pieces. |

**Adding a command to the terminal.** A mod with terminal commands looks the terminal up among the loaded plugins and registers each command by reflection, using only basic types (`src/Shared/TerminalRegistration.cs`), with the terminal declared as a soft dependency. No assembly is shared, and the mod works the same without a terminal.

Icons the mods draw themselves are kept in `%LOCALAPPDATA%\RRSOS-PCC-Mods\CustomIcons\<group id>.png`, apart from the game's own icons. A file already there is used instead of a freshly drawn one, so an icon can be replaced by dropping a PNG in; delete it to have it drawn again.

Every mod folder holds the DLL and a `mod.json` that describes the mod (id, version, commands, config file, data folder), so a tool can list and manage them.

Shared source lives in `src/Shared` (log, world state, placing a piece, the chat hook). Each mod compiles the files it needs into its own DLL, so a mod can be installed alone.

## Principles

- **Live writes, deliberately.** These mods change the running game. Each is off until `Enabled = true` in its own config file (`BepInEx\config\com.rrsos.pcc.<mod>.cfg`, created on the first start).
- **The game's own paths.** Pieces are brought to life the way the game loads a saved piece (a world object with its position, panels and colour, then `InstantiateWorldObject`), so the game saves them itself.
- **Logged and undoable.** Each mod writes `log.txt` in `%LOCALAPPDATA%\RRSOS-PCC-Mods\<Mod>\`. The Base mod keeps what it placed so `/undo` can remove exactly that.
- **Host only.** Nothing runs for a client in someone else's world.
- **Nothing of the game is redistributed.** The projects reference the game's assemblies in place, from your Steam folder, and take BepInEx from NuGet. Game files and the game's decompiled source are never copied into this repository.
- **Fragile by nature.** Some features use the game's private members (the chat command list, the group lists). A game update can break them; each failure is logged and the mod keeps going without that feature.

## Setup

1. Install the .NET 10 SDK and the game (Steam). Install BepInEx 5.4 (win x64) into the game folder and run the game once.
2. `powershell -ExecutionPolicy Bypass -File tools\setup.ps1` finds the game in any Steam library and writes `solution_private.targets`.
3. Close the game, then `dotnet build RRSOS.PCC.Mods.slnx -c Release`. Each DLL and its `mod.json` are copied to `BepInEx\plugins\<folder>\`. Start the game fully (a running game does not pick up a changed DLL).
4. Set `Enabled = true` in the config file of each mod you want.

To check a build without touching the game folder: `dotnet build RRSOS.PCC.Mods.slnx -c Release -p:PluginsFolder=<scratch folder>\`. To build one mod: `dotnet build src/Base -c Release`.

## Base mod request files

While a world is loaded, drop `build-now.txt` or `undo-now.txt` into `%LOCALAPPDATA%\RRSOS-PCC-Mods\Base\`; the mod acts within two seconds and deletes the file. The folder also holds `done.txt` (which saves were handled) and `undo.txt` (what was placed).

## Status

First working versions, tested by hand in the game. Planned: an anchor per planet instead of the hard-coded Prime location, more stages of the base, and a dashboard "command center" to install and switch mods.

## License

MIT, see [LICENSE](LICENSE).
