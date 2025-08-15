using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.System.Threading;
using Lumina.Excel.Sheets;
using Lumina.Extensions;
using MarketList.RawInformation;
using MarketList.Universalis;

namespace MarketList.Windows;

public class MainWindow : Window, IDisposable
{
    private Plugin Plugin;
    private UniversalisClient Universalis;
    private MarketboardData? MarketboardData;

    // We give this window a hidden ID using ##.
    // The user will see "My Amazing Window" as window title,
    // but for ImGui the ID is "My Amazing Window##With a hidden ID"
    public MainWindow(Plugin plugin)
        : base("MarketList helper##marketlistmain", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(375, 330),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };
        
        Plugin = plugin;
        Universalis = new UniversalisClient();
    }

    public void Dispose() { }

    public override void Draw()
    {
        if (ImGui.Button("Check items (import from clipboard)"))
        {
            CheckItemsFromClipboard();
        }

        ImGui.Spacing();
        
        if (ImGui.Button("Check profitability"))
        {
            CheckProfitability();
        }
    }

    private async void CheckItemsFromClipboard()
    {
        var clipboard = ImGui.GetClipboardText();
        if (clipboard == string.Empty) return;

        var items = GetItems(clipboard);
        if (items.Count == 0) return;
        
        // Loop items and check Universalis prices on DC
        Universalis.PlayerWorld = Svc.ClientState.LocalPlayer?.CurrentWorld.RowId;
        var allItems = new List<MinItemPriceInfo>();
        var tasks = new List<Task>();
        var batchSize = 8;
        var numberOfBatches = Math.Ceiling((double)items.Count / batchSize);
        Plugin.Log.Info($"Need to lookup {items.Count} items in {numberOfBatches} batches");
        
        // Batch requests in groups of 15
        // This is not a robust way to deal with large numbers of items, but it should stop it breaking for now
        for (var i = 0; i < numberOfBatches; i++)
        {
            var batch = items.GetRange(i * batchSize, Math.Min(batchSize, items.Count - i * batchSize));
            
            foreach (var item in batch)
            {
                var newTask = new Task(() => GetLowestPriceWorld(item, allItems));
                tasks.Add(newTask);
                newTask.Start();
            }
            
            await Task.WhenAll(tasks.ToArray());
            System.Threading.Thread.Sleep(500);
        }
        
        Plugin.Log.Info($"Found {allItems.Count} items");
        
        var worldItems = allItems.GroupBy(item => item.LowestWorld).ToDictionary(item => item.Key, item => item.ToList());
        
        Plugin.Log.Info($"Found {worldItems.Count} worlds");

        // Build output
        var result = new StringBuilder();
        foreach (var world in worldItems.Keys.Order())
        {
            result.AppendLine(world);
            
            foreach (var item in worldItems[world])
            {
                result.AppendLine($"{item.Name} - {item.CurrentMinimumPrice} ({item.Quantity})");
            }
            
            result.AppendLine();
        }

        ImGui.SetClipboardText(result.ToString());
    }

    private static List<ItemQuantity> GetItems(string items)
    {
        // Parse items from raw text
        // Items are of the format exported by Artisan, so we can split at \n and x characters to extract the names
        // 3x Gold Ingot
        // TODO parsing error warnings
        var lines = items.Split("\n")
                                    .Where(line => !string.IsNullOrWhiteSpace(line))
                                    .Select(ExtractItemNameQuantity)
                                    .Where(tuple => tuple != null)
                                    .ToList();
        
        return lines.Select(line => GetItemByName(line.Name, line.Quantity))
                                                     .Where(item => item != null)
                                                     .ToList();
    }

    // TODO why can multiple item names be the same?
    // Check against marketable item ids?
    private static ItemQuantity? GetItemByName(string itemName, int quantity)
    {
        var item = LuminaSheets.ItemLookup.Contains(itemName) ? LuminaSheets.ItemLookup[itemName].FirstOrNull(item => !item.IsUntradable) : null;
        if (item == null) return null;

        return new ItemQuantity()
        {
            Item = item!.Value,
            Quantity = quantity
        };
    }

    private static ItemNameQuantity? ExtractItemNameQuantity(string line)
    {
        try
        {
            var index = line.IndexOf('x');
            if (index != -1 && index + 1 < line.Length)
            {
                var quantity = int.Parse(line.Substring(0, index).Trim());
                var name = line.Substring(index + 1).Trim();

                return new ItemNameQuantity { Name = name, Quantity = quantity };
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private void GetLowestPriceWorld(ItemQuantity item, List<MinItemPriceInfo> allItems)
    {
        Universalis.GetDCData(item.Item.RowId, ref MarketboardData);
        if (MarketboardData == null)
        {
            return;
        }

        var world = MarketboardData.LowestWorld;
        var price = MarketboardData.CurrentMinimumPrice;
        if (world == null || price == null)
        {
            return;
        }

        var newItem = new MinItemPriceInfo()
        {
            ItemId = item.Item.RowId,
            Name = item.Item.Name.ToString(),
            LowestWorld = world,
            CurrentMinimumPrice = (double)price,
            Quantity = item.Quantity
        };

        allItems.Add(newItem);
    }

    private void CheckProfitability()
    {
        // do nothing
    }
}
