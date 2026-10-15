using BepInEx;
using BepInEx.Configuration;
using RRSOS.PCC.Shared;

namespace RRSOS.PCC.Boosters
{
    /// <summary>
    /// A Boosters screen to place in the world, which builds a booster (machines and their optimizers) at a beacon. A live-write mod, so it does nothing until <c>Enabled</c> is true in its config file.
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    [BepInDependency(TerminalRegistration.TerminalGuid, BepInDependency.DependencyFlags.SoftDependency)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.rrsos.pcc.boosters";
        public const string Name = "RRSOS PCC Boosters";
        public const string Version = "0.1.0";

        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<bool> AllowLocked;
        internal static ConfigEntry<string> ExcludeBeacons;

        private void Awake()
        {
            ModLog.Init("Boosters", Logger);
            Enabled = Config.Bind("General", "Enabled", false, "Turn the Boosters mod on: adds the Boosters screen to the build menu. Building a booster writes into the running game.");

            AllowLocked = Config.Bind("General", "AllowLocked", false, "Offer machines, optimizers and fuses that are not unlocked yet on the Boosters screen (building is free anyway).");

            ExcludeBeacons = Config.Bind("General", "ExcludeBeacons", "Base", "Beacon names that are never offered on the Boosters screen, separated by commas (capitals and spaces around a name do not matter; the whole name must match).");

            BoosterKinds.BindFillSettings(Config);

            ChatHook.Enabled = () => Enabled.Value;
            ChatHook.Add("/teleport", TeleportBeacon.OnCommand, "build a teleporter at each beacon named teleport");
            gameObject.AddComponent<ChatHook>();

            gameObject.AddComponent<BoosterItem>();
            gameObject.AddComponent<BoosterWindow>();
            ModLog.Note($"{Name} {Version} loaded. Enabled: {Enabled.Value}. Log: {ModLog.LogFile}");
        }
    }
}
