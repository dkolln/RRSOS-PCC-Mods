using BepInEx;
using BepInEx.Configuration;
using RRSOS.PCC.Shared;

namespace RRSOS.PCC.Boosters
{
    /// <summary>
    /// A Boosters screen to place in the world, which builds a booster (machines and their optimizers) at a beacon. A live-write mod, so it does nothing until <c>Enabled</c> is true in its config file.
    /// Work in progress: for now it only writes boosters-spike.txt, a report of what the game has that the build needs (group ids, beacons, optimizer inventories, the Energy screen's own parts).
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.rrsos.pcc.boosters";
        public const string Name = "RRSOS PCC Boosters";
        public const string Version = "0.0.1";

        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<bool> AllowLocked;
        internal static ConfigEntry<string> ExcludeBeacons;
        internal static ConfigEntry<bool> RunPlacementTest;

        private void Awake()
        {
            ModLog.Init("Boosters", Logger);
            Enabled = Config.Bind("General", "Enabled", false, "Turn the Boosters mod on: adds the Boosters screen to the build menu. Building a booster writes into the running game.");

            AllowLocked = Config.Bind("General", "AllowLocked", false, "Offer machines, optimizers and fuses that are not unlocked yet on the Boosters screen (building is free anyway).");

            ExcludeBeacons = Config.Bind("General", "ExcludeBeacons", "Base", "Beacon names that are never offered on the Boosters screen, separated by commas (capitals and spaces around a name do not matter; the whole name must match).");

            RunPlacementTest = Config.Bind("Spike", "RunPlacementTest", false, "Once, when a world loads: place an optimizer, heaters, a spreader, a hive and a farm in front of the player, report what the game made of them (boosters-placement.txt), and remove them again after ten seconds. Set it back to false afterwards.");

            gameObject.AddComponent<Spike>();
            gameObject.AddComponent<PlacementTest>();
            gameObject.AddComponent<BoosterItem>();
            gameObject.AddComponent<BoosterWindow>();
            ModLog.Note($"{Name} {Version} loaded. Enabled: {Enabled.Value}. Log: {ModLog.LogFile}");
        }
    }
}
