using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx.Bootstrap;
using RRSOS.PCC.Shared;
using UnityEngine;

namespace RRSOS.PCC.Commands
{
    // Unity fills these fields itself when it reads mod.json, so the compiler never sees them assigned.
#pragma warning disable CS0649
    [Serializable]
    internal sealed class CommandInfo
    {
        public string name;
        public string usage;
        public string help;
    }

    [Serializable]
    internal sealed class ModInfo
    {
        public string id;
        public string name;
        public string version;
        public CommandInfo[] commands;
    }
#pragma warning restore CS0649

    /// <summary>
    /// The RRSOS mods the game has loaded, as they describe themselves in the mod.json next to their DLL. A mod that is not installed, or whose plugin the game
    /// did not load, is not in it, so a terminal's /help lists only commands that can really be used.
    /// </summary>
    internal static class ModCatalog
    {
        public static List<ModInfo> Loaded()
        {
            var result = new List<ModInfo>();
            foreach (var info in Chainloader.PluginInfos.Values)
            {
                if (!info.Metadata.GUID.StartsWith("com.rrsos.pcc."))
                    continue;

                try
                {
                    var path = Path.Combine(Path.GetDirectoryName(info.Location), "mod.json");
                    if (!File.Exists(path))
                        continue;

                    var mod = JsonUtility.FromJson<ModInfo>(File.ReadAllText(path));
                    if (mod != null)
                        result.Add(mod);
                }
                catch (Exception e)
                {
                    ModLog.LogOnce("catalog-" + info.Metadata.GUID, "Could not read the mod.json of " + info.Metadata.GUID + ": " + e.Message);
                }
            }

            return result.OrderBy(m => m.name).ToList();
        }

        /// <summary>The names ("/build") of every command the loaded mods describe, lower case.</summary>
        public static HashSet<string> CommandNames()
        {
            var names = new HashSet<string>();
            foreach (var mod in Loaded())
            {
                if (mod.commands == null)
                    continue;
                foreach (var command in mod.commands)
                    if (!string.IsNullOrEmpty(command.name))
                        names.Add(command.name.ToLowerInvariant());
            }

            return names;
        }
    }
}
