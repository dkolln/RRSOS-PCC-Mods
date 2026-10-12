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
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.rrsos.pcc.base";
        public const string Name = "RRSOS PCC Base";
        public const string Version = "0.2.0";

        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<bool> UndoLastBuild;
        internal static ConfigEntry<bool> AutoUpgrade;
        internal static ConfigEntry<string> UpgradeScope;

        private void Awake()
        {
            ModLog.Init("Base", Logger);
            Enabled = Config.Bind("General", "Enabled", false, "Build the T0 base when a new world has none of it. Writes into the running game.");
            UndoLastBuild = Config.Bind("General", "UndoLastBuild", false, "Set to true and load the world: removes what this mod placed in that world, then sets itself back to false.");

            AutoUpgrade = Config.Bind("General", "AutoUpgrade", true, "While playing, swap buildings for the better one as it is unlocked, by the chains in upgrades.txt (in this mod's data folder), for example Wind Turbine to Solar Panel to T2 Solar Panel.");
            UpgradeScope = Config.Bind("General", "UpgradeScope", "World", new ConfigDescription("Which buildings are swapped: World = every building of that kind on the planet, Base = only the pieces this mod placed.", new AcceptableValueList<string>("World", "Base")));

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
            gameObject.AddComponent<ChatHook>();
            ModLog.Note($"{Name} {Version} loaded. Enabled: {Enabled.Value}. It writes into the running game only when Enabled is true. Log: {ModLog.LogFile}");
        }
    }
}
