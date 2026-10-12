using BepInEx;
using BepInEx.Configuration;
using RRSOS.PCC.Shared;

namespace RRSOS.PCC.Commands
{
    /// <summary>
    /// A Terminal you can build: a screen with a prompt where you type the other RRSOS mods' commands, with /help listing only what is installed and active.
    /// A live-write mod, so it does nothing until <c>Enabled</c> is true in its config file.
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.rrsos.pcc.commands";
        public const string Name = "RRSOS PCC Commands";
        public const string Version = "0.1.0";

        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<float> Scale;
        internal static ConfigEntry<int> InputLimit;
        internal static ConfigEntry<float> TopMargin;
        internal static ConfigEntry<float> LeftMargin;
        internal static ConfigEntry<float> FontSize;

        private void Awake()
        {
            ModLog.Init("Commands", Logger);
            Enabled = Config.Bind("General", "Enabled", false, "Add the Terminal to the build menu (1 Iron, 1 Silicon, 1 Titanium, 1 Cobalt). Writes into the running game.");
            Scale = Config.Bind("General", "Scale", 2.5f, "How much larger than a sign the terminal's screen is (width and height; it stays as thin as a sign). Takes effect when the game starts.");
            InputLimit = Config.Bind("Screen", "InputLimit", 100, new ConfigDescription("How many characters can be typed at the terminal (the game's text window allows far fewer by default; the most a terminal can hold is about 120).", new AcceptableValueRange<int>(10, 120)));
            TopMargin = Config.Bind("Screen", "TopMargin", 8f, "Space above the first line of text, in text units (the whole text area is 75 high). Raise it to push the text down. Takes effect when the game starts.");
            LeftMargin = Config.Bind("Screen", "LeftMargin", 7f, "Space left of the text, in text units (the text area is 200 wide). Takes effect when the game starts.");
            FontSize = Config.Bind("Screen", "FontSize", 3.5f, new ConfigDescription("Size of the text on the screen (the sign's own lettering is 7; smaller fits more lines). Takes effect when the game starts.", new AcceptableValueRange<float>(2f, 12f)));
            gameObject.AddComponent<TerminalMod>();
            gameObject.AddComponent<BootGreeting>();
            if (Enabled.Value)
                Boot.Announce(Boot.Commands);
            ModLog.Note($"{Name} {Version} loaded. Enabled: {Enabled.Value}. Log: {ModLog.LogFile}");
        }
    }
}
