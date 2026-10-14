using BepInEx;
using BepInEx.Configuration;
using RRSOS.PCC.Shared;

namespace RRSOS.PCC.Vehicle
{
    /// <summary>
    /// Buttons on the vehicle station's window: spawn a rover, fit its gear at the first or the top tier. A live-write mod, so it does nothing until <c>Enabled</c> is true in its config file.
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.rrsos.pcc.vehicle";
        public const string Name = "RRSOS PCC Vehicle";
        public const string Version = "0.2.0";

        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<string> VehicleGroup;
        internal static ConfigEntry<float> ButtonsX, ButtonsY;

        private void Awake()
        {
            ModLog.Init("Vehicle", Logger);
            Enabled = Config.Bind("General", "Enabled", false, "Show Spawn Rover, Gear Min and Gear Max on the vehicle station's window. Writes into the running game.");
            VehicleGroup = Config.Bind("General", "VehicleGroup", "VehicleTruck", "The game's id for the vehicle (the rover).");
            ButtonsX = Config.Bind("Placement", "ButtonsX", 0f, "Sideways offset of the buttons on the vehicle station window (UI units from the bottom centre).");
            ButtonsY = Config.Bind("Placement", "ButtonsY", 60f, "Height of the buttons above the bottom edge of the vehicle station window (UI units).");

            gameObject.AddComponent<VehicleButtons>();
            ModLog.Note($"{Name} {Version} loaded. Enabled: {Enabled.Value}. Log: {ModLog.LogFile}");
        }
    }
}
