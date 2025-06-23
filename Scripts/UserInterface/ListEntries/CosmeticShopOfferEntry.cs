using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Resources;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

public partial class CosmeticShopOfferEntry : Control, IRecyclableEntry
{
    [Signal]
    public delegate void NameChangedEventHandler(string name);
    [Signal]
    public delegate void TypeChangedEventHandler(string type);
    [Signal]
    public delegate void TooltipChangedEventHandler(string tooltip);
    [Signal]
    public delegate void OutlineChangedEventHandler(Color outline);

    [Signal]
    public delegate void OwnedVisibilityEventHandler(bool visible);

    [Signal]
    public delegate void BonusTextVisibilityEventHandler(bool visible);
    [Signal]
    public delegate void BonusTextChangedEventHandler(string name);

    [Signal]
    public delegate void PriceAmountEventHandler(string amount);
    [Signal]
    public delegate void PriceVisibilityEventHandler(bool visible);

    [Signal]
    public delegate void OldPriceAmountEventHandler(string amount);
    [Signal]
    public delegate void OldPriceVisibilityEventHandler(bool visible);

    [Signal]
    public delegate void ReminingTimeVisibilityEventHandler(bool visible);
    [Signal]
    public delegate void BestsellerVisibilityEventHandler(bool visible);

    [Signal]
    public delegate void LastSeenTextEventHandler(string amount);
    [Signal]
    public delegate void LastSeenVisibilityEventHandler(bool visible);
    [Signal]
    public delegate void LastSeenAlertVisibilityEventHandler(bool visible);
    [Signal]
    public delegate void AlmostAYearVisibilityEventHandler(bool visible);


    [Export]
    Label expiryTimerText;
    [Export]
    Control loadingCubes;
    [Export]
    ShaderHook backgroundGradientTexture;
    [Export]
    float waitTimeToLoadResources = 0.5f;
    [Export]
    ShaderHook resourceTarget;
    [Export]
    Texture2D fallbackTexture;
    [Export]
    bool useOutlineColor;
    [Export]
    Color fallbackOutlineColor;
    Timer resourceLoadTimer;

    CosmeticShopOfferData currentOfferData;
    Gradient bgGradient;
    Color[] bgGradientDefaultColors;
    static readonly float[] bgGradientDefaultOffsets = new float[] { 0, 1 };

    public override void _Ready()
    {
        base._Ready();

        resourceLoadTimer = new() {WaitTime = waitTimeToLoadResources};
        AddChild(resourceLoadTimer);
        resourceLoadTimer.Timeout += StartResourceLoadSequence;
        bgGradient = (backgroundGradientTexture.Texture as GradientTexture2D).Gradient;
        bgGradientDefaultColors = bgGradient.Colors;
        //resourceRequest = new();
        //AddChild(resourceRequest);
        //resourceRequest.RequestCompleted += CompleteResourceLoadSequence;
        RefreshTimerController.OnSecondChanged += UpdateOutTimer;
    }

    public override void _ExitTree()
    {
        ClearCosmeticOfferData();
        RefreshTimerController.OnSecondChanged -= UpdateOutTimer;
    }

    public Control node => this;
    IRecyclableElementProvider<GameOffer> offerProvider;

    public void SetRecyclableElementProvider(IRecyclableElementProvider provider)
    {
        offerProvider = provider is IRecyclableElementProvider<GameOffer> newOfferProvider ? newOfferProvider : null;
    }

    public void SetRecycleIndex(int index)
    {
        
    }

    DateTime? outDate;
    private void UpdateOutTimer()
    {
        //TODO: move to TimerLabel
        if (!outDate.HasValue)
            return;
            var remainingTime = (outDate.Value - DateTime.UtcNow);
        EmitSignal(SignalName.ReminingTimeVisibility, remainingTime.TotalHours < 24);
        expiryTimerText.Text = remainingTime.FormatTime(Helpers.TimeFormat.SigShort);
    }

    public void StartResourceLoadTimer() =>
        resourceLoadTimer.Start();

    public void CancelResourceLoadTimer() =>
        resourceLoadTimer.Stop();

    bool tryLoadImageDisplayAsset;
    bool resourceLoadStarted;
    private async void StartResourceLoadSequence()
    {
        resourceLoadTimer.Stop();
        if (resourceLoadStarted)
            return;
        resourceLoadStarted = true;
        if(currentOfferData is not null)
        {
            if (!currentOfferData.resourceLoadComplete)
                ApplyOfferResource();
            return;
        }

        if (displayAssetLoadStarted)
            await LoadImageDisplayAsset();

        if (imageUrl is null)
        {
            resourceTarget.Texture = fallbackTexture;
            GD.PushWarning("No resource URL");
            loadingCubes.Visible = false;
            resourceTarget.Visible = true;
            return;
        }
        var tex = await CatalogRequests.GetCosmeticResource(imageUrl);
        if (tex is not null)
            ApplyResource(tex);
        else
        {
            resourceTarget.Texture = fallbackTexture;
            GD.PushWarning("Image request failed");
            loadingCubes.Visible = false;
            resourceTarget.Visible = true;
        }
    }

    private async Task LoadImageDisplayAsset()
    {
        var imageDA = await CatalogRequests.GetCosmeticMeta(imageDisplayAssetPath);
        var possibleRenderImage = imageDA?["ContextualPresentations"]?[0]?["RenderImage"]?["AssetPathName"]?.ToString();
        if (possibleRenderImage is null)
            return;
        imageUrl = "https://fortnitecentral.genxgames.gg/api/v1/export?path=" + possibleRenderImage.Split('.')[0];
    }

    bool displayAssetLoadStarted;
    public void Interact()
    {
        if (shopUrl is not null)
            OS.ShellOpen(shopUrl);
    }

    public void LoadOrOpenImage()
    {
        if (imageUrl is not null)
        {
            OS.ShellOpen(imageUrl);
        }
        //else if (!displayAssetLoadStarted)
        //{
        //    displayAssetLoadStarted = true;
        //    resourceLoadStarted = false;
        //    loadingCubes.Visible = true;
        //    resourceTarget.Visible = false;
        //    StartResourceLoadSequence();
        //}
    }

    void ApplyResource() => 
        ApplyResource(null);
    void ApplyResource(Texture2D tex)
    {
        Vector2 shift = tex is null ? new(0.5f, 0.5f) : resourceShift;
        bool fit = tex is not null && resourceFit;
        tex ??= fallbackTexture;
        //resourceTarget.SetShaderTexture(tex, "Cosmetic");
        resourceTarget.Texture = tex;
        resourceTarget.SetShaderVector(shift, "ShiftDirection");
        resourceTarget.SetShaderBool(fit, "Fit");
        resourceLoadStarted = true;
        loadingCubes.Visible = false;
        resourceTarget.Visible = true;
    }

    void ApplyOfferResource() => 
        ApplyResource(currentOfferData.resourceTex);

    void ApplyOfferOwnership()
    {
        EmitSignal(SignalName.PriceVisibility, !currentOfferData.isOwned);
        EmitSignal(SignalName.OldPriceVisibility, !currentOfferData.isOwned && currentOfferData.isDiscountBundle);
        EmitSignal(SignalName.OldPriceAmount, (currentOfferData.price - currentOfferData.discountAmount).ToString());
        EmitSignal(SignalName.OwnedVisibility, currentOfferData.isOwned);
    }

    public void SetCosmeticOfferData(CosmeticShopOfferData offerData)
    {
        ClearCosmeticOfferData();
        currentOfferData = offerData;
        currentOfferData.OnResourceLoaded += ApplyOfferResource;
        //currentOfferData.OnOwnershipLoaded += ApplyOfferOwnership;

        outDate = offerData.outDate;
        UpdateOutTimer();

        EmitSignal(SignalName.NameChanged, offerData.displayName);
        EmitSignal(SignalName.TypeChanged, offerData.displayType);
        EmitSignal(SignalName.TooltipChanged, offerData.displayType);

        EmitSignal(SignalName.PriceAmount, offerData.price.ToString());
        EmitSignal(SignalName.PriceVisibility, true);
        EmitSignal(SignalName.OldPriceAmount, (offerData.price-offerData.discountAmount).ToString());
        EmitSignal(SignalName.OldPriceVisibility, offerData.isDiscountBundle);
        EmitSignal(SignalName.OwnedVisibility, false);
        //if (offerData.ownershipLoadComplete)
        //    ApplyOfferOwnership();

        loadingCubes.Visible = true;
        resourceTarget.Visible = false;
        resourceShift = offerData.resourceShift;
        if (offerData.resourceLoadComplete)
            ApplyResource();

        EmitSignal(SignalName.BonusTextVisibility, !string.IsNullOrWhiteSpace(offerData.bonusText));
        EmitSignal(SignalName.BonusTextChanged, offerData.bonusText);

        EmitSignal(SignalName.LastSeenVisibility, offerData.lastSeenDaysAgo > 1);
        EmitSignal(SignalName.LastSeenText, $"{offerData.lastSeenDaysAgo}");
        EmitSignal(SignalName.LastSeenAlertVisibility, offerData.isOld);
        EmitSignal(SignalName.AlmostAYearVisibility, offerData.isVeryOld);
    }

    void ClearCosmeticOfferData()
    {

        if (currentOfferData is null)
            return;
        currentOfferData.OnResourceLoaded -= ApplyOfferResource;

        currentOfferData = null;
    }

    public bool HasImage => imageUrl is not null;
    string shopUrl = null;
    string layoutId = null;
    string imageUrl = null;
    string imageDisplayAssetPath = null;
    Vector2 resourceShift = new(0.5f, 0.5f);
    bool resourceFit = false;

    public string offerId { get; private set; }
    public List<string> itemTypes { get; private set; } = [];
    int cellWidth = 0;
    public void PopulateEntry(JsonObject entryData, Vector2 cellSize)
    {
        offerId = entryData["offerId"].ToString();
        cellWidth = (int)cellSize.X;
        if (entryData["webURL"]?.ToString() is string extraWebURL)
            shopUrl = "https://www.fortnite.com" + extraWebURL;
        layoutId = entryData["layout"]?["id"]?.ToString();

        int oldPrice = entryData["regularPrice"].GetValue<int>();
        int newPrice = entryData["finalPrice"].GetValue<int>();
        isDiscountBundle = newPrice != oldPrice;
        if (isDiscountBundle)
            discountAmount = oldPrice - newPrice;
        else
            discountAmount = 0;

        EmitSignal(SignalName.BestsellerVisibility, entryData["isBestseller"]?.GetValue<bool>() == true);

        imageDisplayAssetPath = entryData["fallbackDisplayAsset"]?.ToString();

        JsonObject[] allItems = entryData.MergeCosmeticItems()?.Select(n=>n.AsObject())?.ToArray() ?? Array.Empty<JsonObject>();

        foreach (var item in allItems)
        {
            string type = item["type"]?["backendValue"]?.ToString();
            if (type is not null && !itemTypes.Contains(type))
                itemTypes.Add(type);
        }
        if (entryData["fallbackGrants"] is JsonArray fallbackItems)
        {
            foreach (var item in fallbackItems)
            {
                string type = item.ToString().Split(':')[0];
                if (!itemTypes.Contains(type))
                    itemTypes.Add(type);
            }
        }

        if (entryData["bundle"] is not null)
        {
            PopulateAsBundle(entryData, allItems);
        }
        else
        {
            PopulateAsItem(entryData, allItems);
        }

        outDate = entryData["outDate"].AsTime().ToUniversalTime();
        var resourceRender = entryData["newDisplayAsset"]?["renderImages"]?.AsArray()?.FirstOrDefault()?.AsObject();
        var resourceMat = entryData["newDisplayAsset"]?["materialInstances"]?.AsArray()?.FirstOrDefault()?.AsObject();

        if (cellSize.X == 4)
            resourceShift = new Vector2(0.5f, 0.125f);
        else if (cellSize.X == 3)
            resourceShift = new Vector2(0.5f, 0f);
        else
            resourceShift = new Vector2(0.5f, 0f);
        resourceFit = false;

        bgGradient ??= (backgroundGradientTexture.Texture as GradientTexture2D).Gradient;
        bgGradientDefaultColors ??= bgGradient.Colors;
        if (entryData["colors"] is JsonObject colors)
        {
            List<Color> bgColorList = [];
            for (int i = 1; i <= 3; i++)
            {
                if (colors["color"+i] is JsonValue colorVal)
                    bgColorList.Add(Color.FromHtml(colorVal.ToString()));
            }
            bgGradient.Colors = bgColorList.ToArray();
            List<float> bgOffsetList = [];

            for (int i = 0; i < bgColorList.Count; i++)
            {
                bgOffsetList.Add((float)i / (bgColorList.Count - 1));
            }
        }
        else
        {
            bgGradient.Colors = bgGradientDefaultColors;
            bgGradient.Offsets = bgGradientDefaultOffsets;
        }
        Color outlineColor = fallbackOutlineColor;
        if (entryData["colors"]?["textBackgroundColor"] is JsonValue outlineColorVal)
            outlineColor = Color.FromHtml(outlineColorVal.ToString());
        if (useOutlineColor)
            EmitSignal(SignalName.OutlineChanged, outlineColor);

        if (resourceRender is not null)
        {
            imageUrl =
                resourceRender["image"]?.ToString();
            if (resourceRender["productTag"]?.ToString() == "Product.DelMar")
            {
                //TODO: stretch image
                resourceShift = new Vector2(0.5f, 0.5f);
            }
        }
        else if (resourceMat?["images"]?["CarTexture"] is not null)
        {
            imageUrl =
                resourceMat["images"]?["CarTexture"]?.ToString();
            resourceShift = new Vector2(0.5f, 0.5f);
        }
        else if (resourceMat is not null)
        {
            imageUrl =
                resourceMat["images"]["Background"]?.ToString() ??
                resourceMat["images"]["OfferImage"]?.ToString();
        }
        else if (allItems.Any())
        {
            imageUrl =
                allItems[0]["images"]?["featured"]?.ToString() ??
                allItems[0]["images"]?["large"]?.ToString() ??
                allItems[0]["images"]?["small"]?.ToString() ??
                allItems[0]["images"]?["icon"]?.ToString() ??
                allItems[0]["images"]?["smallIcon"]?.ToString();
            resourceShift = new Vector2(0.5f, 0.5f);
        }
        else
            imageUrl = null;

        var nonLobbyMusicItems = allItems.Where(i => i["type"]?["displayValue"].ToString() != "Lobby Music").ToArray();
        if (nonLobbyMusicItems.Length == 1 && nonLobbyMusicItems[0]["type"]?["displayValue"].ToString() == "Jam Track")
        {
            resourceFit = true;
            resourceShift = new Vector2(0.5f, 0f);
            //TODO: if jam track, use dedicated jam track tile format
        }

        if (imageUrl is null && imageDisplayAssetPath  is not null && CatalogRequests.GetLocalCosmeticMeta(imageDisplayAssetPath) is JsonNode imageDA)
        {
            var possibleRenderImage = imageDA?["ContextualPresentations"]?[0]?["RenderImage"]?["AssetPathName"]?.ToString();
            if (possibleRenderImage is null)
                return;
            imageUrl = "https://fortnitecentral.genxgames.gg/api/v1/export?path=" + possibleRenderImage.Split('.')[0];
        }

        if (imageUrl is not null && CatalogRequests.GetLocalCosmeticResource(imageUrl) is Texture2D tex)
            ApplyResource(tex);
        else
            resourceTarget.Texture = null;
    }

    public struct CosmeticMetadata
    {
        public int lastSeenDaysAgo { get; private set; }
        public bool isRecentlyNew { get; private set; }
        public bool isAddedToday { get; private set; }
        public bool isLeavingSoon { get; private set; }
        public bool isOld { get; private set; }
        public bool isVeryOld { get; private set; }
        public bool isBestseller { get; private set; }
        public DateTime firstAddedDate { get; private set; }
        public DateTime? lastAddedDate { get; private set; }
        //public DateTime isVeryOld { get; private set; }

        public CosmeticMetadata(JsonObject firstItem, JsonObject entryData)
        {
            DateTime inDate = DateTime.Parse(entryData["inDate"].ToString()).ToUniversalTime();
            DateTime outDate = DateTime.Parse(entryData["outDate"].ToString()).ToUniversalTime();
            isBestseller = entryData?["isBestseller"]?.GetValue<bool>() == true;

            var shopHistory = firstItem?["shopHistory"]?.AsArray();
            var type = firstItem?["type"]?["backendValue"]?.ToString();
            firstAddedDate = shopHistory?[0]?.ToString() is string firstAddedDateText ? 
                DateTime.Parse(firstAddedDateText).ToUniversalTime() : 
                (
                    type == "CosmeticVariantToken" ? 
                        DateTime.MinValue : 
                        DateTime.UtcNow.Date
                );
            lastAddedDate = null;
            if (shopHistory is not null)
            {
                for (int i = shopHistory.Count - 1; i >= 0; i--)
                {
                    DateTime shopDate = DateTime.Parse(shopHistory[i].ToString()).ToUniversalTime();
                    if (shopDate.CompareTo(inDate) == -1)
                    {
                        lastAddedDate = shopDate;
                        break;
                    }
                }
            }

            lastSeenDaysAgo = lastAddedDate.HasValue ? (int)(DateTime.UtcNow.Date - lastAddedDate.Value).TotalDays : 0;
            isAddedToday = inDate == DateTime.UtcNow.Date/* && (lastSeenDaysAgo > 1 || DateTime.UtcNow.Date == firstAddedDate)*/;
            isRecentlyNew = (DateTime.UtcNow.Date - firstAddedDate).TotalDays < 7 && entryData["fallbackGrants"]?.AsArray()?.Any(n => n.ToString().StartsWith("CosmeticVariantToken:")) != true;
            isLeavingSoon = (outDate - DateTime.UtcNow.Date).TotalHours < 24;

            int oldThreshold = AppConfig.Get("item_shop", "oldThreshold", 500);
            isOld = lastSeenDaysAgo > oldThreshold;
            isVeryOld = lastSeenDaysAgo > Mathf.Max(oldThreshold, AppConfig.Get("item_shop", "veryOldThreshold", 500));
        }

        public CosmeticMetadata(CosmeticMetadata[] itemMetadatas)
        {
            if (itemMetadatas.Length == 0)
            {
                this = default;
                return;
            }
            lastSeenDaysAgo = itemMetadatas.Select(m => m.lastSeenDaysAgo).Max();
            isRecentlyNew = itemMetadatas.Any(m => m.isRecentlyNew);
            isAddedToday = itemMetadatas.Any(m => m.isAddedToday);
            isLeavingSoon = itemMetadatas.Any(m => m.isLeavingSoon);
            isBestseller = itemMetadatas.Any(m => m.isBestseller);
            isOld = itemMetadatas.Any(m => m.isOld);
            isVeryOld = itemMetadatas.Any(m => m.isVeryOld);

            firstAddedDate = itemMetadatas.Select(m => m.firstAddedDate).OrderBy(d => d).FirstOrDefault();
            lastAddedDate = itemMetadatas.Select(m => m.lastAddedDate).OrderBy(d => d).FirstOrDefault();
        }
    }

    public CosmeticMetadata metadata;
    public int discountAmount { get; private set; }
    public bool isDiscountBundle { get; private set; }

    void PopulateAsItem(JsonObject entryData, JsonObject[] allItems)
    {
        JsonObject firstItem = allItems.FirstOrDefault();

        string extraItemsText = null;
        string fullExtraItemsText = null;
        if (allItems.Length > 1)
        {
            extraItemsText = $" (+{allItems.Length - 1})";
            fullExtraItemsText = allItems
                .GroupBy(i => i["type"]?["displayValue"].ToString())
                .Select(g => g.Key + (g.Count() > 1 ? " x" + g.Count() : ""))
                .ToArray()
                .Join(", ");
        }

        string mainName = firstItem?["name"]?.ToString() ?? "<Unknown>";
        string mainType = firstItem?["type"]?["displayValue"].ToString() ?? (entryData["dynamicBundleInfo"] is null ? "<Item>" : "<Bundle>");
        Name = mainName;

        EmitSignal(SignalName.NameChanged, mainName);
        EmitSignal(SignalName.TypeChanged, mainType + (extraItemsText ?? ""));

        int oldPrice = entryData["regularPrice"].GetValue<int>();
        int newPrice = entryData["finalPrice"].GetValue<int>();

        EmitSignal(SignalName.PriceAmount, newPrice.ToString());
        EmitSignal(SignalName.OldPriceAmount, oldPrice.ToString());

        EmitSignal(SignalName.OwnedVisibility, false);
        EmitSignal(SignalName.OldPriceVisibility, newPrice < oldPrice);

        metadata = new(firstItem, entryData);
        SetMetaVisuals();


        string tooltip = mainName + " - " + mainType;
        if (fullExtraItemsText is not null)
            tooltip += "\nContents include: " + fullExtraItemsText;

        EmitSignal(SignalName.TooltipChanged, tooltip);
    }

    void PopulateAsBundle(JsonObject entryData, JsonObject[] allItems)
    {
        string mainName = entryData["bundle"]["name"].ToString();

        EmitSignal(SignalName.NameChanged, mainName);
        EmitSignal(SignalName.TypeChanged, $"Bundle [{allItems.Length} items]");

        int oldPrice = entryData["regularPrice"].GetValue<int>();
        int newPrice = entryData["finalPrice"].GetValue<int>();

        EmitSignal(SignalName.PriceAmount, newPrice.ToString());
        EmitSignal(SignalName.OldPriceAmount, oldPrice.ToString());

        EmitSignal(SignalName.OwnedVisibility, false);
        EmitSignal(SignalName.OldPriceVisibility, newPrice < oldPrice);

        metadata = new(allItems.Select(item=>new CosmeticMetadata(item.AsObject(), entryData)).ToArray());
        SetMetaVisuals();

        string tooltip = mainName + " - Bundle";
        tooltip += "\nContents include: " + allItems
                .GroupBy(i => i["type"]?["displayValue"].ToString())
                .Select(g => g.Key + (g.Count() > 1 ? " x" + g.Count() : ""))
                .ToArray()
                .Join(", ");
        EmitSignal(SignalName.TooltipChanged, tooltip);
    }

    void SetMetaVisuals()
    {
        EmitSignal(SignalName.LastSeenVisibility, metadata.lastSeenDaysAgo > 1);
        EmitSignal(SignalName.LastSeenText, $"{metadata.lastSeenDaysAgo}d");
        EmitSignal(SignalName.LastSeenAlertVisibility, metadata.isOld);
        EmitSignal(SignalName.AlmostAYearVisibility, metadata.isVeryOld);

        if (discountAmount > 0)
        {
            EmitSignal(SignalName.BonusTextChanged, cellWidth > 1 ? $"{discountAmount} V-Bucks Off!" : $"-{discountAmount}");
            EmitSignal(SignalName.BonusTextVisibility, true);
            return;
        }

        if (metadata.isRecentlyNew && metadata.isAddedToday)
        {
            //GD.Print($"New {firstAddedDate}");
            EmitSignal(SignalName.BonusTextChanged, "NEW TODAY");
        }
        else if (metadata.isRecentlyNew)
        {
            //GD.Print($"Recent {firstAddedDate} ({DateTime.UtcNow.Date - firstAddedDate})");
            EmitSignal(SignalName.BonusTextChanged, "NEW");
        }
        else if (metadata.isAddedToday)
        {
            //GD.Print($"Back {lastAddedDate} ({DateTime.UtcNow.Date - firstAddedDate})");
            EmitSignal(SignalName.BonusTextChanged, "#");
        }

        EmitSignal(SignalName.BonusTextVisibility, metadata.isRecentlyNew || metadata.isAddedToday);
    }
}
