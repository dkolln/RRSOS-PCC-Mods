using BepInEx;
using BepInEx.Configuration;
using RRSOS.PCC.Shared;

namespace RRSOS.PCC.ModManager
{
    /// <summary>
    /// A "Manage Mods" button on the main menu, above Quit, opening a screen with every loaded mod and its config options. Work in progress: for now it only writes what it finds
    /// (menu-structure.txt and plugins.txt in its data folder) and adds the button.
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.rrsos.pcc.modmanager";
        public const string Name = "RRSOS PCC Mod Manager";
        public const string Version = "0.0.1";

        internal static ConfigEntry<bool> Enabled;

        private void Awake()
        {
            ModLog.Init("ModManager", Logger);
            Enabled = Config.Bind("General", "Enabled", true, "Add the Manage Mods button to the main menu.");

            gameObject.AddComponent<MenuSpike>();
            ModLog.Note($"{Name} {Version} loaded. Enabled: {Enabled.Value}. Log: {ModLog.LogFile}");
        }
    }
}
