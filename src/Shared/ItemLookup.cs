using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SpaceCraft;

namespace RRSOS.PCC.Shared
{
    /// <summary>
    /// Turns a label ("O2", "oxygen capsule", "waterbottle1") into an item. Whole-name matches only, never part of a name: a label matches an item when, ignoring case, spaces and
    /// punctuation, it equals the item's id, its display name in the game, one of its extra names in DefaultNames.txt (built into the DLL), or one of the names in the user's
    /// aliases.txt (which win). A name that two items go by is not guessed at.
    /// Shared source: compiled into each mod that needs it (Auto Fill, the terminal), so the mods do not depend on each other.
    /// </summary>
    internal sealed class ItemLookup
    {
        /// <summary>The user's own names, "item id = name, name, ...". One file for every mod that resolves item names.</summary>
        public static string AliasFile => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RRSOS-PCC-Mods", "aliases.txt");

        // lower-case item id -> item; plain name -> the items that go by it; plain name -> the item the user's file chose
        private Dictionary<string, GroupItem> _byId;
        private Dictionary<string, HashSet<GroupItem>> _byName;
        private Dictionary<string, GroupItem> _explicit;
        private readonly HashSet<string> _toldAmbiguous = new HashSet<string>();
        private int _builtFor;

        /// <summary>A name with only its letters and digits, lower case, so "Oxygen capsule", "oxygen-capsule" and "OXYGEN CAPSULE" are one name.</summary>
        public static string Plain(string text) => new string(text.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

        /// <summary>The item with this id (any case), or null. Call only once the game's groups are loaded.</summary>
        public GroupItem ById(string id)
        {
            Build();
            return _byId.TryGetValue(id.Trim().ToLowerInvariant(), out var item) ? item : null;
        }

        /// <summary>The item a label names, or null. A label that fits several items is logged once and gives null.</summary>
        public GroupItem Resolve(string label)
        {
            Build();
            var plain = Plain(label);
            if (plain.Length == 0)
                return null;

            if (_explicit.TryGetValue(plain, out var chosen))
                return chosen;

            if (_byId.TryGetValue(label.Trim().ToLowerInvariant(), out var viaId))
                return viaId;

            if (_byName.TryGetValue(plain, out var items))
            {
                if (items.Count == 1)
                    return items.First();

                if (_toldAmbiguous.Add(plain))
                    ModLog.Note("The label \"" + label + "\" fits several items (" + string.Join(", ", items.Select(i => i.GetId()).OrderBy(i => i).ToArray()) + "); nothing filled. Name one in aliases.txt to pick.");
            }

            return null;
        }

        /// <summary>The one item a name already goes to, or null when it goes to none or to several.</summary>
        public GroupItem Existing(string name)
        {
            Build();
            var plain = Plain(name);
            if (_explicit.TryGetValue(plain, out var chosen))
                return chosen;
            if (_byId.TryGetValue(name.Trim().ToLowerInvariant(), out var viaId))
                return viaId;
            if (_byName.TryGetValue(plain, out var items) && items.Count == 1)
                return items.First();
            return null;
        }

        /// <summary>Gives an item one more name, for this session; the caller writes it to aliases.txt.</summary>
        public void AddExplicit(string name, GroupItem item)
        {
            Build();
            _explicit[Plain(name)] = item;
        }

        // ---- building ----------------------------------------------------------------------------------------------------------

        private void Build()
        {
            var all = GroupsHandler.GetAllGroups();
            if (_byId != null && _builtFor == all.Count)
                return;

            _byId = new Dictionary<string, GroupItem>();
            _byName = new Dictionary<string, HashSet<GroupItem>>();
            _explicit = new Dictionary<string, GroupItem>();
            _toldAmbiguous.Clear();

            foreach (var group in all)
            {
                var item = group as GroupItem;
                if (item == null)
                    continue;

                _byId[item.GetId().ToLowerInvariant()] = item;
                AddName(Plain(item.GetId()), item);
                try
                {
                    AddName(Plain(Readable.GetGroupName(item) ?? ""), item);
                }
                catch (Exception)
                {
                }
            }

            var extra = 0;
            foreach (var pair in ReadNames(ReadDefaultNames()))
            {
                if (_byId.TryGetValue(pair.Key.ToLowerInvariant(), out var item))
                {
                    foreach (var name in pair.Value)
                    {
                        AddName(Plain(name), item);
                        extra++;
                    }
                }
            }

            var mine = 0;
            foreach (var pair in ReadNames(ReadUserNames()))
            {
                if (_byId.TryGetValue(pair.Key.ToLowerInvariant(), out var item))
                {
                    foreach (var name in pair.Value)
                    {
                        _explicit[Plain(name)] = item;
                        mine++;
                    }
                }
                else
                {
                    ModLog.Note("aliases.txt: \"" + pair.Key + "\" is not an item id; its names are ignored.");
                }
            }

            _builtFor = all.Count;
            ModLog.Note("Item lookup built: " + _byId.Count + " item ids, " + _byName.Count + " names (" + extra + " extra names from the default table), " + mine + " names from aliases.txt.");
        }

        private void AddName(string plain, GroupItem item)
        {
            if (plain.Length == 0)
                return;

            if (!_byName.TryGetValue(plain, out var set))
                _byName[plain] = set = new HashSet<GroupItem>();
            set.Add(item);
        }

        /// <summary>Lines of "item id = name, name, ..." ("#" starts a comment).</summary>
        private static Dictionary<string, List<string>> ReadNames(IEnumerable<string> lines)
        {
            var result = new Dictionary<string, List<string>>();
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                var at = trimmed.IndexOf('=');
                if (trimmed.StartsWith("#") || at <= 0)
                    continue;

                var names = trimmed.Substring(at + 1).Split(',').Select(n => n.Trim()).Where(n => n.Length > 0).ToList();
                var id = trimmed.Substring(0, at).Trim();
                if (result.TryGetValue(id, out var existing))
                    existing.AddRange(names);
                else
                    result[id] = names;
            }

            return result;
        }

        private static IEnumerable<string> ReadDefaultNames()
        {
            using (var stream = typeof(ItemLookup).Assembly.GetManifestResourceStream("DefaultNames.txt"))
            {
                if (stream == null)
                    return new string[0];

                using (var reader = new StreamReader(stream))
                    return reader.ReadToEnd().Split('\n');
            }
        }

        /// <summary>aliases.txt: "item id = name, name, ...", the same format as the default table. Made with a few comment lines the first time (an earlier copy in Auto Fill's own folder is carried over).</summary>
        private static IEnumerable<string> ReadUserNames()
        {
            try
            {
                var path = AliasFile;
                if (!File.Exists(path))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    var earlier = Path.Combine(Path.GetDirectoryName(path), "AutoFill", "aliases.txt");
                    if (File.Exists(earlier))
                    {
                        File.Copy(earlier, path);
                    }
                    else
                    {
                        File.WriteAllText(path,
                            "# Your own names for items: item id = name, name, ...\r\n"
                            + "# A crate label must equal a whole name (case, spaces and punctuation do not matter). Names here win over the built-in ones.\r\n"
                            + "# Example (remove the #):\r\n"
                            + "# OxygenCapsule1 = oxygen capsule, oxygen bottle, oxygen, O2\r\n");
                        return new string[0];
                    }
                }

                return File.ReadAllLines(path);
            }
            catch (Exception e)
            {
                ModLog.Note("Could not read aliases.txt: " + e.Message);
                return new string[0];
            }
        }
    }
}
