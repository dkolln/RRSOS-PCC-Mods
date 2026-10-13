using BepInEx;
using BepInEx.Configuration;
using RRSOS.PCC.Shared;

namespace RRSOS.PCC.Trash
{
    /// <summary>
    /// A trash can on the window of a storage container: one click deletes everything in that container for good. A live-write mod, so it does nothing until <c>Enabled</c> is true in its config file.
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.rrsos.pcc.trash";
        public const string Name = "RRSOS PCC Trash";
        public const string Version = "0.1.0";

        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<string> Groups;
        internal static ConfigEntry<float> ButtonX, ButtonY;

        private void Awake()
        {
            ModLog.Init("Trash", Logger);
            Enabled = Config.Bind("General", "Enabled", false, "Show the trash can on container windows. One click deletes everything in that container, for good, with no question asked.");
            Groups = Config.Bind("General", "Groups", "Container1,Container2,Container3,Fridge1,Vault1,Locker1", "The containers that get the trash can.");
            ButtonX = Config.Bind("Placement", "ButtonX", 8f, "Gap between the container's grid and the trash can (UI units, to the right).");
            ButtonY = Config.Bind("Placement", "ButtonY", 0f, "How far the trash can hangs below the top of the grid (UI units).");

            gameObject.AddComponent<TrashButton>();
            ModLog.Note($"{Name} {Version} loaded. Enabled: {Enabled.Value}. It writes into the running game only when Enabled is true. Log: {ModLog.LogFile}");
        }
    }
}
