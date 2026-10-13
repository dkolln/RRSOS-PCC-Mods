using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using RRSOS.PCC.Shared;
using SpaceCraft;
using UnityEngine;

namespace RRSOS.PCC.Blueprints
{
    /// <summary>
    /// Unlocks the blueprints of a new game that the game otherwise unlocks through blueprint chips. A game is new when the player has crafted nothing yet, so a game already under way
    /// is never touched. The list is blueprints.txt in the mod's data folder (one group id per line, made from a save: the groups its unlockedGroups held, which is exactly what
    /// chips unlock; what unlocks by terraformation is not in it). Each one goes through the game's own UnlockedGroupsHandler.UnlockGroupGlobally, without a popup.
    /// </summary>
    internal sealed class PlanetfallBlueprints : MonoBehaviour
    {
        private const int PerFrame = 4;

        private const string DefaultList = @"# Blueprints unlocked at the start of a new game: the ones that otherwise need a blueprint chip. One group id per line; # starts a note.
# Made from a save's unlockedGroups. Delete a line to leave that blueprint locked; an id the game does not know is skipped.
MultiToolMineSpeed1
MultiToolMineSpeed2
MultiToolMineSpeed3
MultiToolMineSpeed4
MultiToolLight2
MultiToolLight3
MultiToolDeconstruct2
MultiToolDeconstruct3
BootsSpeed1
BootsSpeed2
BootsSpeed3
Jetpack2
Jetpack3
HudCompass
HudChipCleanConstruction
MapChip
PinChip2
PinChip3
podAngle
Pod4x
RecyclingMachine
InsideLamp1
ScreenMap1
Destructor1
Fence
DisplayCase
SkinDisplayer
WallInside
Flare
Explosive
RocketMap1
RocketMap2
RocketMap3
RocketMap4
RocketInformations1
RocketInformations2
RocketDrones1
VehicleEquipmentSize2
VehicleOxygen1
VehicleInventorySize2
VehicleInventorySize3
VehicleSpeed2
VehicleSpeed3
VehicleLights2
";

        private static string ListFile => ModLog.FileIn("blueprints.txt");
        private static string StoryFile => ModLog.FileIn("story-blueprints.txt");

        private const string DefaultStoryList = @"# Blueprints the game hides in the story and the wrecks (treadmill, fridge, ...). Unlocked at the start of a new game when UnlockStoryBlueprints is true in the config.
# One group id per line; # starts a note. Delete a line to leave that blueprint locked; an id the game does not know is skipped.
SofaAngleColored
Fireplace
Chair2
Counter1
Counter2
Desktop2
ExerciseBike1
FlowerPot2
FlowerPot3
TreePlanter2
Ivy1
Fridge1
Library1
PlanetViewer1
Pooltable1
Shelves1
TableSmall2
TableSmall3
Trashcan1
Treadmill1
Vault1
Server1
Table1
Faucet1
Shower1
Locker1
Toilet1
ArcadeMachine1
AquariumTube1
LavaLamp1
VendingMachine1
";

        private string _handled;

        private IEnumerator Start()
        {
            while (true)
            {
                yield return new WaitForSecondsRealtime(2f);

                if (!Plugin.Enabled.Value)
                    continue;

                if (!WorldState.Ready(out _, out var key) || key == _handled)
                    continue;

                // Once per world loaded; a new game is one where the player has crafted nothing yet, judged from the world itself.
                _handled = key;
                Boot.NewWorld(key);

                var player = Managers.GetManager<PlayersManager>().GetActivePlayerController();
                var crafted = WorldObjectsHandler.Instance.GetTotalCraftedObjects(player.OwnerClientId);
                if (crafted != 0)
                {
                    ModLog.Note("Save " + key + ": not a new game (" + crafted + " objects crafted); no blueprints are unlocked.");
                    Boot.Skip(Boot.Blueprints);
                    continue;
                }

                var todo = Pending();
                if (todo.Count == 0)
                {
                    ModLog.Note("Save " + key + ": every blueprint on the list is already unlocked.");
                    Boot.Skip(Boot.Blueprints);
                    continue;
                }

                ModLog.Note("Save " + key + ": a new game; unlocking " + todo.Count + " blueprints.");
                yield return Boot.Step(Boot.Blueprints, "Accessing Blueprints......", Unlock(todo));
            }
        }

        private static List<Group> Pending()
        {
            var result = new List<Group>();
            Collect(ListFile, DefaultList, result);
            if (Plugin.UnlockStoryBlueprints.Value)
                Collect(StoryFile, DefaultStoryList, result);
            return result;
        }

        // The groups a list file names that are not unlocked yet, added to the result. The file is written with its default the first time.
        private static void Collect(string file, string defaultList, List<Group> result)
        {
            if (!File.Exists(file))
                File.WriteAllText(file, defaultList.Replace("\r\n", "\n").Replace("\n", Environment.NewLine));

            var handler = UnlockedGroupsHandler.Instance;
            foreach (var raw in File.ReadAllLines(file))
            {
                var id = raw.Trim();
                if (id.Length == 0 || id.StartsWith("#"))
                    continue;

                var group = GroupsHandler.GetGroupViaId(id);
                if (group == null)
                {
                    ModLog.Note(Path.GetFileName(file) + ": the game has no group " + id + "; skipped.");
                    continue;
                }

                if (!handler.IsGloballyUnlocked(group) && !result.Contains(group))
                    result.Add(group);
            }
        }

        private static IEnumerator Unlock(List<Group> groups)
        {
            var handler = UnlockedGroupsHandler.Instance;
            var done = 0;
            var tried = 0;
            var failed = new List<string>();
            foreach (var group in groups)
            {
                try
                {
                    handler.UnlockGroupGlobally(group, false, false);
                    if (handler.IsGloballyUnlocked(group))
                        done++;
                    else
                        failed.Add(group.GetId());
                }
                catch (Exception e)
                {
                    failed.Add(group.GetId());
                    ModLog.Note("Blueprints: could not unlock " + group.GetId() + ": " + e.GetType().Name + ": " + e.Message);
                }

                if (++tried % PerFrame == 0)
                    yield return null;
            }

            ModLog.Note("Blueprints: " + done + " of " + groups.Count + " unlocked." + (failed.Count > 0 ? " Not unlocked: " + string.Join(", ", failed.ToArray()) + "." : ""));
        }
    }
}
