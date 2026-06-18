using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

public partial class BRSpriteController : Control
{
	[Export]
	Control spriteDustLayout;
	[Export]
	Label spriteDustCount;
	[Export]
	NewRecycleListContainer activeSpriteContainer;
	[Export]
	NewRecycleListContainer lostSpriteContainer;

	string spriteURL;

	public override void _Ready()
	{
		activeSpriteContainer.LinkListProvider(activeSprites);
		lostSpriteContainer.LinkListProvider(lostSprites);
		if (PegLegResourceManager.MagicNumbers["spriteData"]?["overrideURL"]?.ToString() is string overrideUrl)
			spriteURL = overrideUrl;
		else
		{
			var deployment = PegLegResourceManager.MagicNumbers["spriteData"]?["deployment"]?.ToString() ?? "62a9473a2dca46b29ccf17577fcf42d7";
			var module = PegLegResourceManager.MagicNumbers["spriteData"]?["module"]?.ToString() ?? "70329e8f-f377-4a73-90cf-76b7ace87a07";
			spriteURL = $"https://gc.svc.live.fngw.ol.epicgames.com/api/magpie/v2/deployment/{deployment}/domain/FN1/account/:accountId/workspace/default/linkMode/live/inventory?moduleFilters={module}:*&includeMetadata=true";
		}
		GameAccount.ActiveAccountChanged += ActiveAccountChanged;
		ActiveAccountChanged();
	}

	EntryList<GameItem> activeSprites = [];
	EntryList<GameItem> lostSprites = [];

	private async void ActiveAccountChanged()
	{
		if (!GameAccount.ActiveAccount.isOwned)
			return;
		var acc = GameAccount.ActiveAccount;
		var result = await WebHelpers.MakeRequest(spriteURL.Replace(":accountId", acc.accountId))
			.SetAccount(acc, useEOS: true)
			.Send();
		if (await result.CheckForError())
			return;
		var data = await result.ReadJson<MagpieData>();
		var itemTuples = data.inventory[0].ToTupleArray();
		var currency = itemTuples.FirstOrDefault(i => i.templateName.StartsWith("Currency_", StringComparison.OrdinalIgnoreCase));
		var sprites = itemTuples.Except([currency]).ToArray();
		spriteDustCount.Text = currency.quantity.Notate();

		activeSprites.Clear();
		activeSprites.AddRange(sprites.Where(i => i.quantity == 2).Select(ConvertSprite));

		lostSprites.Clear();
		lostSprites.AddRange(sprites.Where(i => i.quantity == 1).Select(ConvertSprite));

		activeSpriteContainer.MarkListDirty();
		lostSpriteContainer.MarkListDirty();
	}

	static GameItem ConvertSprite((string templateName, int quantity, JsonObject metadata) i) => 
		new(
			GameItemTemplate.Get($"ExtractableSprite:{i.templateName}"), 
			1, 
			i.metadata.SafeDeepClone(), 
			templateId: $"ExtractableSprite:{i.templateName}"
		);

	record struct MagpieData
	{
		public string accountId { get; init; }
		public string deploymentId { get; init; }
		public string domain { get; init; }
		public Inventory[] inventory { get; init; }
		public string linkMode { get; init; }
		public string workspace { get; init; }

		public record struct Inventory
		{
			public Dictionary<string, int> counts { get; init; }
			public Dictionary<string, string> entitlementMetadata { get; init; }
			public (string templateName, int quantity, JsonObject metadata)[] ToTupleArray()
			{
				var allKeys = counts.Keys.Union(entitlementMetadata.Keys).Distinct();
				return [.. allKeys.Select(TupleFromKey)];
			}

			(string, int, JsonObject) TupleFromKey(string k) =>
			(
				k, 
				counts.TryGetValue(k, out var q) ? q : 0, 
				entitlementMetadata.TryGetValue(k, out var a) ? JsonSerializer.Deserialize<JsonObject>(a) : default
			);

			public string metadata { get; init; }
			public int metadataSchemaVersion { get; init; }
			public string moduleId { get; init; }
			public bool purchasedEntitlementConsequentialToGameplay { get; init; } //based on this, is this the system UEFN uses for ingame purchases?

		}
	}
}
