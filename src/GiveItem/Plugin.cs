using BepInEx;
using BepInEx.Configuration;
using RRSOS.PCC.Shared;

namespace RRSOS.PCC.GiveItem
{
    /// <summary>
    /// /giveitem in the game's chat: puts an item into the player's backpack. A live-write mod, so it does nothing until <c>Enabled</c> is true in its config file.
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.rrsos.pcc.giveitem";
        public const string Name = "RRSOS PCC Give Item";
        public const string Version = "0.2.0";

        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<int> MaxQuantity;

        private void Awake()
        {
            ModLog.Init("GiveItem", Logger);
            Enabled = Config.Bind("General", "Enabled", false, "Add the chat command /giveitem <item> [quantity] (and /giveitem add <item id> <name> for another name): puts an item into your backpack. Writes into the running game.");
            MaxQuantity = Config.Bind("General", "MaxQuantity", 500, "The most one /giveitem may ask for (the backpack's room is the real limit).");

            var command = gameObject.AddComponent<GiveItemCommand>();
            ChatHook.Enabled = () => Enabled.Value;
            ChatHook.Add("/giveitem", command.OnCommand, "put an item in your backpack: /giveitem <item> [quantity] (and /giveitem add <item id> <name> for another name)");
            gameObject.AddComponent<ChatHook>();
            ModLog.Note($"{Name} {Version} loaded. Enabled: {Enabled.Value}. Log: {ModLog.LogFile}");
        }
    }
}
