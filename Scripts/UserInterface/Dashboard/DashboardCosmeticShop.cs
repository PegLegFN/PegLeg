using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class DashboardCosmeticShop : Control
{
	[Export]
	CosmeticOfferEntryNew[] cosmeticEntries;
	[Export]
	Button moreButton;
	[Export]
	Control buffering;
	public override void _Ready()
	{
		moreButton.Pressed += CosmeticShopInterfaceNew.GoToTab;
		VisibilityChanged += LoadShop;
		RefreshTimerController.OnDayChanged += MarkDirty;
		MarkDirty();
	}

	public override void _ExitTree()
	{
		RefreshTimerController.OnDayChanged -= MarkDirty;
	}

	private void MarkDirty()
	{
		shopDirty = true;
		LoadShop();
	}

	static HashSet<string> priorityTypes =
	[
		"AthenaCharacter",
		"AthenaBackpack",
		"CosmeticShoes",
		"AthenaPickaxe",
		"AthenaDance",
		"AthenaSpray",
		"AthenaItemWrap",
		"CosmeticMimosa",
		"CosmeticCompanion",
		"Sidekick"
	];

	bool shopDirty = true;
	async void LoadShop()
	{
		if (!shopDirty || !IsVisibleInTree())
			return;
		shopDirty = false;

		for (int i = 0; i < cosmeticEntries.Length; i++)
		{
			cosmeticEntries[i].Visible = false;
		}
		buffering.Visible = true;

		await GameStorefront.FetchCosmeticDependancies();
		var groupedOffers = CosmeticShopInterfaceNew.GetGroupedOffers();
		GameOffer[] sortedOffers = [.. groupedOffers.SelectMany(g => g.rows.SelectMany(r => r.offers))];

		GameOffer[] newOffers =
		[.. sortedOffers
			.Where(o => o.CosmeticTimeData.isAddedToday && o.CosmeticLayoutId != "alc.0")
			.OrderByDescending(o => o.itemGrants.Any(i => priorityTypes.Contains(i.templateId.Split(":")[0])))
			//.ThenByDescending(o => o.BasePrice.quantity)
		];

		buffering.Visible = false;

		moreButton.Visible = newOffers.Length > cosmeticEntries.Length;
		var diff = newOffers.Length - cosmeticEntries.Length;
		moreButton.Text = $"+ {diff} More";

		for (int i = 0; i < Mathf.Min(newOffers.Length, cosmeticEntries.Length); i++)
		{
			cosmeticEntries[i].Visible = true;
			cosmeticEntries[i].SetOffer(newOffers[i]);
		}
	}
}
