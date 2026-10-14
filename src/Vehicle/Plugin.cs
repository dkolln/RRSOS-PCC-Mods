using BepInEx;
using BepInEx.Configuration;
using RRSOS.PCC.Shared;

namespace RRSOS.PCC.Vehicle
{
    /// <summary>
    /// /vehicle on the terminal: spawns a rover and fits its gear at the first or the top tier. A live-write mod, so it does nothing until <c>Enabled</c> is true in its config file.
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    [BepInDependency(TerminalRegistration.TerminalGuid, BepInDependency.DependencyFlags.SoftDependency)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.rrsos.pcc.vehicle";
        public const string Name = "RRSOS PCC Vehicle";
        public const string Version = "0.1.0";

        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<string> VehicleGroup;
        internal static ConfigEntry<float> SpawnDistance;

        private void Awake()
        {
            ModLog.Init("Vehicle", Logger);
            Enabled = Config.Bind("General", "Enabled", false, "Add /vehicle to the terminal: spawns a rover and fits its gear. Writes into the running game.");
            VehicleGroup = Config.Bind("General", "VehicleGroup", "VehicleTruck", "The game's id for the vehicle (the rover).");
            SpawnDistance = Config.Bind("General", "SpawnDistance", 8f, "How far in front of the player a spawned rover appears, in metres.");

            gameObject.AddComponent<VehicleCommand>();
            ModLog.Note($"{Name} {Version} loaded. Enabled: {Enabled.Value}. Log: {ModLog.LogFile}");
        }
    }
}
