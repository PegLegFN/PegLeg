using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

public partial class LlamaInterface : Control
{
    const string TicketUpgradeId = "4D64CBE3618D41FBB5CAD0E472F4610A";
    const string TokenUpgradeId = "D2E08EFA731D437B85B7340EB51A5E1D";

    static LlamaInterface instance;
    public static void SelectLlamaTab() => instance.Visible = true;

    [ExportGroup("Scenes")]
    [Export]
    PackedScene catalogLlamaEntryScene;

    [Export]
    PackedScene cardpackLlamaEntryScene;

    [Export]
    PackedScene itemEntryScene;

    [ExportGroup("List")]
    [Export]
    Control offerListLoadingIcon;

    [Export]
    Control offerListErrorIcon;

    [Export]
    Control llamaScrollArea;

    [Export]
    Control llamaOfferParent;

    [Export]
    Control llamaItemEntryPanel;

    [Export]
    Control llamaItemEntryParent;

    [ExportGroup("Selected")]
    [Export]
    Control selectedLlamaPanel;

    [Export]
    GameOfferEntry currentOfferEntry;

    [Export]
    CardPackEntry currentCardpackEntry;

    [Export]
    Control purchaseButton;

    [Export]
    GameOfferEntry altOfferEntry;

    [Export]
    Control altPurchaseButton;

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


    List<GameOfferEntry> llamaOfferEntries = [];
    Queue<CardPackEntry> llamaItemEntries = new();
    List<GameItemEntry> llamaResultEntries = [];

    GameProfile llamaItemProfile;
    List<LlamaItemStack> llamaItemStacks = [];
    LlamaItemStack currentCardpackSelection = null;

    Dictionary<string, GameOffer> activeOffers = [];
    GameOffer currentOfferSelection;
    GameOffer tokenUpgradeOffer;

    // Called when the node enters the scene tree for the first time.
    public override void _Ready()
	{
        instance = this;
        llamaItemEntryPanel.Visible = false;
        VisibilityChanged += LoadShopLlamas;
        quantitySpinner.ValueChanged += OnQuantityChanged;
        RefreshTimerController.OnHourChanged += ForceLoadShopLlamas;
        GameAccount.ActiveAccountChanged += OnAccountChanged;
        OnAccountChanged();
    }

    public override void _ExitTree()
    {
        RefreshTimerController.OnHourChanged -= ForceLoadShopLlamas;
        GameAccount.ActiveAccountChanged -= OnAccountChanged;
        if(llamaItemProfile is not null)
        {
            llamaItemProfile.OnItemAdded -= AddItem;
            llamaItemProfile.OnItemRemoved -= RemoveLlamaItem;
        }
    }

    class LlamaItemStack
    {
        public readonly string templateId;
        public readonly string customType;
        public readonly bool isKnown;
        public readonly List<GameItem> items;
        public CardPackEntry llamaEntry { get; private set; }

        public LlamaItemStack(GameItem firstItem, CardPackEntry linkedEntry)
        {
            llamaEntry = linkedEntry;

            templateId = firstItem.templateId;

            if (firstItem.template.DisplayName.Contains("Accolade"))
                customType = "Accolade";

            isKnown = firstItem.attributes.ContainsKey("options");

            items = [firstItem];
            UpdateEntry();
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
            UpdateEntry();
        }

        public void RemoveItem(GameItem item)
        {
            items.Remove(item);
            if (items.Count > 0)
                UpdateEntry();
        }

        public GameItem GetDisplayItem()
        {
            var nextItem = items.Last();
            nextItem.customData["stackQuantity"] = isKnown ? -1 : items.Count;
            //OverridePackDisplay(ref nextPack);
            return nextItem;
        }

        void UpdateEntry() => llamaEntry?.SetItem(GetDisplayItem());

        public CardPackEntry DetachLlamaEntry()
        {
            items.Clear();
            llamaEntry.Visible = false;
            var oldEntry = llamaEntry;
            llamaEntry = null;
            return oldEntry;
        }
    }

    void AddItem(GameItem item)
    {
        if (item?.template?.Type != "CardPack")
            return;
        llamaItemEntryPanel.Visible = true;
        var stackableGroup = llamaItemStacks.FirstOrDefault(val=>val.IsStackable(item));

        if (stackableGroup is not null)
        {
            stackableGroup.AddItem(item);
            return;
        }

        CardPackEntry newEntry;
        if (llamaItemEntries.Count > 0)
        {
            //pull from queue
            newEntry = llamaItemEntries.Dequeue();
            newEntry.Visible = true;
        }
        else
        {
            //spawn new
            newEntry = cardpackLlamaEntryScene.Instantiate<CardPackEntry>();
            llamaItemEntryParent.AddChild(newEntry);
            newEntry.LlamaPressed += SetCardPackLlama;
        }

        newEntry.MoveToFront();
        LlamaItemStack llamaStack = new(item, newEntry);
        llamaItemStacks.Add(llamaStack);
    }

    void RemoveLlamaItem(GameItem item)
    {
        if (item?.template?.Type != "CardPack")
            return;
        var llamaStack = llamaItemStacks.FirstOrDefault(val => val.Has(item));
        if (llamaStack is not null)
        {
            llamaStack.RemoveItem(item);
            if (llamaStack.items.Count == 0)
            {
                llamaItemEntries.Enqueue(llamaStack.DetachLlamaEntry());
                llamaItemStacks.Remove(llamaStack);
                if (llamaItemStacks.Count == 0)
                {
                    llamaItemEntryPanel.Visible = false;
                }
            }
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

        //refresh cardpacks
        var account = GameAccount.activeAccount;
        if (!await account.Authenticate() || ct.IsCancellationRequested)
            return;

        var newLlamaItemProfile = await account.GetProfile(FnProfileTypes.AccountItems).Query();
        if (ct.IsCancellationRequested)
            return;

        //apply new data synchronously
        foreach (var offerEntry in llamaOfferEntries)
        {
            offerEntry.Visible = offerEntry.GetMeta("llamaFilter", false).AsBool();
        }

        //load new items
        var newLlamaItems = newLlamaItemProfile.GetItems("CardPack");
        foreach (var llamaStack in llamaItemStacks)
        {
            llamaItemEntries.Enqueue(llamaStack.DetachLlamaEntry());
        }
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
        llamaScrollArea.Visible = false;
        offerListErrorIcon.Visible = false;
        llamaOfferParent.Visible = true;

        activeOffers.Clear();
        ClearSelection();

        bool success = false;
        try
        {
            await llamaShopSemaphore.WaitAsync(ct);
            if (ct.IsCancellationRequested)
                return;

            var prevSelectedOffer = currentOfferSelection?.OfferId;

            var account = GameAccount.activeAccount;
            if (!await account.Authenticate() || ct.IsCancellationRequested)
                return;
            await account.GenerateXRayLlamaResults();

            var xrayStorefront = await GameStorefront.GetStorefront(FnStorefrontTypes.XRayLlamaCatalog, force ? null : RefreshTimeType.Hourly);
            var randomStorefront = await GameStorefront.GetStorefront(FnStorefrontTypes.RandomLlamaCatalog, force ? null : RefreshTimeType.Hourly);
            if (ct.IsCancellationRequested)
                return;

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
            tokenUpgradeOffer = filteredOffers.FirstOrDefault(o => o.OfferId == TokenUpgradeId);
            filteredOffers.Remove(tokenUpgradeOffer);
            await altOfferEntry.SetOffer(tokenUpgradeOffer);

            foreach (var offer in filteredOffers)
            {
                if (llamaOfferEntries.Count <= catalogEntryIndex)
                {
                    var newEntry = catalogLlamaEntryScene.Instantiate<GameOfferEntry>();
                    llamaOfferParent.AddChild(newEntry);
                    newEntry.Pressed += SetLlamaOffer;
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

            if (prevSelectedOffer is not null)
                SetLlamaOffer(prevSelectedOffer);
            success = true;
        }
        finally
        {
            llamaShopSemaphore.Release();
            if (!ct.IsCancellationRequested)
            {
                offerListLoadingIcon.Visible = false;
                llamaScrollArea.Visible = true;
                llamaOfferParent.Visible = success;
                offerListErrorIcon.Visible = !success;
            }
        }
    }

    static async Task<bool> LlamaOfferFilter(GameOffer offer)
    {
        if(offer.OfferId==TokenUpgradeId)
            return true;

        var account = GameAccount.activeAccount;

        if (!await account.MatchesFulfillmentRequirements(offer))
            return false;

        string priceTemplateId = offer.Price?.templateId;
        int price = offer.Price?.quantity ?? 0;
        if (price == 1)
            return (await account.GetProfile(FnProfileTypes.AccountItems).Query())
                .GetTemplateItems(priceTemplateId)
                .Select(item => item.quantity)
                .Sum() >= 1;

        return true;
    }

    public void ClearSelection()
    {
        offerCts?.Cancel();

        currentOfferEntry.ClearOffer();
        currentCardpackEntry.ClearItem();

        openButton.Visible = false;
        purchaseButton.Visible = false;
        altPurchaseButton.Visible = false;
        quantitySpinner.Visible = false;

        resultEntriesParent.Visible = false;
        surpriseResultPanel.Visible = false;
        soldOutResultPanel.Visible = false;
        choicePanel.Visible = false;

        currentOfferSelection = null;
        currentCardpackSelection = null;
    }

    CancellationTokenSource offerCts;

    public void SetLlamaOffer(string offerId)
    {
        if (!activeOffers.ContainsKey(offerId))
        {
            ClearSelection();
            return;
        }
        SetLlamaOffer(activeOffers[offerId]);
    }

    public async void SetLlamaOffer(GameOffer offer)
    {
        ClearSelection();

        offerCts = offerCts.CancelAndRegenerate(out var ct);

        //show loading icon
        var account = GameAccount.activeAccount;
        if (!await account.Authenticate() || ct.IsCancellationRequested)
            return;

        //if this is ticket-based Upgrade Llama, also show token-based Upgrade Llama

        if (offer.OfferId == TicketUpgradeId)
        {
            bool hasToken = (await tokenUpgradeOffer.GetPriceInInventory()) > 0;
            if (ct.IsCancellationRequested)
                return;
            altPurchaseButton.Visible = hasToken;
        }
        else
            altPurchaseButton.Visible = false;

        var purchaseLimit = await account.GetPurchaseLimit(offer);
        bool inStock = purchaseLimit > 0;
        if (ct.IsCancellationRequested)
            return;

        if (offer.Price?.quantity > 0)
        {
            var inventoryCount = await offer.GetPriceInInventory();
            if (ct.IsCancellationRequested)
                return;
            purchaseLimit = Mathf.Min(purchaseLimit, inventoryCount / offer.Price.quantity);
        }

        var prerollData = await offer.GetXRayLlamaData(account);
        if (ct.IsCancellationRequested)
            return;

        await currentOfferEntry.SetOffer(offer);
        if (ct.IsCancellationRequested)
            return;

        currentOfferSelection = offer;
        //CurrencyHighlight.Instance.SetCurrencyTemplate(offer.Price.template);

        if (!inStock)
        {
            soldOutResultPanel.Visible = true;
            return;
        }

        purchaseButton.Visible = true;

        var items = prerollData?.attributes?["items"].AsArray().Select(node => new GameItem(null, null, node.AsObject())).ToArray() ?? null;
        if (items is null)
        {
            //it's a surprise
            quantitySpinner.MaxValue = Mathf.Max(purchaseLimit, 1);
            quantitySpinner.Visible = purchaseButton.Visible && purchaseLimit > 1;
            surpriseResultPanel.Visible = true;
            return;
        }

        var sortedItems = items
            .OrderBy(item => -item.template?.RarityLevel)
            .ThenBy(item => item.template?.Type)
            .ThenBy(item => item.template?.DisplayName)
            .ToArray();
        //fill out item list
        resultEntriesParent.Visible = true;

        while (llamaResultEntries.Count <= items.Length)
        {
            var newEntry = itemEntryScene.Instantiate<GameItemEntry>();
            resultEntriesParent.AddChild(newEntry);
            llamaResultEntries.Add(newEntry);
        }
        for (int i = 0; i < items.Length; i++)
        {
            string templateId = sortedItems[i].templateId;
            llamaResultEntries[i].Visible = true;
            llamaResultEntries[i].SetItem(sortedItems[i]);
            sortedItems[i].SetRewardNotification();
        }
        for (int i = items.Length; i < llamaResultEntries.Count; i++)
        {
            llamaResultEntries[i].Visible = false;
        }
    }

    private void OnQuantityChanged(double value)
    {
        if (currentOfferSelection is not null)
            currentOfferEntry.SetTargetPurchaseQuantity((int)value);
    }

    public void SetCardPackLlama(string uuid)
    {
        ClearSelection();

        var cardPackStack = llamaItemStacks.FirstOrDefault(val => val.Has(uuid));
        var cardPackItem = cardPackStack?.GetDisplayItem();

        if (cardPackItem is null)
            return;

        currentCardpackSelection = cardPackStack;
        openButton.Visible = true;

        var maxAmount = currentCardpackSelection.DisplayAmount;
        quantitySpinner.MaxValue = Mathf.Max(maxAmount, 1);
        quantitySpinner.Visible = maxAmount > 1;
        cardPackItem.MarkItemSeen();
        currentCardpackEntry.SetItem(cardPackItem);

        if (cardPackItem.CardPackChoices is not GameItem[] choices)
        {
            surpriseResultPanel.Visible = true;
            return;
        }

        choicePanel.Visible = true;
        firstChoice.SetItem(choices[0]);
        secondChoice.SetItem(choices[1]);
    }

    public void InspectChoiceLlama(int choiceIndex)
    {
        if (currentCardpackSelection is null)
            return;
        var cardPackItem = currentCardpackSelection.GetDisplayItem();
        GameItemViewer.Instance.ShowItem(cardPackItem, choiceIndex);
    }

    public async void PurchaseLlama()
    {
        if (currentOfferSelection is null)
            return;

        GD.Print("attempting to purchase offer: "+ currentOfferSelection.OfferId);
        var itemsKnown = await currentOfferSelection.GetXRayLlamaData() is not null;
        await CardPackOpener.Instance.StartOpening(null, selectedLlamaPanel, currentOfferSelection, currentOfferEntry.currentPurchaseQuantity, itemsKnown);
        SetLlamaOffer(currentOfferSelection);
    }

    public async void PurchaseTokenUpgradeLlama()
    {
        if (tokenUpgradeOffer is null)
            return;

        GD.Print("attempting to purchase offer: " + tokenUpgradeOffer.OfferId);
        await CardPackOpener.Instance.StartOpening(null, selectedLlamaPanel, tokenUpgradeOffer, 1, true);
        currentOfferSelection.NotifyChanged();
        SetLlamaOffer(currentOfferSelection);
    }

    public async void OpenSelectedCardpack()
    {
        if (currentCardpackSelection is null)
            return;
        int amount = (int)quantitySpinner.Value;
        var items = currentCardpackSelection.items.ToArray()[^amount..];
        bool depletesSelected = currentCardpackSelection.items.Count <= amount;

        //await BulkOpenCardpacks(handles.Select(val=>val.uuid).ToArray());
        await CardPackOpener.Instance.StartOpening(items, selectedLlamaPanel, currentCardpackSelection.GetDisplayItem());

        if (depletesSelected)
            ClearSelection();
        else
            SetCardPackLlama(currentCardpackSelection.items.Last().uuid);
    }

    static GameItem allTheLlamas;
    public async void BulkOpenAllCardpacks()
    {
        List<GameItem> allCardpacks = [];
        bool includesSelected = false;
        foreach (var group in llamaItemStacks)
        {
            //if (group.isKnown)
            //    continue;
            if (currentCardpackSelection == group)
                includesSelected = true;
            foreach (var item in group.items)
            {
                allCardpacks.Add(item);
            }
        }
        allTheLlamas ??= GameItemTemplate.Get("CardPack:cardpack_bronze_10x").CreateInstance();
        await CardPackOpener.Instance.StartOpening(allCardpacks.ToArray(), selectedLlamaPanel, allTheLlamas);
        //await BulkOpenCardpacks(itemIds.ToArray());
        if (includesSelected)
            ClearSelection();
    }
}
