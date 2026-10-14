using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using RRSOS.PCC.Shared;
using SpaceCraft;
using Group = SpaceCraft.Group;

namespace RRSOS.PCC.Resupply
{
    /// <summary>
    /// The drone network, kept simple: a storage crate is a sink (it asks the drones for its one item) and a producer is a source (it offers what it makes). Never both on one
    /// inventory. The settings are the game's own, on an inventory's logistic entity (the same as the drone icon on a crate's window), set through the entity and then told to the game
    /// the way its window does (UpdateLogisticEntity), so the drones react at once and the save keeps them.
    /// </summary>
    internal static class DroneNetwork
    {
        // Things that make a product. Farms, growers and spreaders are not here: they take their inputs and hold their outputs. A butterfly farm has a drone setting but makes
        // nothing, and an autocrafter holds ingredients as well as what it makes, so neither is a source.
        private static readonly Regex Producer = new Regex(
            @"^(OreExtractor|GasExtractor|WaterCollector|ToxicWaterCollector|WaterLifeCollector|Beehive|Biodome|Ecosystem|HarvestingRobot|SilkGenerator)\d*$", RegexOptions.Compiled);

        private static readonly Regex Extractor = new Regex(@"^(OreExtractor|GasExtractor)\d*$", RegexOptions.Compiled);
        private static readonly Regex Hive = new Regex(@"^Beehive\d*$", RegexOptions.Compiled);
        private static readonly Regex Eco = new Regex(@"^Ecosystem\d*$", RegexOptions.Compiled);

        // a beehive makes honey and bee larvae, whatever it holds right now
        private static readonly string[] HiveProducts = { "honey", "Bee1Larvae" };

        public sealed class Outcome
        {
            public int Changed, AlreadySet, NoItem, NoInventory;
            public readonly List<string> Notes = new List<string>();
        }

        // ---- crates: demand ---------------------------------------------------------------------------------------------------

        /// <summary>
        /// Every storage crate on this planet that has an item (its label, or else its category, as CrateFill.ItemFor says) is made to ask the drones for that item, and for nothing
        /// else: it supplies nothing. A crate that already does so is left alone; one with no item is left as it is.
        /// </summary>
        public static Outcome SetDemand(int planetHash, ItemLookup lookup)
        {
            var outcome = new Outcome();
            var groups = new HashSet<string>(Plugin.Groups.Value.Split(',').Select(g => g.Trim()).Where(g => g.Length > 0));
            foreach (var crate in WorldObjectsHandler.Instance.GetAllWorldObjects().Values
                .Where(w => w != null && w.GetGroup() != null && groups.Contains(w.GetGroup().GetId()) && w.GetLinkedInventoryId() != 0 && w.GetPlanetHash() == planetHash).ToList())
            {
                var item = CrateFill.ItemFor(crate, lookup);
                if (item == null)
                {
                    outcome.NoItem++;
                    continue;
                }

                var inventory = InventoriesHandler.Instance.GetInventoryById(crate.GetLinkedInventoryId());
                if (inventory == null)
                {
                    outcome.NoInventory++;
                    continue;
                }

                var entity = inventory.GetLogisticEntity();
                var demand = entity.GetDemandGroups();
                if (demand.Count == 1 && demand.Contains(item) && entity.GetSupplyGroups().Count == 0)
                {
                    outcome.AlreadySet++;
                    continue;
                }

                entity.SetSupplyGroups(new HashSet<Group>());
                entity.SetDemandGroups(new HashSet<Group> { item });
                InventoriesHandler.Instance.UpdateLogisticEntity(inventory);
                outcome.Changed++;
            }

            ModLog.Note("Drone demand: " + outcome.Changed + " crates set, " + outcome.AlreadySet + " already set, " + outcome.NoItem + " with no item.");
            return outcome;
        }

        // ---- producers: supply ------------------------------------------------------------------------------------------------

        /// <summary>
        /// Every producer on this planet (extractors, collectors, hives, an ecosystem, ...) is made to offer what it makes, and nothing else; its own demand stays as it is. What it makes:
        /// a beehive, honey and bee larvae; an ecosystem, every larva and egg; an extractor, the ore or gas it is set to; the rest, what they hold now. A producer that makes nothing yet
        /// is left as it is.
        /// </summary>
        public static Outcome SetSupply(int planetHash)
        {
            var outcome = new Outcome();
            foreach (var machine in WorldObjectsHandler.Instance.GetAllWorldObjects().Values
                .Where(w => w != null && w.GetGroup() != null && Producer.IsMatch(w.GetGroup().GetId()) && w.GetLinkedInventoryId() != 0 && w.GetPlanetHash() == planetHash && w.GetPosition().sqrMagnitude > 0f).ToList())
            {
                var inventory = InventoriesHandler.Instance.GetInventoryById(machine.GetLinkedInventoryId());
                if (inventory == null)
                {
                    outcome.NoInventory++;
                    continue;
                }

                var products = ProductsOf(machine, inventory);
                if (products.Count == 0)
                {
                    outcome.NoItem++;
                    continue;
                }

                var entity = inventory.GetLogisticEntity();
                if (entity.GetSupplyGroups().SetEquals(products))
                {
                    outcome.AlreadySet++;
                    continue;
                }

                entity.SetSupplyGroups(products);
                InventoriesHandler.Instance.UpdateLogisticEntity(inventory);
                outcome.Changed++;
            }

            ModLog.Note("Drone supply: " + outcome.Changed + " producers set, " + outcome.AlreadySet + " already set, " + outcome.NoItem + " making nothing yet.");
            return outcome;
        }

        private static HashSet<Group> ProductsOf(WorldObject machine, Inventory inventory)
        {
            var id = machine.GetGroup().GetId();
            var products = new HashSet<Group>();

            if (Hive.IsMatch(id))
            {
                foreach (var product in HiveProducts)
                    AddGroup(products, product);
            }
            else if (Eco.IsMatch(id))
            {
                foreach (var group in GroupsHandler.GetAllGroups())
                    if (group.GetId().EndsWith("Larvae", StringComparison.Ordinal) || group.GetId().EndsWith("Eggs", StringComparison.Ordinal))
                        products.Add(group);
            }
            else if (Extractor.IsMatch(id))
            {
                // the ore or gas the extractor is set to mine: what its linked groups list
                if (machine.HasLinkedGroups())
                    foreach (var group in machine.GetLinkedGroups())
                        if (group != null)
                            products.Add(group);
            }
            else
            {
                foreach (var item in inventory.GetInsideWorldObjects())
                    if (item != null && item.GetGroup() != null)
                        products.Add(item.GetGroup());
            }

            return products;
        }

        private static void AddGroup(HashSet<Group> set, string groupId)
        {
            var group = GroupsHandler.GetGroupViaId(groupId);
            if (group != null)
                set.Add(group);
        }
    }
}
