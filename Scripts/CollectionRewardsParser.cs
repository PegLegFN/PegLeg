using Godot;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

public partial class CollectionRewardsParser : Control
{
	static CollectionLevel[] collectionRewards;

	[Export]
	string[] displayTemplates;

	[Export]
	public PackedScene rewardScene;

	[Export]
	public Control rewardParent;

	public override void _Ready()
	{
		var doc = PegLegResourceManager.LoadResourceArray("timeline.json");
		collectionRewards = [.. doc[0]["Rows"]
			.Deserialize<Dictionary<string, CollectionLevel>>(Helpers.JsonOptions.Fields)
			.Select(kvp=>kvp.Value with { level = int.Parse(kvp.Key)})
			.OrderBy(cbl=>cbl.level)
		];

		var grouped = collectionRewards.GroupBy(cbl => cbl.Rewards.StandardRewards.FirstOrDefault().ItemData.templateId);
		var groupedDict = grouped.ToDictionary(g => g.Key, g => g.Select(r => r.level).Order().ToArray());
		for (int i = 0; i < displayTemplates.Length; i++)
		{
			if (!groupedDict.TryGetValue(displayTemplates[i], out var rewardLevels))
				continue;
			var entry = rewardScene.Instantiate<CollectionRewardsEntry>();
			rewardParent.AddChild(entry);
			entry.SetItem(GameItemTemplate.Get(displayTemplates[i]).CreateInstance(), rewardLevels);
		}
	}

	public record struct CollectionLevel
	{
		public int level;
		public CollectionRewardGroups Rewards;
		public struct CollectionRewardGroups
		{
			public CollectionReward[] SelectableRewards;
			public CollectionReward[] StandardRewards;
			public CollectionReward[] HiddenRewards;
			public CollectionReward[] PremiumRewards;
		}
	}

	public struct CollectionReward
	{
		[JsonInclude]
		PrimaryAsset ItemPrimaryAssetId;
		[JsonInclude]
		int Quantity;

		[JsonIgnore]
		public GameItem.ItemData ItemData => new(ItemPrimaryAssetId.TemplateId, Quantity);
		struct PrimaryAsset
		{
			[JsonInclude]
			PrimaryAssetTypeData PrimaryAssetType;
			[JsonInclude]
			string PrimaryAssetName;
			struct PrimaryAssetTypeData
			{
				[JsonInclude]
				public string Name;
			}

			[JsonIgnore]
			public string TemplateId => $"{PrimaryAssetType.Name}:{PrimaryAssetName}";
		}
	}
}
