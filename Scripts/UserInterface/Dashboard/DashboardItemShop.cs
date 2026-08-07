using Godot;
using System;
using System.Linq;

public partial class DashboardItemShop : Control
{
	[Export]
	GameOfferEntry[] offerEntries;
	[Export]
	bool eventShop;

	public override void _Ready()
	{
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

	bool shopDirty = true;
	async void LoadShop()
	{
		if (!shopDirty || !IsVisibleInTree())
			return;
		shopDirty = false;

		for (int i = 0; i < offerEntries.Length; i++)
		{
			offerEntries[i].ClearOffer();
			offerEntries[i].Visible = false;
		}
		var storefront = eventShop ? GameStorefront.CampaignEvent : GameStorefront.CampaignWeekly;
		await storefront.Fetch();

		if (eventShop)
			SetEventItems(storefront.Offers);
		else
			SetWeeklyItems(storefront.Offers);
	}

	void SetWeeklyItems(GameOffer[] offers)
	{
		if ((offers?.Length ?? 0) == 0)
			return;
		var boostedAccountResource = offers
			.Where(o => o.itemGrants.FirstOrDefault()?.templateId.StartsWith("AccountResource") == true)
			.GroupBy(o => o.itemGrants[0].templateId)
			.FirstOrDefault(g => g.Count() > 1)?
			.OrderByDescending(o => o.itemGrants[0].quantity)
			.FirstOrDefault() ??
			offers.FirstOrDefault(o => o.itemGrants.FirstOrDefault()?.templateId == "AccountResource:reagent_alteration_upgrade_sr");
		var topSchematic = offers
			.Where(o => o.itemGrants.FirstOrDefault()?.templateId.StartsWith("Schematic") == true)
			.OrderByDescending(o => o.SortPriority)
			.FirstOrDefault();
		offerEntries[0].SetOffer(boostedAccountResource).StartTask();
		offerEntries[0].Visible = boostedAccountResource is not null;
		offerEntries[1].SetOffer(topSchematic).StartTask();
		offerEntries[1].Visible = topSchematic is not null;
	}

	void SetEventItems(GameOffer[] offers)
	{
		if ((offers?.Length ?? 0) == 0)
			return;
		var newItemTypes = Timeline.GetLatestShopItems().ToHashSet();
		var newItemOffers = offers
			.Where(o => newItemTypes.Contains(o.itemGrants.FirstOrDefault()?.templateId))
			.ToArray();
		for (int i = 0; i < Mathf.Min(newItemOffers.Length, offerEntries.Length); i++)
		{
			offerEntries[i].SetOffer(newItemOffers[i]).StartTask();
			offerEntries[i].Visible = true;
		}
	}
}
