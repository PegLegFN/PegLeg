using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using static ExternalCosmetics;

public class GameOffer
{
    public event Action OnChanged;
    public event Action OnRemoving;
    public event Action OnRemoved;

    public GameStorefront storefront { get; private set; }
    public JsonObject rawData { get; private set; }
    public JsonNode this[string propertyName] => rawData[propertyName];

    public string OfferId => rawData["offerId"].ToString();
    JsonObject metadata;
    public int? GetMetaInt(string key) => int.TryParse(GetMeta(key), out var iVal) ? iVal : null;
    public string GetMeta(string key)
    {
        if (metadata[key] is JsonNode metaVal)
            return metaVal.ToString();
        var metaInfoTarget = rawData["metaInfo"]?
            .AsArray()
            .FirstOrDefault(val => val["key"].ToString() == key)
            ?.AsObject();
        if (metaInfoTarget is null)
            return null;
        return (metadata[key] = metaInfoTarget["value"].ToString()).ToString();
    }

    public bool FakeOffer { get; private set; }
    public string Title => rawData["title"]?.ToString();
    public bool IsXRayLlama => GetMeta("Preroll") == "True";

    public int SimultaniousLimit => GetMetaInt("MaxConcurrentPurchases") ?? -1;
    public int DailyLimit => rawData["dailyLimit"]?.GetValue<int>() ?? -1;
    public int WeeklyLimit => rawData["weeklyLimit"]?.GetValue<int>() ?? -1;
    public int MonthlyLimit => rawData["monthlyLimit"]?.GetValue<int>() ?? -1;
    public int EventLimit => GetMetaInt("EventLimit") ?? -1;
    public string EventId => GetMeta("PurchaseLimitingEventId");

    public string Color0 => GetMeta("textBackgroundColor");
    public string Color1 => GetMeta("color1");
    public string Color2 => GetMeta("color2");

    public DateTime? InDate => GetMeta("inDate") is string inDate ? DateTime.Parse(inDate) : null;
    public DateTime? OutDate => GetMeta("outDate") is string outDate ? DateTime.Parse(outDate) : null;

    Dictionary<string, int> GenerateRequirementList(string type) =>
        (rawData["requirements"]?.AsArray() ?? [])
        .Where(n => n["requirementType"]?.ToString() == type)
        .ToDictionary(
            n => n["requiredId"].ToString(),
            n => n["minQuantity"].GetValue<int>()
        );

    Dictionary<string, int> fulfillmentDenyList;
    public Dictionary<string, int> FulfillmentDenyList => fulfillmentDenyList ??= GenerateRequirementList("DenyOnFulfillment");

    Dictionary<string, int> fulfillmentRequireList;
    public Dictionary<string, int> FulfillmentRequireList => fulfillmentRequireList ??= GenerateRequirementList("RequireFulfillment");

    Dictionary<string, int> itemDenyList;
    public Dictionary<string, int> ItemDenyList => itemDenyList ??= GenerateRequirementList("DenyOnItemOwnership");

    GameItem basePrice;
    public GameItem BasePrice => basePrice;
    int discountAmount = 0;
    int discountMin = 0;
    public bool IsFree => discountMin == 0 && discountAmount >= basePrice.quantity;
    public bool IsDiscountBundle => conditionalDiscounts?.Count > 0;

    Dictionary<string, int> conditionalDiscounts;
    GameItem price;
    public GameItem Price => price ??= GetRegularPrice();

    public GameItem[] itemGrants { get; private set; }

    public GameOffer(GameStorefront storefront, JsonObject rawData)
    {
        this.storefront = storefront;
        SetRawData(rawData);
    }
    GameOffer() { }
    public static GameOffer CreateFake(GameItem[] grants, GameItem price, int limit = 1, JsonObject rawData = null)
    {
        rawData ??= [];
        rawData["itemGrants"] = new JsonArray([.. grants.Select(i => i.SimpleRawData)]);
        if(price is not null)
        {
            rawData["prices"] = JsonNode.Parse(
            $$"""
            [
                {
                  "currencyType": "GameItem",
                  "currencySubType": "{{price.templateId}}",
                  "regularPrice": {{price.quantity}},
                  "dynamicRegularPrice": -1,
                  "finalPrice": {{price.quantity}},
                  "saleExpiration": "9999-12-31T23:59:59.999Z",
                  "basePrice": {{price.quantity}}
                }
            ]
            """);
        }
        rawData["dailyLimit"] = limit > 0 ? limit : -1;
        rawData["offerId"] ??= Guid.NewGuid();

        return new()
        {
            rawData = rawData,
            metadata = rawData["meta"]?.AsObject() ?? [],
            itemGrants = grants,
            basePrice = price,
            FakeOffer = true
        };
    }

    public void SetRawData(JsonObject rawData)
    {
        this.rawData = rawData;
        itemGrants = [.. rawData["itemGrants"].AsArray().Select(n => new GameItem(null, null, n.AsObject()))];
        metadata = rawData["meta"]?.AsObject() ?? [];

        if (rawData["dynamicBundleInfo"] is JsonObject dynamicBundleInfo)
        {
            var priceTemplateId = dynamicBundleInfo["currencyType"].ToString() == "MtxCurrency" ? "Currency:mtxpurchased" : dynamicBundleInfo["currencySubType"].ToString();
            var priceTemplate = GameItemTemplate.Get(priceTemplateId);

            discountAmount = -dynamicBundleInfo["discountedBasePrice"].GetValue<int>();
            discountMin = dynamicBundleInfo["floorPrice"].GetValue<int>();
            var itemsArray = dynamicBundleInfo["bundleItems"].AsArray();
            int basePriceAmount = itemsArray.Select(n => n["regularPrice"].GetValue<int>()).Sum();

            conditionalDiscounts = new(
                itemsArray
                    .Where(n => n["alreadyOwnedPriceReduction"].GetValue<int>() > 0)
                    .Select(n => new KeyValuePair<string, int>(
                        n["item"]["templateId"].ToString(), 
                        n["alreadyOwnedPriceReduction"].GetValue<int>()
                    ))
            );

            basePrice = priceTemplate?.CreateInstance(basePriceAmount);
        }
        else if (rawData["prices"][0]?.AsObject() is JsonObject priceData)
        {
            var priceTemplateId = priceData["currencyType"].ToString() == "MtxCurrency" ? "Currency:mtxpurchased" : priceData["currencySubType"].ToString();
            var priceTemplate = GameItemTemplate.Get(priceTemplateId);
            int basePriceAmount = priceData["regularPrice"].GetValue<int>();
            conditionalDiscounts = null;
            discountAmount = basePriceAmount - priceData["finalPrice"].GetValue<int>();
            discountMin = 0;
            basePrice = priceTemplate?.CreateInstance(basePriceAmount);
        }
        
        price = null;
    }

    GameItem GetRegularPrice()
    {
        int price = basePrice?.quantity ?? 0;
        price -= discountAmount;
        price = Mathf.Max(price, discountMin);
        var newPriceItem = basePrice?.template?.CreateInstance(price);

        return newPriceItem;
    }

    public async Task<int> GetPriceInInventory(GameAccount account = null)
    {
        account ??= GameAccount.ActiveAccount;
        var accountItems = await account.GetProfile(FnProfileTypes.AccountItems).Query();
        return accountItems.GetFirstTemplateItem(basePrice?.templateId)?.quantity ?? 0;
    }

    public async Task<GameItem> GetCurrencyItem(GameAccount account = null)
    {
        account ??= GameAccount.ActiveAccount;
        var accountItems = await account.GetProfile(FnProfileTypes.AccountItems).Query();
        return accountItems.GetFirstTemplateItem(basePrice?.templateId);
    }

    public async Task<GameItem> CalculatePersonalPrice(GameAccount account = null, bool forceCosmetics = false)
    {
        int price = basePrice?.quantity ?? 0;
        price -= discountAmount;

        //if dynamic bundle, generate discount based on owned items
        account ??= GameAccount.ActiveAccount;
        if (IsDiscountBundle && await account.Authenticate())
        {
            var cosmeticItems = await account.GetProfile(FnProfileTypes.CosmeticInventory).Query(forceFetch: forceCosmetics);
            foreach (var kvp in conditionalDiscounts)
            {
                if (cosmeticItems.GetFirstTemplateItem(kvp.Key) is not null)
                    price -= kvp.Value;
            }
        }

        price = Mathf.Max(price, discountMin);

        return basePrice?.template?.CreateInstance(price);
    }

    public async Task<GameItem> GetXRayLlamaData(GameAccount account = null)
    {
        if (!IsXRayLlama)
            return null;
        account ??= GameAccount.ActiveAccount;
        await account.GetProfile(FnProfileTypes.AccountItems).Query();
        return GetLocalXRayLlamaData(account);
    }

    public GameItem GetLocalXRayLlamaData(GameAccount account = null)
    {
        if (!IsXRayLlama)
            return null;
        account ??= GameAccount.ActiveAccount;
        var prerollItems = account.GetProfile(FnProfileTypes.AccountItems).GetItems("PrerollData");
        var match = prerollItems.FirstOrDefault(item => item.attributes?["offerId"].ToString() == OfferId);
        return match;
    }

    public void NotifyChanged()
    {
        OnChanged?.Invoke();
    }

    public void NotifyRemoving() => OnRemoving?.Invoke();
    public void DisconnectFromStorefront()
    {
        storefront = null;
        OnRemoved?.Invoke();
    }

    #region Cosmetic Stuff
    public string CosmeticDisplayAssetPath => rawData["displayAssetPath"]?.ToString();
    public string CosmeticNewDisplayAssetPath => GetMeta("NewDisplayAssetPath");
    public string CosmeticLayoutId => GetMeta("LayoutId");
    public string CosmeticSectionId
    {
        get
        {
            var layout = CosmeticLayoutId;
            if (!layout.Contains('.'))
                return null;
            return layout.Split('.')[0];
        }
    }
    public string CosmeticGroupId
    {
        get
        {
            var layout = CosmeticLayoutId;
            if (!layout.Contains('.'))
                return null;
            return layout.Split('.')[1];
        }
    }
    public int CosmeticSortPriority => rawData["sortPriority"]?.GetValue<int>() ?? 0;

    public FNDashOffer FNDashOffer => fnDashOffer?.Valid == true ? FNDashOffer : (fnDashOffer = GetFNDashOffer(OfferId));
    FNDashOffer fnDashOffer;
    public FNDotDisplayAsset FNDotDisplayAsset => fnDotDisplayAsset ??= GetFNDotDisplayAsset(CosmeticNewDisplayAssetPath);
    FNDotDisplayAsset fnDotDisplayAsset;
    public RawDisplayAsset RawDisplayAsset => rawDisplayAsset ??= LoadLocalRawDisplayAsset(CosmeticDisplayAssetPath);
    RawDisplayAsset rawDisplayAsset;
    public RawCosmetic[] RawCosmetics => rawCosmetics ?? [];
    RawCosmetic[] rawCosmetics;

    public void LoadLocalCosmetics()
    {
        rawCosmetics = new RawCosmetic[itemGrants.Length];
        for (int i = 0; i < itemGrants.Length; i++)
        {
            rawCosmetics[i] = LoadLocalRawCosmetic(itemGrants[i].templateId);
        }
    }

    public async Task LoadCosmetics()
    {
        rawCosmetics = new RawCosmetic[itemGrants.Length];
        List<Task> loadTask = [];
        for (int i = 0; i < itemGrants.Length; i++)
        {
            int index = i;
            async Task CosmeticSubtask()
            {
                rawCosmetics[index] = await LoadRawCosmetic(itemGrants[index].templateId);
            }
            loadTask.Add(CosmeticSubtask());
        }
        await Task.WhenAll(loadTask);
    }

    public async Task LoadFirstCosmetic()
    {
        rawCosmetics = new RawCosmetic[itemGrants.Length];
        if (itemGrants.Length > 0)
            rawCosmetics[0] = await LoadRawCosmetic(itemGrants[0].templateId);
    }

    ImageTexture cosmeticImage;
    public ImageTexture CosmeticImage => cosmeticImage ??=
        FNDashOffer?.GetLocalOfferImage() ??
        FNDotDisplayAsset?.GetLocalOfferImage();

    public string CosmeticName
    {
        get
        {
            if (FNDashOffer is not null)
                return FNDashOffer.DisplayName;
            if (RawDisplayAsset is not null)
                return RawDisplayAsset.properties.DisplayName;
            if ((RawCosmetics?.Length ?? 0) > 0)
                return RawCosmetics[0].properties.ItemName;
            return "<Unknown>";
        }
    }

    public string CosmeticType
    {
        get
        {
            if (FNDashOffer is not null)
                return FNDashOffer.DisplayType;
            if ((RawCosmetics?.Length ?? 0) > 0)
                return RawCosmetics[0].properties.ItemShortDescription + (RawCosmetics.Length > 1 ? $" (+{RawCosmetics.Length - 1})" : "");
            return "<Unknown>";
        }
    }

    CosmeticMeta? cosmeticMetaData;
    public CosmeticMeta CosmeticMetaData
    {
        get
        {
            if (cosmeticMetaData is not null)
                return (cosmeticMetaData).Value;
            if (FNDashOffer is not null)
                return (cosmeticMetaData = FNDashOffer.GenerateCosmeticMeta()).Value;
            return (cosmeticMetaData = new()
            {
                lastSeenDaysAgo = 0,
                isRecentlyNew = GetMeta("ViolatorTag") == "New",
                isAddedToday = InDate.Value == DateTime.UtcNow.Date,
                isLeavingSoon = (OutDate.Value - DateTime.UtcNow.Date).TotalHours < 24,
                lastAddedDate = DateTime.UtcNow.Date
            }).Value;
        }
    }

    public async Task LoadDisplayAssetData()
    {
        if (FNDashOffer is not null)
            cosmeticImage = await FNDashOffer.LoadOfferImage();
        else
            rawDisplayAsset = await LoadRawDisplayAsset(CosmeticDisplayAssetPath);
        if (FNDotDisplayAsset is not null)
            cosmeticImage = await FNDotDisplayAsset.LoadOfferImage();
    }
    #endregion
}