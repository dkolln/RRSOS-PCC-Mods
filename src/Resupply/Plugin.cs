using BepInEx;
using BepInEx.Configuration;
using RRSOS.PCC.Shared;

namespace RRSOS.PCC.Resupply
{
    /// <summary>
    /// Everything about storage crates and what is in them: a resupply icon on a crate's window, the fill when a crate is labelled, the Supply Manager screen (drone demand and supply, resupply, auto top-up), and /resupply add for item names. A live-write mod, so it does nothing until
    /// <c>Enabled</c> is true in its config file.
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    [BepInDependency(TerminalRegistration.TerminalGuid, BepInDependency.DependencyFlags.SoftDependency)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.rrsos.pcc.resupply";
        public const string Name = "RRSOS PCC Resupply";
        public const string Version = "0.2.0";

        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<string> Groups, DroneStationGroup;
        internal static ConfigEntry<float> NudgeX, NudgeY;
        internal static ConfigEntry<bool> AutoTopUp, FillOnRename;
        internal static ConfigEntry<int> TopUpBelowPercent;
        internal static ConfigEntry<float> TopUpSeconds, SettleSeconds;

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

            FillOnRename = Config.Bind("Fill On Rename", "FillOnRename", true, "When a crate is given a label that names an item (an id, its name in the game, or a name from aliases.txt), fill its empty slots with that item, once. What is in the crate already stays.");
            SettleSeconds = Config.Bind("Fill On Rename", "SettleSeconds", 1.0f, "A new label must stay unchanged this long, with the naming window closed, before it counts (the game writes the label while it is typed).");

            gameObject.AddComponent<Resupplier>();
            var rename = gameObject.AddComponent<RenameFill>();
            ChatHook.Enabled = () => Enabled.Value;
            ChatHook.Add("/resupply", rename.OnResupplyCommand, "fill every crate with its item, or give an item another name: /resupply add <item id> <name>");
            gameObject.AddComponent<ChatHook>();
            gameObject.AddComponent<SupplyItem>();
            gameObject.AddComponent<SupplyWindow>();
            ModLog.Note($"{Name} {Version} loaded. Enabled: {Enabled.Value}. It writes into the running game only when Enabled is true. Log: {ModLog.LogFile}");
        }
    }
}
