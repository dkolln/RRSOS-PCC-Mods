using BepInEx;
using BepInEx.Configuration;
using RRSOS.PCC.Shared;

namespace RRSOS.PCC.Trash
{
    /// <summary>
    /// A trash can on the window of a storage container, and optionally on the personal inventory: one click deletes everything in that container (or the backpack) for good. A live-write mod, so it does nothing until <c>Enabled</c> is true in its config file.
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.rrsos.pcc.trash";
        public const string Name = "RRSOS PCC Trash";
        public const string Version = "0.1.0";

        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<string> Groups;
        internal static ConfigEntry<bool> OnPerson;
        internal static ConfigEntry<bool> LockItems;
        internal static ConfigEntry<string> LockClick;
        internal static ConfigEntry<float> NudgeX, NudgeY;

        private void Awake()
        {
            ModLog.Init("Trash", Logger);
            Enabled = Config.Bind("General", "Enabled", false, "Show the trash can on container windows. One click deletes everything in that container, for good, with no question asked.");
            Groups = Config.Bind("General", "Groups", "Container1,Container2,Container3,Fridge1,Vault1,Locker1", "The containers that get the trash can.");
            OnPerson = Config.Bind("General", "TrashOnPerson", true, "Also show the trash can on your personal inventory window. One click deletes everything in your backpack (never the equipment), for good, with no question asked.");
            LockItems = Config.Bind("Locks", "LockItems", true, "Let a click on an item in an inventory window lock it: a locked item shows a lock, is skipped by the trash can, and stays when Move All moves the rest. Click it again to unlock.");
            LockClick = Config.Bind("Locks", "LockClick", "Middle", new ConfigDescription("Which mouse button locks and unlocks an item. Middle leaves the right button to the game, which uses it to eat, drink or place an item.", new AcceptableValueList<string>("Middle", "Right")));
            NudgeX = Config.Bind("Placement", "NudgeX", 0f, "Fine adjustment of the trash can, sideways from its default spot above the sort icon (UI units, positive is right).");
            NudgeY = Config.Bind("Placement", "NudgeY", 0f, "Fine adjustment of the trash can, up from its default spot above the sort icon (UI units, positive is up).");

            try
            {
                LockPatches.Apply();
            }
            catch (System.Exception e)
            {
                ModLog.Note("Could not hook the inventory windows for locks: " + e.GetType().Name + ": " + e.Message);
            }

            gameObject.AddComponent<TrashButton>();
            ModLog.Note($"{Name} {Version} loaded. Enabled: {Enabled.Value}. It writes into the running game only when Enabled is true. Log: {ModLog.LogFile}");
        }
    }
}
