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

        private void Awake()
        {
            ModLog.Init("Base", Logger);
            Enabled = Config.Bind("General", "Enabled", false, "Build the T0 base when a new world has none of it. Writes into the running game.");
            UndoLastBuild = Config.Bind("General", "UndoLastBuild", false, "Set to true and load the world: removes what this mod placed in that world, then sets itself back to false.");

            ChatHook.Enabled = () => Enabled.Value;
            ChatHook.Add("/build", BaseBuilder.OnBuildCommand, "place the base now");
            ChatHook.Add("/undo", BaseBuilder.OnUndoCommand, "take back what the base mod placed in this save");

            gameObject.AddComponent<BaseBuilder>();
            gameObject.AddComponent<ChatHook>();
            ModLog.Note($"{Name} {Version} loaded. Enabled: {Enabled.Value}. It writes into the running game only when Enabled is true. Log: {ModLog.LogFile}");
        }
    }
}
