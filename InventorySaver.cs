using Oxide.Core;
using Oxide.Core.Plugins;
using System;
using System.Collections.Generic;
using System.IO;

namespace Oxide.Plugins
{
    [Info("InventorySaver", "Magnumk", "1.3.4")]
    [Description("Saves players' inventories and backpacks upon death with multiple save slots.")]
    public class InventorySaver : RustPlugin
    {
        [PluginReference]
        private Plugin Backpacks;

        [PluginReference]
        private Plugin BagOfHolding;

        private const string PermissionUse = "inventorysaver.use";
        private const int MaxSaveSlots = 30;

        private void Init()
        {
            permission.RegisterPermission(PermissionUse, this);
        }

        private void OnPlayerDeath(BasePlayer player, HitInfo info)
        {
            if (!player.IsNpc)
            {
                SavePlayerInventory(player);
            }
        }

        [ConsoleCommand("restoreinv")]
        private void RestoreInventoryCommand(ConsoleSystem.Arg arg)
        {
            if (!arg.IsAdmin && !permission.UserHasPermission(arg.Connection?.userid.ToString(), PermissionUse))
            {
                SendReply(arg, "You do not have permission to use this command.");
                return;
            }

            if (arg.Args == null || arg.Args.Length < 2)
            {
                SendReply(arg, "Usage: restoreinv <steamid> <slot | list>");
                return;
            }

            string steamId = arg.Args[0];
            string option = arg.Args[1].ToLower();

            if (option == "list")
            {
                ListPlayerDeaths(steamId, arg);
                return;
            }

            if (!int.TryParse(option, out int slot) || slot < 1 || slot > MaxSaveSlots)
            {
                SendReply(arg, $"Slot must be a number between 1 and {MaxSaveSlots}.");
                return;
            }

            RestorePlayerInventory(steamId, slot);
        }

        private void ListPlayerDeaths(string steamId, ConsoleSystem.Arg arg)
        {
            string dirPath = Path.Combine("InventorySaver", steamId);
            string filePath = Path.Combine(dirPath, "inventory.json");

            if (!Interface.Oxide.DataFileSystem.ExistsDatafile(filePath))
            {
                SendReply(arg, $"No saved inventories found for {steamId}.");
                return;
            }

            var inventoryData = Interface.Oxide.DataFileSystem.ReadObject<List<SavedInventory>>(filePath);
            SendReply(arg, $"Listing saved inventories for player {steamId}:");

            foreach (var save in inventoryData)
            {
                int itemCount = (save.Inventory.Wear?.Count ?? 0) +
                                (save.Inventory.Main?.Count ?? 0) +
                                (save.Inventory.Belt?.Count ?? 0) +
                                (save.Inventory.Backpack?.Count ?? 0);
                SendReply(arg, $"Slot {save.Slot}: {DateTime.Parse(save.Timestamp).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")} - {itemCount} items.");
            }
        }

        private void SavePlayerInventory(BasePlayer player)
        {
            string steamId = player.UserIDString;
            string dirPath = Path.Combine("InventorySaver", steamId);
            if (!Directory.Exists(dirPath))
            {
                Directory.CreateDirectory(dirPath);
            }

            string filePath = Path.Combine(dirPath, "inventory.json");
            var inventoryData = Interface.Oxide.DataFileSystem.ExistsDatafile(filePath)
                ? Interface.Oxide.DataFileSystem.ReadObject<List<SavedInventory>>(filePath)
                : new List<SavedInventory>();

            Dictionary<string, List<Item>> allBags = new Dictionary<string, List<Item>>();

            if (BagOfHolding != null)
            {
                try
                {
                    var bagContents = BagOfHolding.Call("API_GetAllBagContents", player.UserIDString) as Dictionary<string, List<Item>>;
                    if (bagContents != null)
                    {
                        foreach (var bag in bagContents)
                        {
                            allBags[bag.Key] = bag.Value;
                            Puts($"[DEBUG] Bag '{bag.Key}' has {bag.Value.Count} items.");
                        }
                    }
                    else
                    {
                        Puts($"[DEBUG] API_GetAllBagContents returned null for player {player.UserIDString}.");
                    }
                }
                catch (Exception ex)
                {
                    Puts($"[DEBUG] Exception while fetching BagOfHolding data: {ex.Message}");
                }
            }
            else
            {
                Puts("[DEBUG] BagOfHolding plugin is not loaded or not referenced.");
            }

            List<ItemData> defaultBackpackContents = null;
            foreach (var item in player.inventory.containerWear.itemList)
            {
                if (item.info.shortname == "largebackpack" || item.info.shortname == "smallbackpack")
                {
                    if (item.contents != null)
                    {
                        defaultBackpackContents = ConvertItemsWithContents(item.contents.itemList);
                        Puts($"[DEBUG] Default backpack '{item.info.shortname}' saved with {defaultBackpackContents.Count} items.");
                    }
                    break;
                }
            }

            int nextSlot = inventoryData.Count + 1;
            var newSave = new SavedInventory
            {
                Slot = nextSlot,
                Timestamp = DateTime.UtcNow.ToString("o"),
                Inventory = new PlayerInventoryData
                {
                    Wear = ConvertItems(player.inventory.containerWear.itemList),
                    Main = ConvertItems(player.inventory.containerMain.itemList),
                    Belt = ConvertItems(player.inventory.containerBelt.itemList),
                    Backpack = Backpacks?.Call("API_GetBackpackInventory", player.UserIDString) as List<Item> != null
                        ? ConvertItems(Backpacks.Call("API_GetBackpackInventory", player.UserIDString) as List<Item>)
                        : null,
                    DefaultBackpack = defaultBackpackContents,
                    Bags = ConvertBagContents(allBags)
                }
            };

            inventoryData.Insert(0, newSave); // Add new save as the first entry

            // Adjust slot numbers to maintain correct order
            for (int i = 0; i < inventoryData.Count; i++)
            {
                inventoryData[i].Slot = i + 1;
            }

            // Keep only the most recent MaxSaveSlots entries
            if (inventoryData.Count > MaxSaveSlots)
            {
                inventoryData.RemoveAt(MaxSaveSlots);
            }

            Interface.Oxide.DataFileSystem.WriteObject(filePath, inventoryData);
        }

        private void RestorePlayerInventory(string steamId, int slot)
        {
            string dirPath = Path.Combine("InventorySaver", steamId);
            string filePath = Path.Combine(dirPath, "inventory.json");

            if (!Interface.Oxide.DataFileSystem.ExistsDatafile(filePath))
            {
                Puts($"No saved inventories found for {steamId}.");
                return;
            }

            var inventoryData = Interface.Oxide.DataFileSystem.ReadObject<List<SavedInventory>>(filePath);
            if (slot < 1 || slot > inventoryData.Count)
            {
                Puts($"Invalid slot number {slot} for player {steamId}.");
                return;
            }

            var save = inventoryData[slot - 1]; // Adjust for zero-based index

            var player = BasePlayer.FindByID(ulong.Parse(steamId));
            if (player == null || !player.IsConnected)
            {
                Puts($"Player {steamId} is not online.");
                return;
            }

            var data = save.Inventory;
            RestoreItems(player.inventory.containerWear, data.Wear);
            RestoreItems(player.inventory.containerMain, data.Main);
            RestoreItems(player.inventory.containerBelt, data.Belt);

            if (Backpacks != null && data.Backpack != null)
            {
                Backpacks.Call("API_SetBackpackInventory", player.UserIDString, DataToItems(data.Backpack));
            }

            if (data.DefaultBackpack != null)
            {
                var backpack = ItemManager.CreateByItemID(data.DefaultBackpack[0].ItemId, 1);
                if (backpack != null && backpack.contents != null)
                {
                    foreach (var itemData in data.DefaultBackpack)
                    {
                        var item = ItemManager.CreateByItemID(itemData.ItemId, itemData.Amount, itemData.SkinId);
                        item?.MoveToContainer(backpack.contents);
                    }
                    player.inventory.GiveItem(backpack, player.inventory.containerWear);
                    Puts($"[DEBUG] Default backpack restored.");
                }
            }

            if (BagOfHolding != null && data.Bags != null)
            {
                foreach (var bag in data.Bags)
                {
                    var restoredBagContents = new List<Item>();
                    foreach (var itemData in bag.Value)
                    {
                        var item = ItemManager.CreateByItemID(itemData.ItemId, itemData.Amount, itemData.SkinId);
                        if (item != null)
                        {
                            if (itemData.Contents != null && item.contents != null)
                            {
                                foreach (var subItemData in itemData.Contents)
                                {
                                    var subItem = ItemManager.CreateByItemID(subItemData.ItemId, subItemData.Amount, subItemData.SkinId);
                                    subItem?.MoveToContainer(item.contents);
                                }
                            }
                            restoredBagContents.Add(item);
                        }
                    }
                    BagOfHolding.Call("API_SetBagContentsByCategory", player.UserIDString, bag.Key, restoredBagContents);
                }
            }

            Puts($"Inventory from slot {slot} restored for player {steamId}.");
        }

        private Dictionary<string, List<ItemData>> ConvertBagContents(Dictionary<string, List<Item>> allBags)
        {
            var bagData = new Dictionary<string, List<ItemData>>();

            foreach (var bag in allBags)
            {
                bagData[bag.Key] = ConvertItemsWithContents(bag.Value);
            }

            return bagData;
        }

        private List<Item> DataToItems(List<ItemData> dataList)
        {
            var items = new List<Item>();
            foreach (var data in dataList)
            {
                var item = ItemManager.CreateByItemID(data.ItemId, data.Amount, data.SkinId);
                if (item != null && data.Contents != null)
                {
                    foreach (var contentData in data.Contents)
                    {
                        var content = ItemManager.CreateByItemID(contentData.ItemId, contentData.Amount, contentData.SkinId);
                        content?.MoveToContainer(item.contents);
                    }
                }
                items.Add(item);
            }
            return items;
        }

        private void RestoreItems(ItemContainer container, List<ItemData> data)
        {
            container.Clear();
            foreach (var itemData in data)
            {
                var item = ItemManager.CreateByItemID(itemData.ItemId, itemData.Amount, itemData.SkinId);
                if (item != null)
                {
                    item.MoveToContainer(container);
                }
            }
        }

        private List<ItemData> ConvertItems(List<Item> items)
        {
            var itemDataList = new List<ItemData>();
            foreach (var item in items)
            {
                itemDataList.Add(ItemToData(item));
            }
            return itemDataList;
        }

        private List<ItemData> ConvertItemsWithContents(List<Item> items)
        {
            var itemDataList = new List<ItemData>();
            foreach (var item in items)
            {
                itemDataList.Add(ItemToDataWithContents(item));
            }
            return itemDataList;
        }

        private ItemData ItemToData(Item item)
        {
            return new ItemData
            {
                ItemId = item.info.itemid,
                Amount = item.amount,
                SkinId = item.skin,
                Contents = item.contents?.itemList.ConvertAll(ItemToData)
            };
        }

        private ItemData ItemToDataWithContents(Item item)
        {
            return new ItemData
            {
                ItemId = item.info.itemid,
                Amount = item.amount,
                SkinId = item.skin,
                Contents = item.contents?.itemList.ConvertAll(ItemToData)
            };
        }

        private class SavedInventory
        {
            public int Slot { get; set; }
            public string Timestamp { get; set; }
            public PlayerInventoryData Inventory { get; set; }
        }

        private class PlayerInventoryData
        {
            public List<ItemData> Wear { get; set; }
            public List<ItemData> Main { get; set; }
            public List<ItemData> Belt { get; set; }
            public List<ItemData> Backpack { get; set; }
            public List<ItemData> DefaultBackpack { get; set; }
            public Dictionary<string, List<ItemData>> Bags { get; set; }
        }

        private class ItemData
        {
            public int ItemId { get; set; }
            public int Amount { get; set; }
            public ulong SkinId { get; set; }
            public List<ItemData> Contents { get; set; }
        }
    }
}
