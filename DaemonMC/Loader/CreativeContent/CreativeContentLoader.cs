using System.Text.Json;
using System.Text.Json.Serialization;
using DaemonMC.Items;
using DaemonMC.Items.VanillaItems;
using DaemonMC.Network.Enumerations;
using DaemonMC.Utils.Game;
using fNbt;

namespace DaemonMC.Loader.CreativeContent;

public static class CreativeContentLoader
{
    public static void Load()
    {
        using var stream = File.OpenRead(@"creative_items.json");
        var creativeItems = JsonSerializer.Deserialize<CreativeItemsFile>(stream, new JsonSerializerOptions() {PropertyNameCaseInsensitive = true}) ?? new CreativeItemsFile();
        
        foreach (var groupedItems in creativeItems.Items.GroupBy(item => item.GroupId).OrderBy(g => g.Key))
        {
            var group = creativeItems.Groups[groupedItems.Key];
            var creativeItemGroup = new CreativeItemGroup()
            {
                Name = group.Name,
                Category = Enum.Parse<CreativeCategoryType>(group.Category, true),
                Icon = ItemPalette.GetItem(group.Icon.Id) ?? new Air()
            };

            foreach (var creativeItem in groupedItems)
            {
                var item = ItemPalette.GetItem(creativeItem.Id) ?? new Air();

                if (!string.IsNullOrEmpty(creativeItem.BlockStateB64) && item is not Air && ToDataTypes.Base64ToNbt(creativeItem.BlockStateB64, true, false)?["network_id"] is NbtInt networkId)
                {
                    item.BlockRuntimeId = networkId.Value;
                }

                creativeItemGroup.Items.Add(item);  
            }
            
            CreativeContentManager.AddGroup(creativeItemGroup);
        }
    }
}

public sealed class CreativeItemsFile
{
    [JsonPropertyName("groups")]
    public List<CreativeItemGroupFile> Groups { get; set; } = [];

    [JsonPropertyName("items")]
    public List<CreativeItemFile> Items { get; set; } = [];
}

public sealed class CreativeItemGroupFile
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("category")]
    public string Category { get; set; } = string.Empty;

    [JsonPropertyName("icon")]
    public CreativeItemIconFile Icon { get; set; } = new();
}

public sealed class CreativeItemFile
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("groupId")]
    public int GroupId { get; set; }

    [JsonPropertyName("damage")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int? Damage { get; set; }

    [JsonPropertyName("nbt_b64")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string? NbtB64 { get; set; }

    [JsonPropertyName("block_state_b64")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string? BlockStateB64 { get; set; }
}

public sealed class CreativeItemIconFile
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("block_state_b64")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string? BlockStateB64 { get; set; }

    [JsonPropertyName("nbt_b64")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string? NbtB64 { get; set; }
}
