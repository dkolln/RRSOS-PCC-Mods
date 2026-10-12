using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx.Logging;

namespace RRSOS.PCC.Shared
{
    /// <summary>
    /// One log and one data folder per mod: %LOCALAPPDATA%\RRSOS-PCC-Mods\&lt;mod&gt;\ (log.txt and whatever small state files the mod keeps there).
    /// Compiled into each mod's own DLL, so mods do not depend on each other.
    /// </summary>
    internal static class ModLog
    {
        private static readonly HashSet<string> Reported = new HashSet<string>();
        private static ManualLogSource _log;
        private static string _folder;

        public static string Folder => _folder;

        public static string LogFile => Path.Combine(_folder, "log.txt");

        public static void Init(string modName, ManualLogSource log)
        {
            _log = log;
            _folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RRSOS-PCC-Mods", modName);
        }

        public static string FileIn(string name) => Path.Combine(_folder, name);

        /// <summary>Writes to the BepInEx log and to this mod's log.txt.</summary>
        public static void Note(string message)
        {
            _log.LogInfo(message);
            try
            {
                Append(LogFile, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + message);
            }
            catch (Exception)
            {
            }
        }

        /// <summary>Like Note, but only the first time for each key, so a problem that repeats every second cannot flood the log.</summary>
        public static void LogOnce(string key, string message)
        {
            if (Reported.Add(key))
                Note(message);
        }

        public static List<string> ReadLines(string path) => File.Exists(path) ? File.ReadAllLines(path).Where(l => l.Length > 0).ToList() : new List<string>();

        public static void Append(string path, string line)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.AppendAllText(path, line + Environment.NewLine);
        }

        public static void Consume(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception e)
            {
                Note("Could not delete " + path + ": " + e.Message);
            }
        }
    }
}
