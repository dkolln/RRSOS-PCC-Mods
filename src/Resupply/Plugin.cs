using BepInEx;
using BepInEx.Configuration;
using RRSOS.PCC.Shared;

namespace RRSOS.PCC.Resupply
{
    /// <summary>
    /// A resupply icon on a storage crate's window: one click fills that crate's empty slots with its item (its label, or else its demand). A live-write mod, so it does nothing until
    /// <c>Enabled</c> is true in its config file.
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.rrsos.pcc.resupply";
        public const string Name = "RRSOS PCC Resupply";
        public const string Version = "0.2.0";

        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<string> Groups, DroneStationGroup;
        internal static ConfigEntry<float> NudgeX, NudgeY;
        internal static ConfigEntry<bool> AutoTopUp;
        internal static ConfigEntry<int> TopUpBelowPercent;
        internal static ConfigEntry<float> TopUpSeconds;

        private void Awake()
        {
            ModLog.Init("Resupply", Logger);
            Enabled = Config.Bind("General", "Enabled", false, "Show the resupply icon on crate windows and the Supply Manager screen in the build menu. Writes into the running game.");
            Groups = Config.Bind("General", "Groups", "Container1,Container2,Container3", "The crates that get the resupply icon (and that Resupply and the auto top-up fill).");
            DroneStationGroup = Config.Bind("General", "DroneStationGroup", "DroneStation1", "The game's id for the drone station. Set Drone Demand and Set Drone Supply on the Supply Manager screen can be used once it is unlocked.");
            NudgeX = Config.Bind("Placement", "NudgeX", 0f, "Fine adjustment of the icon, sideways from its default spot (UI units, positive is right).");
            NudgeY = Config.Bind("Placement", "NudgeY", 0f, "Fine adjustment of the icon, up from its default spot (UI units, positive is up).");

            AutoTopUp = Config.Bind("Auto Top-Up", "AutoTopUp", false, "Every TopUpSeconds, top up the crates that have an item (by label or category) and are less than TopUpBelowPercent full. It is free stock, so it is off unless you switch it on (also on the Supply Manager screen).");
            TopUpBelowPercent = Config.Bind("Auto Top-Up", "TopUpBelowPercent", 50, new ConfigDescription("A crate is topped up by the auto top-up when it is less full than this (percent of its slots).", new AcceptableValueRange<int>(1, 99)));
            TopUpSeconds = Config.Bind("Auto Top-Up", "TopUpSeconds", 30f, new ConfigDescription("How often the auto top-up looks at the crates (seconds, at least 5).", new AcceptableValueRange<float>(5f, 600f)));

            gameObject.AddComponent<Resupplier>();
            gameObject.AddComponent<SupplyItem>();
            gameObject.AddComponent<SupplyWindow>();
            ModLog.Note($"{Name} {Version} loaded. Enabled: {Enabled.Value}. It writes into the running game only when Enabled is true. Log: {ModLog.LogFile}");
        }
    }
}
