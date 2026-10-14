using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using BepInEx.Configuration;
using RRSOS.PCC.Shared;
using SpaceCraft;

namespace RRSOS.PCC.Boosters
{
    /// <summary>A game id and what to call it: a machine tier, an optimizer, a fuse, a seed to fill a machine with.</summary>
    internal sealed class Choice
    {
        public readonly string GId;
        public readonly string Label;

        public Choice(string gId, string label)
        {
            GId = gId;
            Label = label;
        }
    }

    /// <summary>
    /// One kind of booster, as a preset the screen lets you change: the machine (and its tiers, lowest first), the fuses its optimizers start with (one optimizer each), and what its
    /// machines can be filled with. Taken from the dashboard's Boosters page (RRSOS-PCC-Live), with the same spacings. Eight machines always: a stack of eight at the beacon, or eight
    /// in a 3 by 3 ring around the optimizers when the player has to open the machines.
    /// </summary>
    internal sealed class Kind
    {
        public string Key, Name;
        public string[] Aliases;
        public Choice[] Machines;
        public string[] DefaultFuses;
        public bool Ring;
        public double Spacing;
        public Choice[] Fill;             // what each machine's inventory can be filled with; null when the build leaves it empty
        public string FillDefault = "";   // the id chosen to begin with; "" leaves the machines empty
        public bool Grown;                // the machine is set to full growth (a spreader's "grwth":100)
        public string Supply;             // the group ids each machine's inventory supplies to the drones (a beehive: honey and bee larvae)
        public bool SupplyEverything;     // supplies every kind of larva and egg (an ecosystem makes them all)
    }

    internal static class BoosterKinds
    {
        /// <summary>The distance between the stack and its optimizers, and between two optimizers standing together, in metres.</summary>
        public const double OptimizerDistance = 3, OptimizerGap = 1.6;

        public const int Count = 8;

        public static readonly Choice[] Optimizers =
        {
            new Choice("Optimizer1", "T1 optimizer"),
            new Choice("Optimizer2", "T2 optimizer")
        };

        public static readonly Choice[] Fuses =
        {
            new Choice("FuseHeat1", "Heat"), new Choice("FusePressure1", "Pressure"), new Choice("FuseOxygen1", "Oxygen"), new Choice("FusePlants1", "Plants"),
            new Choice("FuseInsects1", "Insects"), new Choice("FuseAnimals1", "Animals"), new Choice("FuseEnergy1", "Energy"), new Choice("FusePurification1", "Purification")
        };

        private static readonly Choice[] TreeSeeds =
        {
            new Choice("Tree0Seed", "Iterra"), new Choice("Tree1Seed", "Linifolia"), new Choice("Tree2Seed", "Aleatus"), new Choice("Tree3Seed", "Cernea"),
            new Choice("Tree4Seed", "Elegea"), new Choice("Tree5Seed", "Humelora"), new Choice("Tree6Seed", "Aemora"), new Choice("Tree7Seed", "Pleom"),
            new Choice("Tree8Seed", "Soleus"), new Choice("Tree9Seed", "Shreox"), new Choice("Tree10Seed", "Rosea"), new Choice("Tree11Seed", "Lillia"),
            new Choice("Tree12Seed", "Prunea"), new Choice("Tree13Seed", "Ruberu"), new Choice("Tree14Seed", "Malissea"), new Choice("Tree15Seed", "Redwood"),
            new Choice("Tree16Seed", "Pamelia"), new Choice("Tree17Seed", "Detoxo"), new Choice("Tree18Seed", "Brojo")
        };

        // The flowers a spreader can be filled with, by the names the game gives their seeds (Seed1 to Seed10Humble, and the golden one; Seed0 is left out)
        private static readonly Choice[] FlowerSeeds =
        {
            new Choice("Seed1", "Shanga"), new Choice("Seed2", "Pestera"), new Choice("Seed3", "Nulna"), new Choice("Seed4", "Tuska"),
            new Choice("Seed5", "Orema"), new Choice("Seed6", "Volnus"), new Choice("Seed8Humble", "Brelea"), new Choice("Seed7Humble", "Snepea"),
            new Choice("Seed10Humble", "Furteo"), new Choice("Seed9Humble", "Seleus"), new Choice("SeedGold", "Golden")
        };

        private static readonly Choice[] FrogEggs =
        {
            new Choice("Frog1Eggs", "Generic"), new Choice("Frog2Eggs", "Huli"), new Choice("Frog3Eggs", "Felicianna"), new Choice("Frog4Eggs", "Strabo"),
            new Choice("Frog5Eggs", "Trajuu"), new Choice("Frog6Eggs", "Aiolus"), new Choice("Frog7Eggs", "Afae"), new Choice("Frog8Eggs", "Cillus"),
            new Choice("Frog9Eggs", "Amedo"), new Choice("Frog10Eggs", "Kenjoss"), new Choice("Frog11Eggs", "Lavaum"), new Choice("Frog12Eggs", "Leglus"),
            new Choice("Frog13Eggs", "Jumi"), new Choice("Frog14Eggs", "Seren"), new Choice("Frog15Eggs", "Acuzzi"), new Choice("Frog16Eggs", "Toxifia"),
            new Choice("FrogGoldEggs", "Golden")
        };

        private static readonly Choice[] ButterflyLarvae =
        {
            new Choice("Butterfly1Larvae", "Azurae"), new Choice("Butterfly2Larvae", "Leani"), new Choice("Butterfly3Larvae", "Fensea"), new Choice("Butterfly4Larvae", "Galaxe"),
            new Choice("Butterfly5Larvae", "Abstreus"), new Choice("Butterfly6Larvae", "Empalio"), new Choice("Butterfly7Larvae", "Penga"), new Choice("Butterfly8Larvae", "Chevrone"),
            new Choice("Butterfly9Larvae", "Aemel"), new Choice("Butterfly10Larvae", "Liux"), new Choice("Butterfly11Larvae", "Nere"), new Choice("Butterfly12Larvae", "Lorpen"),
            new Choice("Butterfly13Larvae", "Fiorente"), new Choice("Butterfly14Larvae", "Alben"), new Choice("Butterfly15Larvae", "Futura"), new Choice("Butterfly16Larvae", "Imeo"),
            new Choice("Butterfly17Larvae", "Serena"), new Choice("Butterfly18Larvae", "Golden"), new Choice("Butterfly19Larvae", "Faleria"), new Choice("Butterfly20Larvae", "Oesbe"),
            new Choice("Butterfly21Larvae", "Lucia")
        };

        private static Choice[] Machines(params string[] pairs)
        {
            var list = new List<Choice>();
            for (var i = 0; i < pairs.Length; i += 2)
                list.Add(new Choice(pairs[i], pairs[i + 1]));
            return list.ToArray();
        }

        // The ids are the game's, not the tiers' numbering: the T5 drill is Drill4, the T1 tree spreader is TreeSpreader0. Tiers run lowest first.
        // The grass spreader takes oxygen and plants fuses (the owner's choice); the flower spreader's fuse and both spacings are a guess from the tree spreader (change them on the screen), and the game has no
        // grass spreader above the first tier yet; a later one is one more entry in Machines (and a chain in the Base mod's upgrades.txt).
        public static readonly Kind[] All =
        {
            new Kind { Key = "Heat", Name = "Heat", Aliases = new[] { "heat", "heater", "hot", "warm" },
                Machines = Machines("Heater1", "Heater T1", "Heater2", "Heater T2", "Heater3", "Heater T3", "Heater4", "Heater T4", "Heater5", "Heater T5"),
                DefaultFuses = new[] { "FuseHeat1", "FusePressure1" } },

            new Kind { Key = "Pressure", Name = "Pressure", Aliases = new[] { "pressure", "drill" },
                Machines = Machines("Drill0", "Drill T1", "Drill1", "Drill T2", "Drill2", "Drill T3", "Drill3", "Drill T4", "Drill4", "Drill T5"),
                DefaultFuses = new[] { "FuseHeat1", "FusePressure1" } },

            new Kind { Key = "Power", Name = "Power", Aliases = new[] { "power", "energy", "electric", "generator", "solar", "wind", "nuclear", "reactor", "fusion" },
                Machines = Machines("EnergyGenerator1", "Wind Turbine", "EnergyGenerator2", "Solar Panel T1", "EnergyGenerator3", "Solar Panel T2", "EnergyGenerator4", "Nuclear Reactor T1", "EnergyGenerator5", "Nuclear Reactor T2", "EnergyGenerator6", "Fusion Generator"),
                DefaultFuses = new[] { "FuseEnergy1" } },

            new Kind { Key = "Butterflies", Name = "Butterflies", Aliases = new[] { "butterfl", "moth" },
                Machines = Machines("ButterflyFarm1", "Butterfly Farm T1", "ButterflyFarm2", "Butterfly Farm T2"),
                DefaultFuses = new[] { "FuseInsects1" }, Ring = true, Spacing = 6.6, Fill = ButterflyLarvae },

            new Kind { Key = "Bees", Name = "Bees", Aliases = new[] { "bee", "hive", "honey" },
                Machines = Machines("Beehive1", "Beehive T1", "Beehive2", "Beehive T2"),
                DefaultFuses = new[] { "FuseInsects1" }, Ring = true, Spacing = 3.3, Supply = "honey,Bee1Larvae" },

            new Kind { Key = "Ecosystem", Name = "Ecosystem", Aliases = new[] { "eco" },
                Machines = Machines("Ecosystem1", "Ecosystem"),
                DefaultFuses = new[] { "FusePlants1", "FuseInsects1" }, Ring = true, Spacing = 10.5, SupplyEverything = true },

            new Kind { Key = "Frogs", Name = "Frog", Aliases = new[] { "frog", "toad", "amphib" },
                Machines = Machines("AmphibiansFarm1", "Amphibian Farm"),
                DefaultFuses = new[] { "FusePlants1", "FuseAnimals1" }, Ring = true, Spacing = 6.6, Fill = FrogEggs },

            new Kind { Key = "Trees", Name = "Tree", Aliases = new[] { "tree", "spreader", "redwood", "forest" },
                Machines = Machines("TreeSpreader0", "Tree Spreader T1", "TreeSpreader1", "Tree Spreader T2", "TreeSpreader2", "Tree Spreader T3"),
                DefaultFuses = new[] { "FuseOxygen1", "FusePlants1" }, Ring = true, Spacing = 7.3, Fill = TreeSeeds, FillDefault = "Tree15Seed", Grown = true },

            new Kind { Key = "Grass", Name = "Grass", Aliases = new[] { "grass", "lawn" },
                Machines = Machines("GrassSpreader1", "Grass Spreader"),
                DefaultFuses = new[] { "FuseOxygen1", "FusePlants1" }, Ring = true, Spacing = 7.3, Grown = true },

            new Kind { Key = "Flowers", Name = "Flower", Aliases = new[] { "flower", "bloom" },
                Machines = Machines("SeedSpreader1", "Flower Spreader T1", "SeedSpreader2", "Flower Spreader T2"),
                DefaultFuses = new[] { "FusePlants1" }, Ring = true, Spacing = 7.3, Fill = FlowerSeeds, Grown = true }
        };

        private const string NoFill = "No Fill";
        private static readonly Dictionary<string, ConfigEntry<string>> FillSettings = new Dictionary<string, ConfigEntry<string>>();

        /// <summary>One setting per kind that has a fill list (flowers, frogs, trees, butterflies, and any kind added later): what its machines are filled with when the screen opens on it. A fixed list, so the Mod Manager offers it as a cycling button.</summary>
        public static void BindFillSettings(ConfigFile config)
        {
            foreach (var kind in All.Where(k => k.Fill != null))
            {
                var labels = new[] { NoFill }.Concat(kind.Fill.Select(c => c.Label)).ToArray();
                var start = string.IsNullOrEmpty(kind.FillDefault) ? NoFill : kind.Fill.First(c => c.GId == kind.FillDefault).Label;
                FillSettings[kind.Key] = config.Bind("Fill", kind.Key, start,
                    new ConfigDescription("What the machines of a " + kind.Name + " booster are filled with when the Boosters screen opens on it. It can still be changed on the screen.", new AcceptableValueList<string>(labels)));
            }
        }

        /// <summary>The id the kind starts with on the screen: its setting, or its built-in default; "" for no fill. A choice that is not available on the planet the player is on is not used.</summary>
        public static string DefaultFill(Kind kind)
        {
            var id = kind.FillDefault ?? "";
            if (kind.Fill != null && FillSettings.TryGetValue(kind.Key, out var setting))
            {
                var chosen = kind.Fill.FirstOrDefault(c => c.Label == setting.Value);
                id = chosen == null ? "" : chosen.GId;
            }

            return id.Length > 0 && !AvailableHere(id) ? "" : id;
        }

        /// <summary>Whether the game lets this item be used on the planet the player is on (a seed that belongs to one planet is offered only there). When that cannot be told, it is offered.</summary>
        public static bool AvailableHere(string groupId)
        {
            try
            {
                var group = GroupsHandler.GetGroupViaId(groupId);
                return group == null || group.GetUnlockingInfos() == null || group.GetUnlockingInfos().GetIsAvailableOnCurrentPlanet();
            }
            catch (Exception e)
            {
                ModLog.LogOnce("boosters-planet-" + groupId, "Boosters: could not tell whether " + groupId + " is for this planet: " + e.GetType().Name + ": " + e.Message);
                return true;
            }
        }

        public static Kind ByKey(string key) => All.FirstOrDefault(k => string.Equals(k.Key, key, StringComparison.OrdinalIgnoreCase));

        /// <summary>The booster a beacon's name asks for, or null. Forgiving: the name is split into words and any word that starts with a kind's name or one of its aliases counts;
        /// the longest match wins.</summary>
        public static Kind For(string beaconText)
        {
            var words = Regex.Split((beaconText ?? "").ToLowerInvariant(), "[^a-z]+").Where(w => w.Length >= 3).ToList();
            Kind best = null;
            var bestLength = 0;
            foreach (var kind in All)
            {
                foreach (var alias in kind.Aliases.Concat(new[] { kind.Key.ToLowerInvariant() }))
                {
                    if (alias.Length > bestLength && words.Any(w => w.StartsWith(alias, StringComparison.Ordinal)))
                    {
                        best = kind;
                        bestLength = alias.Length;
                    }
                }
            }

            return best;
        }
    }
}
