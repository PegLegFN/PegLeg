using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

public partial class LlamaSelector : Control
{
	public const string TicketUpgradeId = "4D64CBE3618D41FBB5CAD0E472F4610A";
	public const string TokenUpgradeId = "D2E08EFA731D437B85B7340EB51A5E1D";

	event Action<GameOffer> OnOfferSelected;
	event Action<GameItem[]> OnItemsSelected;
	event Action OnSelectionCleared;

	[ExportGroup("Scenes")]
	[Export]
	PackedScene catalogLlamaEntryScene;

	[Export]
	PackedScene cardpackLlamaEntryScene;

	[ExportGroup("Nodes")]
	[Export]
	Control offerListLoadingIcon;

	[Export]
	Control offerListErrorPanel;

	[Export]
	Control llamaOfferParent;

	[Export]
	Control llamaItemEntryPanel;

	[Export]
	Control llamaItemEntryParent;

	[Export]
	LlamaPreview mainLlamaPreview;

	[Export]
	LlamaPreview screenshotLlamaPreview;

	List<GameOfferEntry> llamaOfferEntries = [];
	Queue<CardPackEntry> llamaItemEntryPool = [];
	Dictionary<string, CardPackEntry> inventoryLlamaEntries = [];

	GameProfile llamaItemProfile;
	Dictionary<string, CardPackStack> llamaItemStacks = [];
	Dictionary<string, GameOffer> activeOffers = [];

	public override void _Ready()
	{
		VisibilityChanged += LoadShopLlamas;
		RefreshTimerController.OnHourChanged += ForceLoadShopLlamas;
		GameAccount.ActiveAccountChanged += OnAccountChanged;
		OnAccountChanged();
	}

	public override void _ExitTree()
	{
		RefreshTimerController.OnHourChanged -= ForceLoadShopLlamas;
		GameAccount.ActiveAccountChanged -= OnAccountChanged;
		if (llamaItemProfile is not null)
		{
			llamaItemProfile.OnItemAdded -= AddItem;
			llamaItemProfile.OnItemRemoved -= RemoveLlamaItem;
		}
	}


	CancellationTokenSource accountChangeCTS;
	private async void OnAccountChanged()
	{
		accountChangeCTS = accountChangeCTS.CancelAndRegenerate(out var ct);

		ForceLoadShopLlamas();

		//disconnect prev profile
		if (llamaItemProfile is not null)
		{
			llamaItemProfile.OnItemAdded -= AddItem;
			llamaItemProfile.OnItemRemoved -= RemoveLlamaItem;
			llamaItemProfile = null;
		}

		var newLlamaItemProfile = await GameAccount.ActiveAccount.GetProfile(FnProfileTypes.AccountItems).Query();
		if (ct.IsCancellationRequested)
			return;

		//apply new data synchronously
		foreach (var offerEntry in llamaOfferEntries)
		{
			offerEntry.Visible = offerEntry.GetMeta("llamaFilter", false).AsBool();
		}

		//load new items
		var newLlamaItems = newLlamaItemProfile.GetItems("CardPack");
		foreach (var kvp in inventoryLlamaEntries)
		{
			llamaItemEntryPool.Enqueue(kvp.Value);
		}
		inventoryLlamaEntries.Clear();
		llamaItemStacks.Clear();
		llamaItemEntryPanel.Visible = false;
		//if (newLlamaItems is not null)
		//    GD.Print(newLlamaItems.Select(i=>i.templateId).ToArray());
		foreach (var item in newLlamaItems)
		{
			AddItem(item);
		}

		//connect new profile
		llamaItemProfile = newLlamaItemProfile;
		llamaItemProfile.OnItemAdded += AddItem;
		llamaItemProfile.OnItemRemoved += RemoveLlamaItem;
	}

	#region Cardpack Stack stuff
	class CardPackStack
	{
		public readonly string templateId;
		public readonly string customType;
		public readonly bool isKnown;
		public readonly List<GameItem> items;

		public GameItem DisplayItem { get; private set; }

		public CardPackStack(GameItem firstItem)
		{
			templateId = firstItem.templateId;

			if (firstItem.template.DisplayName.Contains("Accolade"))
				customType = "Accolade";

			isKnown = firstItem.attributes.ContainsKey("options");

			items = [firstItem];
			DisplayItem = GameItemTemplate.Get(templateId).CreateInstance().SetUUID();
			UpdateDisplayItem();
		}

		public bool Has(GameItem item) => items.Contains(item);
		public bool Has(string uuid) => items.Any(val => val.uuid == uuid);
		public int DisplayAmount => isKnown ? -1 : items.Count;

		public bool IsStackable(GameItem item)
		{
			if (item.attributes.ContainsKey("options"))
				return false;
			if (templateId == item.templateId)
				return true;
			if (item.template.DisplayName.Contains("Accolade") && customType == "Accolade")
				return true;
			return false;
		}

		public void AddItem(GameItem item)
		{
			items.Add(item);
			UpdateDisplayItem();
		}

		public void RemoveItem(GameItem item)
		{
			items.Remove(item);
			if (items.Count > 0)
				UpdateDisplayItem();
		}

		void UpdateDisplayItem()
		{
			DisplayItem.SetLocalQuantity(items.Count);
			DisplayItem.NotifyChanged();
		}
	}

	void AddItem(GameItem item)
	{
		if (item?.template?.Type != "CardPack")
			return;
		llamaItemEntryPanel.Visible = true;
		var stackableGroup = llamaItemStacks.Values.FirstOrDefault(val => val.IsStackable(item));

		if (stackableGroup is not null)
		{
			stackableGroup.AddItem(item);
			return;
		}

		CardPackEntry newEntry;
		if (llamaItemEntryPool.Count > 0)
		{
			//pull from queue
			newEntry = llamaItemEntryPool.Dequeue();
			newEntry.Visible = true;
		}
		else
		{
			//spawn new
			newEntry = cardpackLlamaEntryScene.Instantiate<CardPackEntry>();
			llamaItemEntryParent.AddChild(newEntry);
			newEntry.LlamaPressed += SelectLlamaItem;
		}

		newEntry.MoveToFront();
		CardPackStack llamaStack = new(item);
		newEntry.SetItem(llamaStack.DisplayItem);
		llamaItemStacks.Add(llamaStack.DisplayItem.uuid, llamaStack);
		inventoryLlamaEntries.Add(llamaStack.DisplayItem.uuid, newEntry);
	}

	void RemoveLlamaItem(GameItem item)
	{
		if (item?.template?.Type != "CardPack")
			return;
		var llamaStack = llamaItemStacks.Values.FirstOrDefault(val => val.Has(item));
		if (llamaStack is not null)
		{
			llamaStack.RemoveItem(item);
			if (llamaStack.items.Count == 0)
			{
				var entry = inventoryLlamaEntries[llamaStack.DisplayItem.uuid];
				inventoryLlamaEntries.Remove(llamaStack.DisplayItem.uuid);
				llamaItemStacks.Remove(llamaStack.DisplayItem.uuid);
				llamaItemEntryPool.Enqueue(entry);
				if (llamaItemStacks.Count == 0)
					llamaItemEntryPanel.Visible = false;
			}
		}
	}

	#endregion

	CancellationTokenSource llamaShopCTS;
	SemaphoreSlim llamaShopSemaphore = new(1);
	async void LoadShopLlamas() => await LoadShopLlamasAsync();
	async void ForceLoadShopLlamas() => await LoadShopLlamasAsync(true);
	bool llamasDirty = false;
	async Task LoadShopLlamasAsync(bool force = false)
	{
		if (force)
			llamasDirty = true;
		if (!IsVisibleInTree() || (!llamasDirty && activeOffers.Count > 0))
			return;
		llamasDirty = false;
		llamaShopCTS = llamaShopCTS.CancelAndRegenerate(out var ct);

		offerListLoadingIcon.Visible = true;
		llamaOfferParent.Visible = false;
		offerListErrorPanel.Visible = false;

		activeOffers.Clear();

		bool success = false;
		try
		{
			await llamaShopSemaphore.WaitAsync(ct);
			if (ct.IsCancellationRequested)
				return;

			var xrayStorefront = await GameStorefront.XRayLlamas.Fetch(force);
			var randomStorefront = await GameStorefront.RandomLlamas.Fetch(force);
			if (ct.IsCancellationRequested)
				return;
			await GameAccount.ActiveAccount.GenerateXRayLlamaResults();

			int catalogEntryIndex = 0;
			var allOffers = xrayStorefront.Offers.Union(randomStorefront.Offers);
			List<GameOffer> filteredOffers = [];
			foreach (var offer in allOffers)
			{
				if (await LlamaOfferFilter(offer))
					filteredOffers.Add(offer);
				if (ct.IsCancellationRequested)
					return;
			}

			//remove token-based Upgrade Llama, it will be merged into the ticket-based llama
			var tokenUpgradeOffer = GameStorefront.GetExistingOffer(TokenUpgradeId);
			filteredOffers.Remove(tokenUpgradeOffer);

			foreach (var offer in filteredOffers)
			{
				if (llamaOfferEntries.Count <= catalogEntryIndex)
				{
					var newEntry = catalogLlamaEntryScene.Instantiate<GameOfferEntry>();
					llamaOfferParent.AddChild(newEntry);
					newEntry.Pressed += SelectLlamaOffer;
					llamaOfferEntries.Add(newEntry);
				}
				activeOffers.TryAdd(offer.OfferId, offer);
				var thisEntry = llamaOfferEntries[catalogEntryIndex];
				thisEntry.Visible = true;
				thisEntry.SetOffer(offer).StartTask();
				(thisEntry.GetNode("%AltPrice") as Control).Visible = offer.OfferId == TicketUpgradeId;
				catalogEntryIndex++;
			}

			for (int i = catalogEntryIndex; i < llamaOfferEntries.Count; i++)
			{
				llamaOfferEntries[i].Visible = false;
			}

			success = true;
		}
		finally
		{
			llamaShopSemaphore.Release();
			if (!ct.IsCancellationRequested)
			{
				offerListLoadingIcon.Visible = false;
				llamaOfferParent.Visible = success;
				offerListErrorPanel.Visible = !success;
			}
		}
	}

	static async Task<bool> LlamaOfferFilter(GameOffer offer)
	{
		if (offer.OfferId == TokenUpgradeId)
			return true;

		var account = GameAccount.ActiveAccount;

		if (!await account.MatchesFulfillmentRequirements(offer))
			return false;

		string priceTemplateId = offer.Price?.templateId;
		int price = offer.Price?.quantity ?? 0;
		if (price == 1)
		{
			var profile = await account.GetProfile(FnProfileTypes.AccountItems).Query();
			return profile.GetFirstTemplateItem(priceTemplateId) is not null;
		}

		return true;
	}

	void SelectLlamaOffer(string offerId)
	{
		//move to llama interface
		var offer = GameStorefront.GetExistingOffer(offerId);
		OnOfferSelected?.Invoke(offer);
		mainLlamaPreview?.SetLlamaOffer(offer);
		screenshotLlamaPreview?.SetLlamaOffer(offer);
		if (mainLlamaPreview is null)
			LlamaPreview.ShowLlamaOffer(offer);
	}

	void SelectLlamaItem(string stackUuid)
	{
		//move to llama interface
		var items = llamaItemStacks[stackUuid].items.ToArray();
		OnItemsSelected?.Invoke(items);
		mainLlamaPreview?.SetLlamaItems(items);
		screenshotLlamaPreview?.SetLlamaItems(items);
		if (mainLlamaPreview is null)
			LlamaPreview.ShowLlamaItems(items);
	}

	void SelectEmpty()
	{
		//move to llama interface
		OnSelectionCleared?.Invoke();
		mainLlamaPreview?.ClearPreview();
		screenshotLlamaPreview?.ClearPreview();
		//if main preview is null, try use overlay
	}

	static GameItem allTheLlamas;
	public async void BulkOpenAllCardpacks()
	{
		GameItem[] allCardpacks = [.. llamaItemStacks.SelectMany(stack => stack.Value.items)];
		var selectedUUIDs = mainLlamaPreview?.currentCardpacks.Select(c => c.uuid).ToHashSet();
		bool includesSelected = allCardpacks.Any(i => selectedUUIDs.Contains(i.uuid));

		allTheLlamas ??= GameItemTemplate.Get("CardPack:cardpack_bronze_10x").CreateInstance();
		await CardPackOpener.Instance.StartOpening([.. allCardpacks], mainLlamaPreview?.llamaPanel, allTheLlamas);

		if (includesSelected)
		{
			mainLlamaPreview.ClearPreview();
			screenshotLlamaPreview?.ClearPreview();
		}
	}
}
