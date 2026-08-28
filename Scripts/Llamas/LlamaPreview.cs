using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

public partial class LlamaPreview : Control
{
	static LlamaPreview overlayInstance;

	[Export]
	bool interactive = true;

	[ExportGroup("Scenes")]
	[Export]
	PackedScene itemEntryScene;

	[ExportGroup("Nodes")]
	[Export]
	ModalWindow overlayWindow;

	[Export]
	public Control llamaPanel { get; private set; }

	[Export]
	GameOfferEntry currentOfferEntry;

	[Export]
	CardPackEntry currentCardpackEntry;

	[Export]
	Control purchaseButton;

	[Export]
	Control openButton;

	[Export]
	SpinBox quantitySpinner;

	[Export]
	Control resultEntriesParent;

	[Export]
	Control surpriseResultPanel;

	[Export]
	Control soldOutResultPanel;

	[Export]
	Control choicePanel;

	[Export]
	GameItemEntry firstChoice;

	[Export]
	GameItemEntry secondChoice;

	GameOffer currentOffer;
	GameOffer tokenUpgradeOffer;
	bool useTokenOffer = false;
	public GameItem[] currentCardpacks { get; private set; }
	GameItem topCardpack;

	CancellationTokenSource offerCts;
	List<GameItemEntry> llamaResultEntries = [];

	//accept individual GameOffers and arrays of GameItems
	//remove game items after opening them
	//clear selection when items deplete or offer is disconnected

	public override void _Ready()
	{
		if (overlayWindow is not null)
		{
			overlayInstance = this;
			overlayWindow.WindowClosed += ClearPreview;
		}
		quantitySpinner?.ValueChanged += OnQuantityChanged;
		ClearPreview();
		CardPackOpener.OnLlamaOpeningComplete += RefreshLlama;
	}

	public override void _ExitTree()
	{
		if (overlayInstance == this)
			overlayInstance = null;
		CardPackOpener.OnLlamaOpeningComplete += RefreshLlama;
	}

	public static void ShowLlamaOffer(GameOffer offer)
	{
		if (overlayInstance is null)
			return;
		overlayInstance.overlayWindow.SetWindowOpen(true);
		overlayInstance.SetLlamaOffer(offer);
	}

	public static void ShowLlamaItems(GameItem[] items)
	{
		if (overlayInstance is null)
			return;
		overlayInstance.overlayWindow.SetWindowOpen(true);
		overlayInstance.SetLlamaItems(items);
	}

	private void RefreshLlama()
	{
		if (currentOffer is not null)
			SetLlamaOffer(currentOffer);
		else if (currentCardpacks is not null)
		{
			currentCardpacks = [.. currentCardpacks.Where(c => c.profile is not null)];
			SetLlamaItems(currentCardpacks);
		}
	}

	public void ClearPreview()
	{
		offerCts?.Cancel();
		useTokenOffer = false;

		currentOfferEntry?.ClearOffer();
		currentCardpackEntry?.ClearItem();

		openButton?.Visible = false;
		purchaseButton?.Visible = false;
		quantitySpinner?.Visible = false;

		resultEntriesParent?.Visible = false;
		surpriseResultPanel?.Visible = false;
		soldOutResultPanel?.Visible = false;
		choicePanel?.Visible = false;

		currentOffer = null;
		currentCardpacks = null;
	}

	public async void SetLlamaOffer(GameOffer offer)
	{
		ClearPreview();

		if (offer is null)
		{
			GD.PushWarning("Null Offer");
			return;
		}

		offerCts = offerCts.CancelAndRegenerate(out var ct);

		//show loading icon
		var account = GameAccount.ActiveAccount;
		if (!await account.Authenticate() || ct.IsCancellationRequested)
			return;

		//if this is ticket-based Upgrade Llama, show token-based Upgrade Llama instead
		if (offer.OfferId == LlamaSelector.TicketUpgradeId)
		{
			tokenUpgradeOffer ??= GameStorefront.GetExistingOffer(LlamaSelector.TokenUpgradeId);
			//set token offer entry
			useTokenOffer = tokenUpgradeOffer.GetPriceInInventory() > 0;
			if (ct.IsCancellationRequested)
				return;
		}

		var purchaseLimit = await account.GetStockLimit(offer);
		bool inStock = purchaseLimit > 0;
		if (ct.IsCancellationRequested)
			return;

		if (offer.Price?.quantity > 0)
		{
			var inventoryCount = offer.GetPriceInInventory();
			if (ct.IsCancellationRequested)
				return;
			purchaseLimit = Mathf.Min(purchaseLimit, inventoryCount / offer.Price.quantity);
		}

		var prerollData = await offer.GetXRayLlamaData(account);
		if (offer.IsXRayLlama && prerollData is null)
		{
			await account.GenerateXRayLlamaResults();
			prerollData = await offer.GetXRayLlamaData(account);

			if (ct.IsCancellationRequested)
				return;
		}
		if (ct.IsCancellationRequested)
			return;

		await currentOfferEntry.SetOffer(useTokenOffer ? tokenUpgradeOffer : offer);
		if (ct.IsCancellationRequested)
			return;

		currentOffer = offer;

		if (!inStock && !useTokenOffer)
		{
			soldOutResultPanel.Visible = true;
			return;
		}

		purchaseButton?.Visible = true;

		var items = prerollData?.GetPrerollItems();
		if (items is null)
		{
			//it's a surprise
			quantitySpinner?.MaxValue = Mathf.Max(purchaseLimit, 1);
			quantitySpinner?.Visible = purchaseLimit > 1;
			surpriseResultPanel?.Visible = true;
			return;
		}

		//fill out item list
		SetLlamaContents(items);
	}

	private void OnQuantityChanged(double value)
	{
		if (currentOffer is not null)
			currentOfferEntry.SetTargetPurchaseQuantity((int)value);
	}

	public void InspectChoiceLlama(int choiceIndex)
	{
		if (topCardpack is null)
			return;
		GameItemViewer.Instance.ShowItem(topCardpack, choiceIndex);
	}

	public void SetLlamaItems(GameItem[] items)
	{
		items = [.. items.Where(i => i.profile is not null)];
		ClearPreview();

		if ((items?.Length ?? 0) == 0)
		{
			GD.PushWarning("Null or empty Item Array");
			return;
		}

		currentCardpacks = items;
		GameItem.MarkItemsSeen(items);

		if (items.Length > 1 && items.Any(i => i.CardPackChoices is not null))
		{
			//stack of choices
			SetLlamaContents(items);
		}
		else
		{
			SetTopLlamaItem(items[^1]);
			var maxAmount = items.Length;
			quantitySpinner?.MaxValue = Mathf.Max(maxAmount, 1);
			quantitySpinner?.Visible = maxAmount > 1;
			openButton?.Visible = true;
		}
	}

	void SetTopLlamaItem(GameItem topItem)
	{
		surpriseResultPanel.Visible = false;
		choicePanel.Visible = false;

		topCardpack = topItem;
		currentCardpackEntry.SetItem(topItem);

		if (topItem.CardPackChoices is not GameItem[] choices)
		{
			surpriseResultPanel.Visible = true;
			return;
		}

		choicePanel.Visible = true;
		firstChoice.SetItem(choices[0]);
		secondChoice.SetItem(choices[1]);
	}

	void SetLlamaContents(GameItem[] items)
	{
		resultEntriesParent.Visible = (items?.Length ?? 0) > 0;
		if (!resultEntriesParent.Visible)
			return;
		while (llamaResultEntries.Count <= items.Length)
		{
			var newEntry = itemEntryScene.Instantiate<GameItemEntry>();
			newEntry.preventInteractability |= !interactive;
			resultEntriesParent.AddChild(newEntry);
			llamaResultEntries.Add(newEntry);
		}
		for (int i = 0; i < items.Length; i++)
		{
			llamaResultEntries[i].Visible = true;
			llamaResultEntries[i].SetItem(items[i]);
			items[i].SetRewardNotification();
		}
		for (int i = items.Length; i < llamaResultEntries.Count; i++)
		{
			llamaResultEntries[i].Visible = false;
		}
	}

	public async void PurchaseLlamaOffer()
	{
		if (currentOffer is null)
			return;
		var targetOffer = useTokenOffer ? tokenUpgradeOffer : currentOffer;

		GD.Print("attempting to purchase offer: " + targetOffer.OfferId);
		var itemsKnown = await targetOffer.GetXRayLlamaData() is not null;
		await CardPackOpener.Instance.StartOpening(null, this, targetOffer, currentOfferEntry.currentPurchaseQuantity, itemsKnown);
		//SetLlamaOffer(currentOffer);
	}

	public async void OpenSelectedCardpack()
	{
		if (currentCardpacks is null)
			return;
		int amount = (int)quantitySpinner.Value;
		currentCardpacks = [.. currentCardpacks.Where(i => i.profile is not null)];
		amount = Mathf.Min(amount, currentCardpacks.Length);
		if (amount == 0)
		{
			ClearPreview();
			return;
		}
		var targetItems = currentCardpacks[^amount..];
		currentCardpacks = amount < currentCardpacks.Length ? currentCardpacks[..^amount] : [];

		await CardPackOpener.Instance.StartOpening(targetItems, llamaPanel, targetItems[^1]);
		//if (currentCardpacks.Length == 0)
		//	ClearPreview();
		//else
		//	SetLlamaItems(currentCardpacks);
	}
}
