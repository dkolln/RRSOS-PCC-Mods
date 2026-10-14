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
        internal static ConfigEntry<string> StationGroup, SpawnOffset;

        private void Awake()
        {
            ModLog.Init("Vehicle", Logger);
            Enabled = Config.Bind("General", "Enabled", false, "Add /vehicle to the terminal: spawns a rover and fits its gear. Writes into the running game.");
            VehicleGroup = Config.Bind("General", "VehicleGroup", "VehicleTruck", "The game's id for the vehicle (the rover).");
            StationGroup = Config.Bind("General", "StationGroup", "VehicleCrafter1", "The game's id for the vehicle station. /vehicle spawn needs one built, and spawns at the nearest.");
            SpawnOffset = Config.Bind("General", "SpawnOffset", "0.05,5.45,-1.06", "Where the rover appears, relative to the station (x,y,z in metres, in the station's own directions). The default is where the game keeps a rover on its station.");

            gameObject.AddComponent<VehicleCommand>();
            ModLog.Note($"{Name} {Version} loaded. Enabled: {Enabled.Value}. Log: {ModLog.LogFile}");
        }
    }
}
