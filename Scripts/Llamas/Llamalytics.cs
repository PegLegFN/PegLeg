using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

public static class Llamalytics
{
	const string Comment = "Llamalytics locally logs the contents of Llamas and other Card Pack types to a json file in appdata. This only applies to packs viewed or opened from within PegLeg. PegLeg does not share this file automatically, it's up to you if you want to manually share the file, but it contains no identifiable information.";

	class DataFile
	{
		[JsonInclude]
		string comment { get; } = Comment;
		[JsonInclude]
		public Dictionary<string, PackEntry> Packs { get; init; } = [];

		public void MergeFrom(DataFile other)
		{
			if (other == this)
				return;
			foreach (var kvp in other.Packs.Where(kvp=>Packs.ContainsKey(kvp.Key)))
			{
				Packs.Add(kvp.Key, kvp.Value);
			}
		}
	}

	record class PackEntry()
	{
		public bool? xRay { get => field == false ? null : field; init; }
		public string type { get; init; }
		public int? displayLevel { get; init; }
		public string tierGroup { get; init; }
		public int? tier { get => field == -1 ? null : field; init; }
		public int? highestRarity { get => field == 0 ? null : field; init; }
		public int? overrideTier { get => field == -1 ? null : field; init; }
		public int? packLevel { get => field == 1 ? null : field; init; }
		public int? occurences { get => field == 1 ? null : field; init; }
		public Dictionary<string, int> fixedRewards { get; init; }
		public string[][] choiceRewards { get; init; }
	}

	static DataFile currentData = new();
	static JsonSerializerOptions sOptions = new()
	{
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
		WriteIndented = true
	};

	public static void TryAddPreroll(GameItem prerollData)
	{
		if (prerollData is null)
			return;
		var offerId = prerollData.attributes["offerId"].ToString();
		if (offerId == LlamaSelector.TokenUpgradeId)
			return;//prevent duplicate entries for upgrade llamas (50 tickets/1 token)
		TryLoadPacks();
		if (currentData.Packs.ContainsKey(prerollData.uuid))
			return;
		try
		{
			var items = prerollData.attributes["items"].Deserialize<GameItem.ItemReward[]>() ?? [];
			var (fixedRewards, choiceRewards) = ConvertItems(items);
			currentData.Packs.Add(prerollData.uuid, new()
			{
				xRay = true,
				type = GameStorefront.GetExistingOffer(offerId).itemGrants[0].templateId,
				packLevel = prerollData.attributes["level"].GetValue<int>(),
				highestRarity = prerollData.attributes["highest_rarity"].GetValue<int>(),
				fixedRewards = fixedRewards,
				choiceRewards = choiceRewards
			});
			SavePacks();
		}
		catch { }
	}

	public static void TryAddCardpack(GameItem pack, JsonObject resultNotification)
	{
		if (pack is null || resultNotification is null)
			return;
		TryLoadPacks();
		try
		{
			var items = resultNotification["lootGranted"]["items"].Deserialize<GameItem.ItemReward[]>();
			var (fixedRewards, choiceRewards) = ConvertItems(items);
			PackEntry entry = new()
			{
				type = pack.templateId,
				packLevel = pack.attributes["level"].GetValue<int>(),
				displayLevel = resultNotification["displayLevel"].GetValue<int>(),
				tierGroup = resultNotification["tierGroupName"]?.GetValue<string>(),
				tier = resultNotification["tier"]?.GetValue<int>(),
				overrideTier = resultNotification["overrideTier"]?.GetValue<int>(),
				fixedRewards = fixedRewards,
				choiceRewards = choiceRewards
			};
			var hash = $"h:{JsonSerializer.Serialize(entry).GetHashCode()}";
			if (currentData.Packs.TryGetValue(hash, out var existing))
				entry = entry with { occurences = (existing.occurences ?? 1) + 1 };
			currentData.Packs[hash] = entry;
			SavePacks();
		}
		catch { }
	}

	static (Dictionary<string, int> fixedRewards, string[][] choiceRewards) ConvertItems(GameItem.ItemReward[] items)
	{
		var fixedRewards = items
					.Where(i => !i.itemType.StartsWith("CardPack:"))
					.GroupBy(i => i.itemType)
					.ToDictionary(
						g => g.Key,
						g => g.Sum(i => i.quantity)
					);
		if (fixedRewards.Count == 0)
			fixedRewards = null;
		var choiceRewards = items
			.Where(i => i.itemType.StartsWith("CardPack:"))
			.Select(i => i.CreateItem().CardPackChoices.Select(i => i.templateId).ToArray())
			.ToArray();
		if (choiceRewards.Length == 0)
			choiceRewards = null;
		return (fixedRewards, choiceRewards);
	}

	const string saveFolder = "user://Llamalytics/";
	static readonly string savePath = $"{saveFolder}Llamas_{DateTime.UtcNow.Year}_{DateTime.UtcNow.Month}.json";

	static void TryLoadPacks()
	{
		if (!FileAccess.FileExists(savePath))
			return;
		using var saveFile = FileAccess.Open(savePath, FileAccess.ModeFlags.Read);
		currentData = JsonSerializer.Deserialize<DataFile>(saveFile.GetAsText(), sOptions);
	}

	static void SavePacks()
	{
		if (currentData.Packs.Count == 0)
			return;
		if (!DirAccess.DirExistsAbsolute(saveFolder))
			DirAccess.MakeDirAbsolute(saveFolder);
		using var saveFile = FileAccess.Open(savePath, FileAccess.ModeFlags.Write);
		saveFile.StoreString(JsonSerializer.Serialize(currentData, sOptions));
	}
}
