using BepInEx;
using BepInEx.Configuration;
using RRSOS.PCC.Shared;

namespace RRSOS.PCC.AutoFill
{
    /// <summary>
    /// Fills a storage crate's empty slots when it is labelled with an item's name. A live-write mod, so it does nothing until <c>Enabled</c> is true in its config file.
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    [BepInDependency(TerminalRegistration.TerminalGuid, BepInDependency.DependencyFlags.SoftDependency)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.rrsos.pcc.autofill";
        public const string Name = "RRSOS PCC Auto Fill";
        public const string Version = "0.1.0";

        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<string> CrateGroups;
        internal static ConfigEntry<float> SettleSeconds;

        private void Awake()
        {
            ModLog.Init("AutoFill", Logger);
            Enabled = Config.Bind("General", "Enabled", false, "Fill a crate's empty slots when it is labelled with an item's name. Writes into the running game.");
            CrateGroups = Config.Bind("General", "CrateGroups", "Container1,Container2,Container3", "The crates this applies to (the ones that can be labelled).");
            SettleSeconds = Config.Bind("General", "SettleSeconds", 1.0f, "A new label must stay unchanged this long, with the naming window closed, before it counts (the game writes the label while it is typed).");

            var filler = gameObject.AddComponent<AutoFiller>();
            ChatHook.Enabled = () => Enabled.Value;
            ChatHook.Add("/addname", filler.OnAddName, "give an item another name for labels: /addname <item id> <name>");
            gameObject.AddComponent<ChatHook>();
            ModLog.Note($"{Name} {Version} loaded. Enabled: {Enabled.Value}. Log: {ModLog.LogFile}");
        }
    }
}
