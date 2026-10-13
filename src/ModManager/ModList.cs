using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using RRSOS.PCC.Shared;

namespace RRSOS.PCC.ModManager
{
    /// <summary>One mod the manager shows: a loaded BepInEx plugin, or a plugin folder that was switched off and is waiting in plugins-disabled.</summary>
    internal sealed class ModInfo
    {
        public string Name;
        public string Version = "";
        public string Guid = "";
        public string Top;              // the plugin's folder (or single dll) directly under BepInEx\plugins, as a name
        public bool Loaded;             // running in this session
        public bool OffAtNextStart;     // its folder has been moved to plugins-disabled
        public bool IsSelf;
        public ConfigFile Config;

        /// <summary>The setting that switches the mod on or off while the game runs, when it has one.</summary>
        public ConfigEntryBase EnabledEntry => Config == null ? null : Config.Keys.Where(k => k.Key == "Enabled").Select(k => Config[k]).FirstOrDefault(e => e.SettingType == typeof(bool));
    }

    /// <summary>The mods, and the move of a mod's folder between BepInEx\plugins and BepInEx\plugins-disabled (which takes effect when the game starts again).</summary>
    internal static class ModList
    {
        private static string PluginsRoot => Paths.PluginPath;
        private static string DisabledRoot => Path.Combine(Paths.BepInExRootPath, "plugins-disabled");

        public static List<ModInfo> Load()
        {
            var result = new List<ModInfo>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var info in Chainloader.PluginInfos.Values)
            {
                var top = TopOf(info.Location);
                var mod = new ModInfo
                {
                    Name = info.Metadata.Name,
                    Version = info.Metadata.Version.ToString(),
                    Guid = info.Metadata.GUID,
                    Top = top,
                    Loaded = true,
                    IsSelf = info.Metadata.GUID == Plugin.Guid,
                    Config = info.Instance == null ? null : info.Instance.Config
                };
                mod.OffAtNextStart = top != null && !Exists(Path.Combine(PluginsRoot, top)) && Exists(Path.Combine(DisabledRoot, top));
                result.Add(mod);
                if (top != null)
                    seen.Add(top);
            }

            // folders switched off in an earlier session: not loaded now, so all that is known is the folder's name
            if (Directory.Exists(DisabledRoot))
            {
                foreach (var path in Directory.GetFileSystemEntries(DisabledRoot))
                {
                    var name = Path.GetFileName(path);
                    if (seen.Contains(name))
                        continue;
                    result.Add(new ModInfo { Name = Path.GetFileNameWithoutExtension(name), Top = name, Loaded = false, OffAtNextStart = true });
                }
            }

            // a loaded mod with no settings to show (the Live plugin; the Mod Manager, whose only setting is hidden) has nothing to manage; a switched-off folder stays so it can be switched on
            result = result.Where(m => !m.Loaded || HasSettings(m)).ToList();

            // RRSOS mods first, then the rest, each by name
            return result.OrderBy(m => m.Guid.StartsWith("com.rrsos.", StringComparison.Ordinal) || m.Name.StartsWith("RRSOS", StringComparison.Ordinal) ? 0 : 1).ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static bool HasSettings(ModInfo mod) => mod.Config != null && mod.Config.Keys.Any(k => !(mod.IsSelf && k.Key == "Enabled"));

        public static string FolderText(ModInfo mod) => mod.Top == null ? "" : Path.Combine(mod.OffAtNextStart ? DisabledRoot : PluginsRoot, mod.Top);

        /// <summary>Moves the mod's folder out of or back into BepInEx\plugins. Returns an error text, or null when it worked.</summary>
        public static string Toggle(ModInfo mod)
        {
            if (mod.Top == null)
                return "This mod's folder is not known.";
            if (mod.IsSelf)
                return "The Mod Manager cannot switch itself off.";

            try
            {
                var from = Path.Combine(mod.OffAtNextStart ? DisabledRoot : PluginsRoot, mod.Top);
                var to = Path.Combine(mod.OffAtNextStart ? PluginsRoot : DisabledRoot, mod.Top);
                if (!Exists(from))
                    return "Not found: " + from;
                if (Exists(to))
                    return "Already there: " + to;

                Directory.CreateDirectory(Path.GetDirectoryName(to));
                if (Directory.Exists(from))
                    Directory.Move(from, to);
                else
                    File.Move(from, to);

                mod.OffAtNextStart = !mod.OffAtNextStart;
                ModLog.Note((mod.OffAtNextStart ? "Switched off at the next start: " : "Switched on at the next start: ") + mod.Name + " (" + mod.Top + ").");
                return null;
            }
            catch (Exception e)
            {
                return "Could not move the folder: " + e.Message;
            }
        }

        private static bool Exists(string path) => Directory.Exists(path) || File.Exists(path);

        // the first part of the plugin's path under BepInEx\plugins
        private static string TopOf(string location)
        {
            try
            {
                var relative = location.Substring(PluginsRoot.TrimEnd('\\', '/').Length).TrimStart('\\', '/');
                var cut = relative.IndexOfAny(new[] { '\\', '/' });
                return cut < 0 ? relative : relative.Substring(0, cut);
            }
            catch
            {
                return null;
            }
        }
    }
}
