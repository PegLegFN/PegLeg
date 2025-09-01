using Godot;
using System;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

public partial class GameOfferEntry : Control
{
    [Signal]
    public delegate void NameChangedEventHandler(string name);
    [Signal]
    public delegate void StockChangedEventHandler(string amount);
    [Signal]
    public delegate void IsInStockChangedEventHandler(bool isInStock);
    [Signal]
    public delegate void IsFreeChangedEventHandler(bool isFree);
    [Signal]
    public delegate void IsAffordableChangedEventHandler(bool isAffordable);
    [Signal]
    public delegate void TotalGrantAmountChangedEventHandler(string amount);
    [Signal]
    public delegate void IsLimitedTimeChangedEventHandler(bool isLimitedTime);
    [Signal]
    public delegate void IsLimitedStockChangedEventHandler(bool isLimitedStock);
    [Signal]
    public delegate void IsErroredEventHandler(bool isErrored);
    [Signal]
    public delegate void PressedEventHandler(string currentOfferId);

    [Export]
    GameItemEntry grantedItemEntry;
    [Export]
    GameItemEntry priceEntry;
    [Export]
    GameItemEntry priceInInventoryEntry;
    [Export]
    CheckButton selectionGraphics;
    [Export]
    bool includeAmountInName = false;
    [Export]
    bool showSingleStockAmount = false;
    [Export]
    bool showSoldOutAmount = true;
    [Export]
    bool cosmeticMode = false;

    public GameOffer currentOffer { get; private set; }
    GameItem pricePerPurchase;
    GameItem grantedItem;
    public int targetPurchaseQuantity { get; private set; }
    public int currentPurchaseQuantity { get; private set; }
    int currentPriceInInventory;
    int currentStockLimit;
    bool accountDirty = false;
    bool offerDirty = false;


    public override void _Ready()
    {
        VisibilityChanged += CheckForRefresh;
        GameAccount.ActiveAccountChanged += MarkOfferDirty;
    }

    public override void _ExitTree()
    {
        VisibilityChanged -= CheckForRefresh;
        GameAccount.ActiveAccountChanged -= MarkOfferDirty;
    }

    private void MarkOfferDirty()
    {
        offerDirty = true;
        CheckForRefresh();
    }

    private void MarkAccountDirty()
    {
        accountDirty = true;
        CheckForRefresh();
    }

    private void CheckForRefresh()
    {
        if (!IsVisibleInTree())
            return;
        if (offerDirty)
            SetOffer(currentOffer).StartTask();
        else if (accountDirty)
            RefreshAccount().StartTask();
    }

    CancellationTokenSource cts;
    public async Task SetOffer(GameOffer shopOffer)
    {
        cts?.Cancel();
        if (shopOffer is null)
        {
            ClearOffer(); 
            return;
        }

        if (currentOffer != shopOffer)
        {
            if(currentOffer is not null)
            {
                currentOffer.OnChanged -= MarkOfferDirty;
                currentOffer.OnRemoved -= ClearOffer;
            }
            shopOffer.OnChanged += MarkOfferDirty;
            shopOffer.OnRemoved += ClearOffer;
        }

        EmitSignal(SignalName.IsErrored, false);
        currentOffer = shopOffer;
        pricePerPurchase = shopOffer.Price;
        //in future, add support for an item list
        grantedItem = shopOffer.itemGrants.FirstOrDefault().Clone();
        grantedItem.customData["shopQuantity"] = -1;

        targetPurchaseQuantity = 1;
        currentPurchaseQuantity = 1;
        currentPriceInInventory = -1;
        currentStockLimit = -1;

        UpdateVisuals();
        cts = new();
        await UpdateAccountProperties(cts.Token);
    }

    public async Task RefreshAccount()
    {
        if (currentOffer is null)
            return;
        cts = cts.CancelAndRegenerate(out var ct);
        await UpdateAccountProperties(ct);
    }

    async Task UpdateAccountProperties(CancellationToken ct = default)
    {
        if (currentOffer is null)
            return;
        accountDirty = false;
        var account = GameAccount.activeAccount;
        var isAuthenticated = await account.Authenticate();
        if (ct.IsCancellationRequested)
            return;
        if (!isAuthenticated)
        {
            //show warning symbol or something
            EmitSignal(SignalName.IsErrored, true);
            return;
        }
        EmitSignal(SignalName.IsErrored, false);

        int stockLimit = await account.GetPurchaseLimit(currentOffer);
        if (!account.MatchesItemRequirements(currentOffer)) //todo: proper item requirement check
            stockLimit = 0;
        if (ct.IsCancellationRequested)
            return;
        currentStockLimit = stockLimit;
        if (currentStockLimit == 999)
            currentStockLimit = -1;
        grantedItem.customData["shopQuantity"] = currentStockLimit;

        if (grantedItem?.template?.Type == "CardPack")
        {
            int tier = (await currentOffer.GetXRayLlamaData(account))?.attributes?["highest_rarity"]?.GetValue<int>() ?? 0;
            if (grantedItem.template.DisplayName.Contains("Legendary"))
                tier = 2;
            if (ct.IsCancellationRequested)
                return;
            grantedItem.customData["llamaTier"] = tier;
        }

        if (cosmeticMode)
        {
            var finalPrice = await currentOffer.GetPersonalPrice();
            if (ct.IsCancellationRequested)
                return;
            pricePerPurchase = finalPrice;
        }

        if ((pricePerPurchase?.quantity ?? 0) > 0)
        {
            var inventoryItem = await currentOffer.GetPriceInventoryItem();
            if (ct.IsCancellationRequested)
                return;
            currentPriceInInventory = inventoryItem?.quantity ?? 0;
        }

        //async stuff complete, now update visuals
        UpdateVisuals();
    }

    public void ClearOffer()
    {
        cts?.Cancel();

        currentOffer = null;
        grantedItem = null;
        pricePerPurchase = null;

        targetPurchaseQuantity = 1;
        currentPurchaseQuantity = 1;
        currentPriceInInventory = -1;
        currentStockLimit = -1;

        EmitSignal(SignalName.NameChanged, "");
        EmitSignal(SignalName.StockChanged, "");
        EmitSignal(SignalName.IsLimitedStockChanged, false);
        EmitSignal(SignalName.IsFreeChanged, false);
        EmitSignal(SignalName.IsInStockChanged, false);
        EmitSignal(SignalName.IsAffordableChanged, false);
        EmitSignal(SignalName.IsLimitedTimeChanged, false);
        EmitSignal(SignalName.TotalGrantAmountChanged, "");
        EmitSignal(SignalName.IsErrored, false);
        grantedItemEntry?.ClearItem(null);
        priceInInventoryEntry?.ClearItem(null);
        priceEntry?.ClearItem(null);
    }

    public void SetTargetPurchaseQuantity(int targetPurchaseQuantity)
    {
        this.targetPurchaseQuantity = targetPurchaseQuantity;
        UpdateQuantityAndItems();
    }

    void UpdateVisuals()
    {
        UpdateQuantityAndItems();

        var price = (pricePerPurchase?.quantity ?? 0) * currentPurchaseQuantity;
        var grantedQuantity = grantedItem.quantity * currentPurchaseQuantity;

        EmitSignal(SignalName.IsInStockChanged, currentStockLimit != 0);
        EmitSignal(SignalName.IsLimitedStockChanged, currentStockLimit > -1);
        EmitSignal(SignalName.IsFreeChanged, price == 0);
        EmitSignal(SignalName.IsAffordableChanged, price == 0 || (pricePerPurchase?.quantity ?? 0) <= currentPriceInInventory);
        EmitSignal(SignalName.IsLimitedTimeChanged, currentOffer.OfferId == "D46EC225FA1149ADB00B4B17B2ABAB70"); //offerId of random free llamas which only last 1 hour
        //8339003D26B24F70878EE280B70C340D: offerId of Winter free llamas that restock daily for (14?) days
        //8339003D26B24F70878EE280B70C340D: offerId of Winter free llamas that restock daily for (14?) days

        if (currentStockLimit >= (showSoldOutAmount ? 0 : 1))
        {
            string stockText = "x" + currentStockLimit;
            if (currentStockLimit == 0)
                stockText = "Sold Out";
            if (grantedQuantity > 1 && currentStockLimit != 0)
                stockText = grantedQuantity + stockText;
            EmitSignal(SignalName.StockChanged, stockText);
        }
        else
        {
            EmitSignal(SignalName.StockChanged, "");
        }

        var name = currentOffer.Title ?? 
            grantedItem?.template?.DisplayName ?? 
            currentOffer.itemGrants.FirstOrDefault().templateId.Split(":")[1] ?? 
            "Offer Name";
        if (includeAmountInName)
        {
            if (currentStockLimit > (showSingleStockAmount ? 1 : 0))
                name += " (" + currentStockLimit + " left)";
            else if (currentStockLimit == 0)
                name += " (Sold Out)";
        }
        EmitSignal(SignalName.NameChanged, name);
    }

    void UpdateQuantityAndItems()
    {
        int maxQuantity = 1;
        if ((pricePerPurchase?.quantity ?? 0) > 0 && currentPriceInInventory >= 0)
            maxQuantity = Mathf.FloorToInt(currentPriceInInventory / pricePerPurchase.quantity);
        if (currentStockLimit > -1)
            maxQuantity = Mathf.Min(maxQuantity, currentStockLimit);
        if (currentOffer.SimultaniousLimit > 0)
            maxQuantity = Mathf.Min(maxQuantity, currentOffer.SimultaniousLimit);

        currentPurchaseQuantity = Mathf.Clamp(targetPurchaseQuantity, 1, Mathf.Max(maxQuantity, 1));
        EmitSignal(SignalName.TotalGrantAmountChanged, ((grantedItem?.quantity ?? 1) * currentPurchaseQuantity).ToString());

        var price = (pricePerPurchase?.quantity ?? 0) * currentPurchaseQuantity;
        if (price > 0)
        {
            priceEntry?.SetItem(pricePerPurchase.Clone(price));
            priceInInventoryEntry?.SetItem(pricePerPurchase.Clone(currentPriceInInventory));
        }
        else
        {
            priceEntry?.ClearItem(null);
            priceInInventoryEntry?.ClearItem(null);
        }
        grantedItemEntry?.SetItem(grantedItem, true);
    }

    public void EmitPressedSignal()
    {
        if (currentOffer is null)
            return;
        if (selectionGraphics is not null)
            selectionGraphics.ButtonPressed = true;
        EmitSignal(SignalName.Pressed, currentOffer.OfferId);
    }

    public void Inspect()
    {
        GameItemViewer.Instance.ShowOffer(currentOffer);
    }
}
