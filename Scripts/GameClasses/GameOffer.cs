using Amazon.Runtime.Internal.Endpoints.StandardLibrary;
using Amazon.Runtime.Telemetry;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using XmppDotNet.Xmpp.HttpUpload;
using static ExternalCosmetics;

public partial class GameOffer
{
	public event Action OnChanged;
	public event Action OnRemoving;
	public event Action OnRemoved;
	public event Action<ImageTexture> OnCosmeticImageRecieved;

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
	public int SortPriority => rawData["sortPriority"]?.GetValue<int>() ?? 0;

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
	public bool IsDynamicBundle => conditionalDiscounts?.Count > 0;

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
		rawData["itemGrants"] = JsonSerializer.SerializeToNode(grants.Select(g => g.GameItemData).ToArray());
		if (price is not null)
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

		if(OfferId== "v2:/2cc86f652a3db4537279b78c1ff60458a441c5113a3245d356c14a25b0645c11")
		{
			GD.Print("testing price stuff");
		}

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

			basePrice = new(GameItemTemplate.Get(priceTemplateId), basePriceAmount, templateId: priceTemplateId);
		}
		else if (rawData["prices"][0]?.AsObject() is JsonObject priceData)
		{
			var priceTemplateId = priceData["currencyType"].ToString() == "MtxCurrency" ? "Currency:mtxpurchased" : priceData["currencySubType"].ToString();
			var priceTemplate = GameItemTemplate.Get(priceTemplateId);
			int basePriceAmount = priceData["regularPrice"].GetValue<int>();
			conditionalDiscounts = null;
			discountAmount = basePriceAmount - priceData["finalPrice"].GetValue<int>();
			discountMin = 0;
			basePrice = new(GameItemTemplate.Get(priceTemplateId), basePriceAmount, templateId: priceTemplateId);
		}

		price = null;
	}

	GameItem GetRegularPrice()
	{
		int price = basePrice?.quantity ?? 0;
		price -= discountAmount;
		price = Mathf.Max(price, discountMin);
		var newPriceItem = basePrice?.Clone(price);

		return newPriceItem;
	}

	public async Task<int> GetPriceInInventory(GameAccount account = null)
	{
		account ??= GameAccount.ActiveAccount;
		var accountItems = await account.GetProfile(FnProfileTypes.AccountItems).Query();
		return accountItems?.GetFirstTemplateItem(basePrice?.templateId)?.quantity ?? 0;
	}

	public async Task<GameItem> GetCurrencyItem(GameAccount account = null)
	{
		account ??= GameAccount.ActiveAccount;
		var accountItems = await account.GetProfile(FnProfileTypes.AccountItems).Query();
		return accountItems?.GetFirstTemplateItem(basePrice?.templateId);
	}

	public async Task<GameItem> CalculatePersonalPrice(GameAccount account = null, bool forceCosmetics = false)
	{
		int price = basePrice?.quantity ?? 0;
		price -= discountAmount;

		//if dynamic bundle, generate discount based on owned items
		account ??= GameAccount.ActiveAccount;
		if (IsDynamicBundle && await account.Authenticate())
		{
			var cosmeticItems = await account.GetProfile(FnProfileTypes.CosmeticInventory).Query(ignoreCache: forceCosmetics);
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

	[GeneratedRegex("""\[VIRTUAL](?:\d+ x ([^,]+),?)(?: \d+ x [^,]+,?)*for -?\d+ MtxCurrency""")]
	private static partial Regex DevNameParser();
	public string ParseCosmeticOfferName()
	{
		var parseMatch = DevNameParser().Match(rawData["devName"]?.ToString());
		if (parseMatch.Success && parseMatch.Groups.Count > 1)
			return parseMatch.Groups[1].Value;
		return null;
	}
	public string CosmeticDisplayAssetPath => rawData["displayAssetPath"]?.ToString();
	public string CosmeticNewDisplayAssetPath => GetMeta("NewDisplayAssetPath");
	public string CosmeticOfferMainType => GetMeta("OfferMainType");
	public string CosmeticPrimaryTemplate => GetMeta("PrimaryTemplateId") ?? GetMeta("templateId");
	public string CosmeticTagline => GetMeta("ViolatorTag");
	public CosmoRequests.CosmoImageData? CosmeticDAV2Image
	{
		get
		{
			if (CosmeticNewDisplayAssetPath is not string path)
				return null;
			var split = path.Split('.');
			if (split.Length != 2)
				return null;
			return CosmoRequests.GetDisplayAsset(split[^1]);
		}
	}
	public Color[] CosmeticBGColours
	{
		get
		{
			Color[] result = [
				Color.FromHtml(GetMeta("color1")),
				Color.FromHtml(GetMeta("color2")),
			];
			if(GetMeta("color3") is string third)
			{
				Array.Resize(ref result, result.Length + 1);
				result[^1]= Color.FromHtml(third);
			}
			return result;
		}
	}
	public Color CosmeticTextBGColour => GetMeta("textBackgroundColor") is string textCol ? Color.FromHtml(textCol) : Colors.Black;
	public string CosmeticLayoutId => GetMeta("LayoutId");
	public string CosmeticSectionId
	{
		get
		{
			var layout = CosmeticLayoutId;
			if (!layout.Contains('.'))
				return layout;
			return layout.Split('.')[0];
		}
	}
	public string CosmeticRowGroupId
	{
		get
		{
			var layout = CosmeticLayoutId;
			if (!layout.Contains('.'))
				return null;
			return layout.Split('.')[1];
		}
	}
	public Vector2I CosmeticTileSize
	{
		get
		{
			var tileSizeString = GetMeta("TileSize");
			if (!tileSizeString.StartsWith("Size_"))
				return Vector2I.One;
			tileSizeString = tileSizeString[5..];
			if (!tileSizeString.Contains("_x_"))
				return Vector2I.One;
			var split = tileSizeString.Split("_x_");
			if(split.Length!=2 || !int.TryParse(split[0], out var xSize) || !int.TryParse(split[1], out var ySize))
				return Vector2I.One;
			return new(xSize, ySize);
		}
	}

	public string CosmeticURL
	{
		get
		{
			if (GetMeta("webURL") is not string extraWebURL)
				return null;

			var splitURLEnding = extraWebURL.Split('/')[^1].Split('-');
			if (extraWebURL.StartsWith("/item-shop/jam-tracks/") && GameStorefront.TryGetJamTrack(CosmeticPrimaryTemplate, out var jamMeta))
			{
				var firstItemName = Regex.Replace(jamMeta.title.ToLower(), "[^ \\w]+", "");
				firstItemName = Regex.Replace(firstItemName, "[ ]+", " ");
				firstItemName = firstItemName.Replace(" ", "-");
				var urlId = jamMeta.songUUID.Split('-')[^1];
				extraWebURL = $"/item-shop/jam-tracks/{firstItemName}-{urlId}";
			}
			else
			{
				//either figure out how to generate the URL hash out of car offers, or hope epic fixes their stuff
				extraWebURL = splitURLEnding[^2] switch
				{
					"wheels" or "boost" or "trail" => null,
					//"wheels" => $"/item-shop/wheels/{string.Join("-", splitURLEnding[..^2])}-{splitURLEnding[^1]}",
					//"boost" => $"/item-shop/boosts/{string.Join("-", splitURLEnding[..^2])}-{splitURLEnding[^1]}",
					//"trail" => $"/item-shop/trails/{string.Join("-", splitURLEnding[..^2])}-{splitURLEnding[^1]}",
					_ => extraWebURL
				};
			}
			if (extraWebURL is null)
				return null;
			return "https://www.fortnite.com" + extraWebURL;
		}
	}
	float imageResolution => CosmeticTileSize.X;
	public ImageTexture CosmeticCachedDisplayImage
	{
		get
		{
			if (CosmeticDAV2Image?.GetCachedTexture() is ImageTexture cosmoResult)
				return cosmoResult;
			if (TryGetJamMeta(out var jamMeta) && jamMeta.GetCachedTexture() is ImageTexture jamResult)
				return jamResult;
			if (FNDashOffer?.GetCachedOfferImage() is ImageTexture fnDashResult)
				return fnDashResult;
			return null;
		}
	}

	public ImageTexture CosmeticLocalDisplayImage
	{
		get
		{
			if(CosmeticDAV2Image?.GetLocalTexture(imageResolution) is ImageTexture cosmoResult)
				return cosmoResult;
			if (TryGetJamMeta(out var jamMeta) && jamMeta.GetLocalTexture(imageResolution) is ImageTexture jamResult)
				return jamResult;
			if (FNDashOffer?.GetLocalOfferImage(imageResolution) is ImageTexture fnDashResult)
				return fnDashResult;
			return null;
		}
	}

	public Image ReadCosmeticDisplayImageDirect()
	{
		if (CosmeticDAV2Image?.ReadLocalImageDirect() is Image cosmoResult)
			return cosmoResult;
		if (TryGetJamMeta(out var jamMeta) && jamMeta.ReadLocalImageDirect() is Image jamResult)
			return jamResult;
		if (FNDashOffer?.ReadLocalOfferImageDirect() is Image fnDashResult)
			return fnDashResult;
		return null;
	}

	public async void FetchDisplayAssetImage()
	{
		ImageTexture result = null;
		if (CosmeticDAV2Image is { } realImage)
			result = await realImage.FetchTexture(imageResolution);
		if (result is null && TryGetJamMeta(out var jamMeta))
			result = await jamMeta.FetchTexture();
		if (result is null && FNDashOffer is not null)
			result = await FNDashOffer.FetchOfferImage(imageResolution);
		OnCosmeticImageRecieved?.Invoke(result);
	}

	GameStorefront.JamTrackMeta? cachedJamMeta;
	bool TryGetJamMeta(out GameStorefront.JamTrackMeta jamMeta)
	{
		jamMeta = cachedJamMeta ?? default;
		if(cachedJamMeta is not null)
			return true;
		if (CosmeticPrimaryTemplate is not string primaryTemplate || !primaryTemplate.StartsWith("SparksSong"))
			return false;
		if (!GameStorefront.TryGetJamTrack(primaryTemplate, out jamMeta))
			return false;
		cachedJamMeta = jamMeta;
		return true;
	}

	#endregion

	#region Third Party Cosmetic Stuff
	public FNDashOffer FNDashOffer => fnDashOffer?.Valid == true ? fnDashOffer : (fnDashOffer = GetFNDashOffer(OfferId));
	FNDashOffer fnDashOffer;

	public string CosmeticDisplayName => FNDashOffer?.DisplayName.Replace("\\\"", "\"") ?? ParseCosmeticOfferName();
	public string CosmeticDisplayType => FNDashOffer?.DisplayType;

	CosmeticTimeData? cosmeticTimeData;
	CosmeticTimeData? estimateCosmeticTimeData;
	public CosmeticTimeData CosmeticTimeData
	{
		get
		{
			if (cosmeticTimeData is not null)
				return cosmeticTimeData.Value;
			if (FNDashOffer is not null)
				return cosmeticTimeData ??= FNDashOffer.GenerateCosmeticTimeData();

			if (estimateCosmeticTimeData is not null)
				return estimateCosmeticTimeData.Value;
			return estimateCosmeticTimeData ??= new()
			{
				lastSeenDaysAgo = 0,
				isRecentlyNew = CosmeticTagline == "New",
				isAddedToday = InDate.Value == DateTime.UtcNow.Date,
				isLeavingSoon = (OutDate.Value - DateTime.UtcNow.Date).TotalHours < 24,
				lastAddedDate = DateTime.UtcNow.Date
			};
		}
	}
	#endregion
}