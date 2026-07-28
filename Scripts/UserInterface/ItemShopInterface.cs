using Godot;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

public partial class ItemShopInterface : Control
{
	[Export]
	bool useEventShop = false;

	[Export]
	PackedScene shopOfferEntryScene;
	[Export]
	Control shopOfferEntryParent;


	public override void _Ready()
	{
		VisibilityChanged += LoadShop;
		RefreshTimerController.OnDayChanged += MarkDirty;
	}

	public override void _ExitTree()
	{
		RefreshTimerController.OnDayChanged -= MarkDirty;
		//if (linkedStorefront is not null)
		//{
		//	linkedStorefront.OnOfferAdded -= AddShopOffer;
		//	linkedStorefront.OnOfferRemoved -= RemoveShopOffer;
		//}
	}

	private void MarkDirty()
	{
		shopDirty = true;
		LoadShop();
	}

	bool shopDirty = true;
	List<GameOfferEntry> inactiveEntries = [];
	Dictionary<string, GameOfferEntry> activeEntries = [];
	public async void LoadShop()
	{
		if (!shopDirty || !IsVisibleInTree())
			return;
		shopDirty = false;

		var storefront = useEventShop ? GameStorefront.CampaignEvent : GameStorefront.CampaignWeekly;
		await storefront.Fetch();
		var offers = storefront.Offers;

		if (useEventShop)
		{
			var futureItems = Timeline.GetCurrentUpcomingShopItems();
			offers =
			[
				..offers.OrderBy(o => -o.SortPriority),
					..futureItems.Select(tuple=>
						GameItemTemplate
							.Get(tuple.templateId)
							.CreateOffer(rawData:new(){["releaseDate"]=tuple.releaseDate})
					)
			];
		}
		else
		{
			offers = [..offers
					.OrderBy(o => -o.itemGrants?.FirstOrDefault()?.template?.RarityLevel ?? 999)
					.ThenBy(o => -o.BasePrice.quantity)
					.ThenBy(o => -o.WeeklyLimit)
			];
		}

		foreach (var entry in activeEntries.Values)
		{
			entry.Visible = false;
			inactiveEntries.Add(entry);
		}
		activeEntries.Clear();
		for (int i = 0; i < offers.Length; i++)
		{
			AddShopOffer(offers[i]);
		}
	}

	void SpawnShopEntry()
	{
		var newEntry = shopOfferEntryScene.Instantiate<GameOfferEntry>();
		shopOfferEntryParent.AddChild(newEntry);
		inactiveEntries.Add(newEntry);
	}

	void AddShopOffer(GameOffer newOffer)
	{
		if (inactiveEntries.Count <= 0)
			SpawnShopEntry();
		var thisEntry = inactiveEntries[0];
		inactiveEntries.Remove(thisEntry);
		thisEntry.SetOffer(newOffer).StartTask();
		thisEntry.Visible = true;
		thisEntry.MoveToFront();
		activeEntries.Add(newOffer.OfferId, thisEntry);
	}

	void RemoveShopOffer(GameOffer oldOffer)
	{
		if (!activeEntries.TryGetValue(oldOffer.OfferId, out GameOfferEntry entry))
			return;
		entry.Visible = false;
		activeEntries.Remove(oldOffer.OfferId);
		inactiveEntries.Add(entry);
	}
}
