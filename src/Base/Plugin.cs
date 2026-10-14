using BepInEx;
using BepInEx.Configuration;
using RRSOS.PCC.Shared;

namespace RRSOS.PCC.Base
{
    /// <summary>
    /// Builds the T0 base in the running game. A live-write mod, so it does nothing until <c>Enabled</c> is true in its config file.
    /// Commands: /build (place it now) and /undo (take back what it placed in this save).
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    [BepInDependency(TerminalRegistration.TerminalGuid, BepInDependency.DependencyFlags.SoftDependency)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.rrsos.pcc.base";
        public const string Name = "RRSOS PCC Base";
        public const string Version = "0.5.0";

        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<bool> UndoLastBuild;
        internal static ConfigEntry<bool> AutoUpgrade;
        internal static ConfigEntry<bool> NuclearToFusion;
        internal static ConfigEntry<bool> PowerButtons;
        internal static ConfigEntry<float> PowerButtonsX, PowerButtonsY;

        /// <summary>Whether a chain's gate is open: the switch in the config that a chain names with gate=Name. An unknown name is open.</summary>
        internal static bool GateOpen(string name)
        {
            if (name == "NuclearToFusion")
                return NuclearToFusion == null || NuclearToFusion.Value;
            return true;
        }

        private void Awake()
        {
            ModLog.Init("Base", Logger);
            Enabled = Config.Bind("General", "Enabled", false, "Build the T0 base when a new world has none of it. Writes into the running game.");
            UndoLastBuild = Config.Bind("General", "UndoLastBuild", false, "Set to true and load the world: removes what this mod placed in that world, then sets itself back to false.");

            AutoUpgrade = Config.Bind("General", "AutoUpgrade", true, "While playing, swap buildings for the better one as it is unlocked, by the chains in upgrades.txt (in this mod's data folder), for example Wind Turbine to Solar Panel to T2 Solar Panel.");

            NuclearToFusion = Config.Bind("Upgrades", "NuclearToFusion", true, "Also swap the T2 Nuclear Reactor for the Fusion Generator once that is unlocked (the last step of the nuclear chain in upgrades.txt, which says gate=NuclearToFusion). When false, reactors stop at T2.");
            PowerButtons = Config.Bind("Power", "Buttons", true, "Show [ ON ] and [ OFF ] buttons on the game's Energy window: they switch the base's energy output on or off. This is the only way to do it; nothing is reported in the chat or on the terminal.");
            PowerButtonsX = Config.Bind("Power", "ButtonsX", 0f, "Sideways offset of the buttons on the Energy window (UI units from the bottom centre).");
            PowerButtonsY = Config.Bind("Power", "ButtonsY", 40f, "Height of the buttons above the bottom edge of the Energy window (UI units).");


            ChatHook.Enabled = () => Enabled.Value;
            ChatHook.Add("/build", BaseBuilder.OnBuildCommand, "place the base now");
            ChatHook.Add("/undo", BaseBuilder.OnUndoCommand, "take back what the base mod placed in this save");

            if (Enabled.Value)
            {
                Boot.Announce(Boot.Base);
                var at = BaseBuilder.Anchor;
                Welcome.Add(2, "A base has been setup at location: [" + at.x + ":" + at.y + ":" + at.z + "] and stocked with most of the items you need to get started.");
            }

            gameObject.AddComponent<BaseBuilder>();
            gameObject.AddComponent<BaseUpgrades>();
            PanelFix.Apply();
            gameObject.AddComponent<BasePower>();
            gameObject.AddComponent<PowerButtons>();
            gameObject.AddComponent<ChatHook>();
            ModLog.Note($"{Name} {Version} loaded. Enabled: {Enabled.Value}. It writes into the running game only when Enabled is true. Log: {ModLog.LogFile}");
        }
    }
}
