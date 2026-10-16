using System;
using System.Collections;
using System.IO;
using SpaceCraft;
using UnityEngine;
using RRSOS.PCC.Shared;

namespace RRSOS.PCC.Base
{
    /// <summary>
    /// A backup save, made with the game's own save call under another name (for example Standard-1-before-build-20261016-054500), just before /build or /undo changes the world.
    /// The game's current save is not touched and keeps its name; the backup can be loaded from the game's load screen like any other save.
    /// </summary>
    internal static class SaveBackup
    {
        private const float WaitSeconds = 8f;

        /// <summary>Makes the backup and says whether the file is there; the command goes on only when it is.</summary>
        public static IEnumerator Make(string why, Action<bool> done)
        {
            var ok = false;
            string path = null;
            string name = null;
            try
            {
                var handler = UnityEngine.Object.FindObjectOfType<SavedDataHandler>();
                if (handler != null)
                {
                    name = handler.GetCurrentSaveFileName() + "-before-" + why + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                    path = Path.Combine(Application.persistentDataPath, name + ".json");
                    handler.SaveWorldData(name);
                }
            }
            catch (Exception e)
            {
                ModLog.Note("Backup save failed to start: " + e.GetType().Name + ": " + e.Message);
            }

            if (path != null)
            {
                var until = Time.realtimeSinceStartup + WaitSeconds;
                while (!File.Exists(path) && Time.realtimeSinceStartup < until)
                    yield return null;

                ok = File.Exists(path);
                if (ok)
                    yield return new WaitForSecondsRealtime(0.5f);   // let the writer finish
            }

            ModLog.Note(ok ? "Backup save written: " + name : "No backup save could be made before " + why + ".");
            done(ok);
        }
    }
}
