using Godot;
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

static class CatalogRequests
{
    static JsonObject storefrontCache;

    //static JsonObject[] llamaCache;
    //public static async Task<JsonObject[]> GetLlamaShop(bool forceRefresh = false)
    //{
    //    if(!StorefrontRequiresUpdate() && !forceRefresh && llamaCache is not null)
    //        return llamaCache;

    //    await EnsureStorefront(forceRefresh);

    //    var prerollOffers = storefrontCache[XRayLlamaCatalog].AsArray();

    //    if (!prerollOffers[0].AsObject().ContainsKey("prerollData"))
    //    {
    //        //assume that prerolls havent been generated for any offer
    //        var prerollData = await GameAccount.activeAccount.GetAllPrerollData();
    //        for (int i = 0; i < prerollOffers.Count; i++)
    //        {
    //            var thisOffer = prerollOffers[i].AsObject();
    //            var thisPreroll = prerollData.FirstOrDefault(item => item.attributes?["offerId"]?.ToString() == thisOffer["offerId"].ToString());
    //            thisPreroll ??= prerollData.FirstOrDefault(item => item.attributes?["linked_offer"]?.ToString() == "OfferId:" + thisOffer["offerId"].ToString());
    //            if (thisPreroll is not null)
    //                thisOffer["prerollData"] = thisPreroll.attributes.Reserialise();
    //        }
    //    }
    //    llamaCache = prerollOffers
    //        .Select(val=>val.AsObject().Reserialise())
    //        .Union(
    //            storefrontCache[RandomLlamaCatalog]
    //            .AsArray()
    //            .Select(val=>val.AsObject().Reserialise())
    //        )
    //        .ToArray();

    //    return llamaCache;
    //}


    static JsonObject cosmeticCache;
    public static async Task<JsonObject> GetCosmeticShop(bool forceRefresh = false)
    {
        if (!StorefrontRequiresUpdate() && !forceRefresh && cosmeticCache is not null)
            return cosmeticCache;

        var layoutTask = GetCosmeticLayouts(true);
        var storefrontTask = EnsureStorefront(forceRefresh);
        var cosmeticDisplayData = await RequestCosmeticDisplayData();
        var bestsellingCosmetics = await RequestCosmeticBestsellingData();
        var jamTrackData = await RequestJamtrackData();
        await storefrontTask;
        await layoutTask;
        return cosmeticCache = ProcessCosmetics(cosmeticDisplayData, bestsellingCosmetics, jamTrackData);
    }

    public static JsonObject GetCachedCosmeticOfferData(string offerId)
    {
        return null;
    }

    //static JsonObject weeklyCache;
    //public static async Task<JsonObject> GetWeeklyShop(bool forceRefresh = false)
    //{
    //    if (!StorefrontRequiresUpdate() && !forceRefresh && weeklyCache is not null)
    //        return weeklyCache;
    //    await EnsureStorefront(forceRefresh);

    //    return weeklyCache = ProcessShop(WeeklyShopCatalog);
    //}

    //static JsonObject eventCache;
    //public static async Task<JsonObject> GetEventShop(bool forceRefresh = false)
    //{
    //    if (!StorefrontRequiresUpdate() && !forceRefresh && eventCache is not null)
    //        return eventCache;
    //    await EnsureStorefront(forceRefresh);

    //    return eventCache = ProcessShop(EventShopCatalog);
    //}

    static readonly Dictionary<string, string[]> defaultShops = new()
    {
        [FnStorefrontTypes.WeeklyShopCatalog] =
        [
            "v2:/8833e6245fe4bf6f0a87e2d248398ec079aac302a1d0b17d036cdd6a1f485d85",
            "v2:/a3eeb54f8f9d2f32ba2f1769a095a9fa406a5c6f239235a8d810d7263cd727e5",
            "v2:/485f70bb37ced8eb25c4b4e42302ee5274532823c17091afb486e1879c4ecc16",
            "v2:/9b91076467e61cf01a3c16e39a18331d2e23d754cdafc860aac0fdd7155615ae",
            "v2:/365d69d31591ba699bdf2c89730b8fa02883302ac56d1bd43b06d81f2ef25f0e",
            "v2:/d9fe40e917bf98babee1c239153990efe3e1a568dd0e985c663dbba228eef03f",
            "v2:/bfd337ddb7380a663929ae0ad03f6cdbff5b562d1639c8c813cb8316b37f83bb",
            "v2:/d8c8f59ca26294a0192676567f75ee6c3631f96eea201fd14f8cac0c47acfb5c",
            "v2:/4f1c82dc8fb66fef5a0046fb2163344069b65b6ba64e496939d2fc8e8f779157",
            "v2:/9af32d7a9a16f864eae99d17542ec08763d118f3ce9c72ad05d5fc5f44586dc1",
            "v2:/fd2b5edc1839496be18a0cb1ef1bc74c07f391b4448de53d07bb63f695f1763b"
        ],
        [FnStorefrontTypes.EventShopCatalog] =
        [
            "v2:/222374fc7ea9f6ef8eb0b3c20f3a5d7f64f612e9f3435c74e3d51d785739bf9f",
            "v2:/570ff3bed6fc8a1f7006610dbb6ce9e4bcd244a32caa435a60392460da356c88",
            "v2:/6633ab8087f2a2e80bdf7a90d06351e7a03b82790cc2e286f4b6851020532ed4",
            "v2:/5c841be6c7cf1635cca83f2d4c345242c85192bf5beda2af0317e1cc745a3a38",
            "v2:/bfe19601a5107b1a6ba83ab25ac9fef02ae14b78ee451ab33c6b5218938183c4"
        ]
    };

    static JsonObject cachedCosmeticLayouts;
    static SemaphoreSlim cosmeticLayoutSemaphore = new(1);
    public static async Task<JsonObject> GetCosmeticLayouts(bool force = false)
    {
        using var st = await cosmeticLayoutSemaphore.AwaitToken();
        if (!st.wasImmediate && !force)
            return cachedCosmeticLayouts;
        var rawLayoutData = await Helpers.MakeRequest(
                HttpMethod.Get,
                FnWebAddresses.content,
                "content/api/pages/fortnite-game/mp-item-shop",
                "",
                null
            );
        if (rawLayoutData["errorMessage"] is not null)
            return cachedCosmeticLayouts;
        JsonObject layoutResult = [];
        await Task.Run(() =>
        {
            foreach (var section in rawLayoutData["shopData"]?["sections"]?.AsArray())
            {
                JsonObject sectionData = null;
                try
                {
                    sectionData = new()
                    {
                        ["displayName"] = section["displayName"].ToString(),
                        ["category"] = section["category"]?.ToString(),
                        ["background"] = section["metadata"]["background"]?.SafeDeepClone(),
                        ["rank"] = section["metadata"]["stackRanks"][0]["stackRankValue"].GetValue<int>(),
                        ["pages"] = new JsonObject()
                    };
                }
                catch
                {
                    GD.PushError("Error Parsing Layout: \n"+section);
                }
                foreach (var page in section["metadata"]["offerGroups"].AsArray())
                {
                    JsonObject pageData = new()
                    {
                        ["displayType"] = page["displayType"].ToString()
                    };
                    if (page["metadata"]["textureMetadata"]?.AsArray() is JsonArray textureMeta)
                        pageData["images"] = new JsonObject(textureMeta.Select(n => KeyValuePair.Create(n["key"].ToString(), (JsonNode)n["value"].ToString())));
                    if (page["metadata"]["textMetadata"]?.AsArray() is JsonArray textMeta)
                        pageData["text"] = new JsonObject(textMeta.Select(n => KeyValuePair.Create(n["key"].ToString(), (JsonNode)n["value"].ToString())));
                    sectionData["pages"][$"{section["sectionID"]}.{page["offerGroupId"]}"] = pageData;
                }
                layoutResult[$"{section["sectionID"]}"] = sectionData;
            }
        });
        return cachedCosmeticLayouts = layoutResult;
    }

    //static JsonObject ProcessShop(string shopId)
    //{
    //    var shopOffers = storefrontCache[shopId]?.AsArray().Reserialise();
    //    JsonArray highlights = new();
    //    for (int i = 0; i < shopOffers.Count; i++)
    //    {
    //        var item = shopOffers[i];
    //        if (!(defaultShops[shopId]?.Contains(item["offerId"].ToString()) ?? true))
    //        {
    //            highlights.Add(item.Reserialise());
    //            shopOffers.RemoveAt(i);
    //            i--;
    //        }
    //    }

    //    return new()
    //    {
    //        ["regular"] = shopOffers,
    //        ["highlights"] = highlights
    //    };
    //}

    static JsonObject ProcessCosmetics(
        JsonObject cosmeticDisplayData, 
        FrozenDictionary<string, string[]> bestsellingCosmetics, 
        FrozenDictionary<string, JamTrackData> jamTracks)
    {
        var shopOfferList = storefrontCache[FnStorefrontTypes.WeeklyCosmeticShopCatalog]?.AsArray().ToList();
        if(shopOfferList is null)
            return null;
        bestsellingCosmetics ??= FrozenDictionary<string, string[]>.Empty;
        //shopOfferList.AddRange(storefrontCache[FnStorefrontTypes.DailyCosmeticShopCatalog].AsArray());
        var shopOfferDict = shopOfferList.ToDictionary(n => n["offerId"].ToString());

        Parallel.ForEach(shopOfferDict, offer =>
        {
            bool needsFallback = false;
            lock (cosmeticDisplayData)
            {
                needsFallback = !cosmeticDisplayData.ContainsKey(offer.Key);
            }
            var globalBestSellers = bestsellingCosmetics["bestsellers_list"];
            bool isBestseller = globalBestSellers.Contains(offer.Key);
            if (isBestseller)
                GD.Print("BESTSELLER: " + offer.Value["devName"]?.ToString());
            var bestsellerRegions = bestsellingCosmetics
                .Where(kvp => kvp.Value.Contains(offer.Key) && kvp.Key != "bestsellers_list")
                .ToDictionary(kvp => kvp.Key.Split("_")[^1], kvp => (JsonNode)(10-Array.IndexOf(kvp.Value, offer.Key)));
            var regionalRank = (float)bestsellerRegions.Select(kvp => (int)kvp.Value * CosmeticShopInterface.GetRegionWeight(kvp.Key)).Sum();

            var webUrl = offer.Value["meta"]?["webURL"]?.ToString();

            //fix jam track URL (janky)
            if (offer.Value["meta"]?["templateId"]?.ToString() is string templateId)
            {
                if (templateId.StartsWith("SparksSong") && jamTracks.TryGetValue(templateId, out var trackData))
                {
                    webUrl = trackData.WebURL;
                }
            }

            //fix car part URL
            if (webUrl.Contains("/bundles/"))
            {
                if (webUrl.Contains("-wheel-"))
                    webUrl = webUrl.Replace("/bundles/", "/wheels/");
                else if (webUrl.Contains("-trail-"))
                    webUrl = webUrl.Replace("/bundles/", "/trails/");
                else if (webUrl.Contains("-boost-"))
                    webUrl = webUrl.Replace("/bundles/", "/boosts/");
            }

            if (needsFallback)
            {
                var fallbackDisplayData = new JsonObject()
                {
                    ["isFallback"] = true,
                    ["devName"] = offer.Value["devName"]?.ToString(),
                    ["fallbackType"] = offer.Value["meta"]?["templateId"]?.ToString().Split(":")[0],
                    ["offerId"] = offer.Value["offerId"]?.ToString(),
                    ["inDate"] = offer.Value["meta"]?["inDate"]?.ToString(),
                    ["outDate"] = offer.Value["meta"]?["outDate"]?.ToString(),
                    ["regularPrice"] = offer.Value["prices"]?.AsArray().FirstOrDefault()?["regularPrice"]?.GetValue<int>(),
                    ["finalPrice"] = offer.Value["prices"]?.AsArray().FirstOrDefault()?["finalPrice"]?.GetValue<int>(),
                    ["webURL"] = webUrl,
                    ["layoutId"] = offer.Value["meta"]?["LayoutId"]?.ToString(),
                    ["isBestseller"] = isBestseller,
                    ["layout"] = new JsonObject()
                    {
                        ["id"] = offer.Value["meta"]?["AnalyticOfferGroupId"]?.ToString(),
                    },
                    ["colors"] = new JsonObject()
                    {
                        ["color1"] = offer.Value["meta"]?["color1"]?.ToString(),
                        ["color2"] = offer.Value["meta"]?["color2"]?.ToString(),
                        ["color3"] = offer.Value["meta"]?["color3"]?.ToString(),
                        ["textBackgroundColor"] = offer.Value["meta"]?["textBackgroundColor"]?.ToString(),
                    },
                    ["tileSize"] = offer.Value["meta"]?["TileSize"]?.ToString(),
                    ["sortPriority"] = offer.Value["sortPriority"]?.GetValue<int>(),
                };

                if (isBestseller)
                    fallbackDisplayData["bestsellerRank"] = Array.IndexOf(globalBestSellers, offer.Key);
                if (bestsellerRegions.Count != 0)
                {
                    fallbackDisplayData["bestsellerRegions"] = new JsonObject(bestsellerRegions);
                    fallbackDisplayData["regionalRank"] = regionalRank;
                }

                if (cachedCosmeticLayouts[fallbackDisplayData["layout"]["id"]?.ToString()] is JsonObject fallbackLayoutData)
                {
                    fallbackDisplayData["layout"]["name"] = fallbackLayoutData["displayName"]?.ToString();
                    fallbackDisplayData["layout"]["category"] = fallbackLayoutData["category"]?.ToString();
                    fallbackDisplayData["layout"]["rank"] = fallbackLayoutData["rank"]?.ToString();
                }

                if (offer.Value["dynamicBundleInfo"] is JsonObject bundleInfo)
                {
                    fallbackDisplayData["dynamicBundleInfo"] = bundleInfo.SafeDeepClone();

                    int totalPrice = bundleInfo["bundleItems"].AsArray().Select(n => n["regularPrice"].GetValue<int>()).Sum();
                    fallbackDisplayData["regularPrice"] = totalPrice;
                    fallbackDisplayData["finalPrice"] = totalPrice + bundleInfo["discountedBasePrice"].GetValue<int>();

                    if (offer.Value["meta"]?["displayAssetPath"]?.ToString() is string nameDaPath && nameDaPath.Contains("/DisplayAssets/"))
                    {
                        fallbackDisplayData["bundleDisplayAsset"] = nameDaPath.Replace("/Game/Catalog/", "/OfferCatalog/");
                    }
                }

                if (offer.Value["meta"]?["NewDisplayAssetPath"]?.ToString() is string imgDaPath && imgDaPath.Contains("/NewDisplayAssets/"))
                {
                    fallbackDisplayData["fallbackDisplayAsset"] = imgDaPath.Replace("/Game/Catalog/", "/OfferCatalog/");
                }

                fallbackDisplayData["fallbackGrants"] = new JsonArray([.. 
                    offer.Value["itemGrants"]
                    .AsArray()
                    .Select(g => (JsonNode)g["templateId"].ToString())
                ]);

                lock (cosmeticDisplayData)
                {
                    cosmeticDisplayData[offer.Key] = fallbackDisplayData;
                }
                return;
            }

            JsonNode displayData = null;
            lock (cosmeticDisplayData)
            {
                displayData = cosmeticDisplayData[offer.Key];
            }

            //additions
            displayData["inDate"] = offer.Value["meta"]?["inDate"]?.ToString() ?? null;
            displayData["outDate"] = offer.Value["meta"]?["outDate"]?.ToString() ?? null;
            displayData["isBestseller"] = isBestseller;
            displayData["webURL"] = webUrl;

            if (isBestseller)
                displayData["bestsellerRank"] = Array.IndexOf(globalBestSellers, offer.Key);
            if (bestsellerRegions.Count != 0)
            {
                displayData["bestsellerRegions"] = new JsonObject(bestsellerRegions);
                displayData["regionalRank"] = regionalRank;
            }

            if (offer.Value["dynamicBundleInfo"] is JsonObject dynBundleInfo)
                displayData["dynamicBundleInfo"] = dynBundleInfo.SafeDeepClone();

            //sometimes these are just missing
            displayData["layoutId"] ??= offer.Value["meta"]?["LayoutId"].ToString() ?? "?";
            displayData["layout"] ??= new JsonObject();
            displayData["layout"]["id"] ??= offer.Value["meta"]?["AnalyticOfferGroupId"].ToString();
            if (cachedCosmeticLayouts[displayData["layout"]["id"]?.ToString()] is JsonObject layoutData)
            {
                displayData["layout"]["name"] = layoutData["displayName"]?.ToString();
                displayData["layout"]["category"] = layoutData["category"]?.ToString();
                displayData["layout"]["rank"] = layoutData["rank"]?.ToString();
            }

            if ((offer.Value["prices"]?.AsArray().Count ?? 0) > 0)
                displayData["prices"] = offer.Value["prices"].SafeDeepClone();

            if (!(displayData["layout"]["category"]?.ToString() is string cat && !string.IsNullOrWhiteSpace(cat)))
                displayData["layout"]["category"] = "Uncategorised";

            //jam tracks are funky, gotta reformat them
            if (displayData["tracks"] is JsonArray trackList)
            {
                foreach (var item in trackList)
                {
                    var trackObj = item.AsObject();
                    trackObj["name"] = trackObj["title"].ToString();
                    trackObj["type"] = new JsonObject()
                    {
                        ["value"] = "track",
                        ["displayValue"] = "Jam Track",
                        ["backendValue"] = "SparksSong",
                    };
                    trackObj["rarity"] = new JsonObject()
                    {
                        ["value"] = "rare",
                        ["displayValue"] = "Rare",
                        ["backendValue"] = "EFortRarity::Rare",
                    };
                    trackObj["images"] = new JsonObject()
                    {
                        ["icon"] = trackObj["albumArt"].ToString(),
                    };
                    trackObj["description"] =
                        (trackObj["artist"] is JsonValue artist ? $"Artist: \"{artist}\"\n" : "") +
                        (trackObj["album"] is JsonValue album ? $"Album: \"{album}\"\n" : "") +
                        (trackObj["releaseYear"] is JsonValue releaseYear ? $"Released in: {releaseYear}\n" : "") +
                        (trackObj["duration"] is JsonValue duration ? $"Duration: {duration.GetValue<int>().FormatTimeSeconds()}\n" : "");
                }
            }

            cosmeticDisplayData[offer.Key] = displayData;
        });

        var partiallyOrganisedCosmetics = cosmeticDisplayData
            .Select(n => KeyValuePair.Create(n.Key, n.Value.SafeDeepClone()))
            .OrderBy(n => -n.Value["sortPriority"]?.GetValue<int>() ?? 0)// sort by offer index (descending)
            .GroupBy(n => n.Value["layoutId"]?.ToString() ?? "Unknown")// group into pages
            .OrderBy(p => PagePriorityFromLayoutID(p.Key))// sort by page index (descending)
            .GroupBy(p => p.First().Value["layout"]?["name"]?.ToString() ?? "Unknown")// group by page header
            .OrderBy(g => g.First().First().Value["layout"]?["index"]?.GetValue<int>())// sort by page header index
            .GroupBy(g => g.First().First().Value["layout"]?["category"]?.ToString() ?? "Uncategorised");// group by page category

        //partiallyOrganisedCosmetics.Select(g =>
        //{
        //    GD.Print(g.Key+":"+(g.First().Value["layout"]?["category"]?.ToString() ?? "missing"));
        //    return g;
        //}).ToArray();

        var organisedCosmetics = partiallyOrganisedCosmetics
            .Select(c =>
                KeyValuePair.Create<string, JsonNode>(c.Key, new JsonObject(c.Select(g =>
                        KeyValuePair.Create<string, JsonNode>(g.Key, new JsonArray(g.Select(p=>new JsonObject(p)).ToArray()))
                    )))
                );
        JsonObject organisedCosmeticsJson = new(organisedCosmetics);

        if (organisedCosmeticsJson.ContainsKey("Uncategorised"))
        {
            //moves uncategorised sections to the end of the list
            var uncategorised = organisedCosmeticsJson["Uncategorised"].AsObject();
            organisedCosmeticsJson.Remove("Uncategorised");
            organisedCosmeticsJson["Uncategorised"] = uncategorised;
        }

        return organisedCosmeticsJson;
    }

    static int PagePriorityFromLayoutID(string layoutID)
    {
        if (int.TryParse(layoutID.Split(".")[^1], out int parseResult))
            return -parseResult;
        else if (int.TryParse(layoutID[^2..], out int fallbackParseResult))
        {
            GD.Print("Layout Priority fallback: " + fallbackParseResult);
            return -fallbackParseResult;
        }
        return -100;
    }

    public static bool StorefrontRequiresUpdate()
    {
        if (storefrontCache is null)
            return true;
        var expirationTime = DateTime.Parse(storefrontCache["expiration"].ToString(), null, DateTimeStyles.RoundtripKind);
        return DateTime.UtcNow.CompareTo(expirationTime) >= 0;
    }

    static Task<JsonObject> activeStorefrontRequest = null;
    static async Task EnsureStorefront(bool forceRefresh)
    {
        if (activeStorefrontRequest is not null && activeStorefrontRequest.IsCompleted)
            activeStorefrontRequest = null;

        if (forceRefresh)
        {
            GD.Print("forcing refresh");
            storefrontCache = null;
        }

        if (storefrontCache is not null)
        {
            var refreshTime = DateTime.Parse(storefrontCache["expiration"].ToString(), null, DateTimeStyles.RoundtripKind);
            if (DateTime.UtcNow.CompareTo(refreshTime) >= 0)
            {
                storefrontCache = null;
            }
        }

        if (storefrontCache is null)
        {
            GD.Print("requesting storefront");
            activeStorefrontRequest ??= RequestStorefront();
            await Task.WhenAny(activeStorefrontRequest);
        }
    }

    static async Task<JsonObject> RequestStorefront()
    {
        var account = GameAccount.activeAccount;
        if (!await account.Authenticate())
            return null;

        GD.Print("retrieving catalog from epic...");
        JsonNode fullStorefront = await Helpers.MakeRequest(
                HttpMethod.Get,
                FnWebAddresses.game,
                "fortnite/api/storefront/v2/catalog",
                "",
                account.AuthHeader
            );
        if (fullStorefront["errorCode"] is not null)
        {
            await GenericConfirmationWindow.ShowErrorForWebResult(fullStorefront.AsObject());
            return null;
        }
        storefrontCache = SimplifyStorefront(fullStorefront);
        //save to file
        //using FileAccess storefrontFile = FileAccess.Open(storefrontCacheSavePath, FileAccess.ModeFlags.Write);
        //storefrontFile.StoreString(storefrontCache.ToString());
        //storefrontFile.Flush();
        return storefrontCache;
    }

    public static async Task<FrozenDictionary<string, string[]>> RequestCosmeticBestsellingData()
    {
        GD.Print("retrieving cosmetic bestsellers from epic...");
        var res = await FnWebAddresses.epicCDN
            .MakeRequest("/fn_bsdata/ebb74910-dd35-44b8-b826-d58dc16c6456.json")
            .Send();
        if (!res.IsSuccessStatusCode)
            return null;
        var responseDict = await res.Content.ReadFromJsonAsync<Dictionary<string, BestsellerData>>(Helpers.JsonOptions.CamelCase);
        return responseDict.ToFrozenDictionary(kvp => kvp.Key, kvp => kvp.Value.offerList ?? []);
    }
    record struct BestsellerData
    {
        [JsonPropertyName("expiry_date")]
        public DateTime expiryDate;
        [JsonPropertyName("offer_list")]
        public string[] offerList;
    }

    public static async Task<FrozenDictionary<string, JamTrackData>> RequestJamtrackData()
    {
        GD.Print("retrieving jam tracks from epic...");
        var res = await FnWebAddresses.content
            .MakeRequest("/content/api/pages/fortnite-game/spark-tracks")
            .Send();
        if (!res.IsSuccessStatusCode)
            return null;
        var responseObj = await res.Content.ReadFromJsonAsync<JsonObject>(Helpers.JsonOptions.CamelCase);
        responseObj.Remove("_title");
        responseObj.Remove("_noIndex");
        responseObj.Remove("_activeDate");
        responseObj.Remove("lastModified");
        responseObj.Remove("_locale");
        responseObj.Remove("_templateName");
        responseObj.Remove("_suggestedPrefetch");
        var responseTracks = responseObj.Deserialize<Dictionary<string, JamTrackData.JamTrackContainer>>(Helpers.JsonOptions.Fields).Select(kvp=>kvp.Value.track);
        return responseTracks.ToFrozenDictionary(track=>track.songTemplateId);
    }

    public record struct JamTrackData
    {
        [JsonPropertyName("tt")]
        public string title;
        [JsonPropertyName("au")]
        public string albumArtUrl;
        [JsonPropertyName("su")]
        public string songUuid;
        [JsonPropertyName("an")]
        public string author;
        [JsonPropertyName("sn")]
        public string songId;
        [JsonPropertyName("ti")]
        public string songTemplateId;

        public string WebURL => $"/item-shop/jam-tracks/{title.ToLower().Replace(' ', '-')}-{songUuid.Split(' ')[^1]}";

        public record struct JamTrackContainer
        {
            public JamTrackData track;
        }
    }

    public static async Task<JsonObject> RequestCosmeticDisplayData()
    {
        GD.Print("retrieving cosmetic visuals from fortnite-api...");
        JsonNode cosmeticDisplayData = await Helpers.MakeRequest(
                HttpMethod.Get,
                WebClients.fnApi,
                "v2/shop?responseFlags=4", // 1 = paths, 2 = gameplayTags, 4 = shop history
                "",
                null,
                addCosmeticHeader:true
            );
        if (cosmeticDisplayData["data"]?["entries"]?.AsArray().Count == 0)
            GD.Print(cosmeticDisplayData.ToString());
        return new(cosmeticDisplayData["data"]["entries"].AsArray()
            .Select(n => n.AsObject().CreateKVP("offerId")));
    }

    public static readonly string[] relevantStorefronts = new string[]
    {
        FnStorefrontTypes.XRayLlamaCatalog,
        FnStorefrontTypes.RandomLlamaCatalog,
        FnStorefrontTypes.WeeklyShopCatalog,
        FnStorefrontTypes.EventShopCatalog,
        FnStorefrontTypes.WeeklyCosmeticShopCatalog,
        FnStorefrontTypes.DailyCosmeticShopCatalog
    };

    static JsonObject SimplifyStorefront(JsonNode fullStorefront)
    {
        var filteredStorefronts = fullStorefront["storefronts"].AsArray().Where(val => relevantStorefronts.Contains(val["name"].ToString()));
        JsonObject jsonFilteredStorefronts = new()
        {
            ["expiration"] = fullStorefront["expiration"].ToString()
        };
        foreach (var item in filteredStorefronts)
        {
            jsonFilteredStorefronts.Add(item["name"].ToString(), item["catalogEntries"].SafeDeepClone());
        }
        return jsonFilteredStorefronts;
    }

    const string fnapiLinkPrefix = "https://fortnite-api.com/images/cosmetics/";
    const string fnapiJamTrackPrefix = "https://cdn.fortnite-api.com/tracks/";
    const string dillyPrefix = "https://export-service.dillyapis.com/v1/export?path=";
    const string imageCacheFolderPath = "user://cosmetic_images/";
    const string metaCacheFolderPath = "user://cosmetic_meta/";
    static readonly Dictionary<string, WeakRef> activeResourceCache = [];
    static readonly Dictionary<string, JsonObject> activeMetaCache = [];

    public static string LocalCosmeticResourcePath(string serverPath)
    {
        bool isJamTrack = serverPath.StartsWith(fnapiJamTrackPrefix);
        bool isFNCentral = serverPath.StartsWith(dillyPrefix);
        string localPath = imageCacheFolderPath;
        if (isFNCentral)
        {
            localPath += "/" + serverPath.Split('/')[^1] + ".png";
        }
        else if (isJamTrack)
        {
            localPath += serverPath[fnapiJamTrackPrefix.Length..].Replace("/", "-");
        }
        else
        {
            localPath += serverPath[fnapiLinkPrefix.Length..].Replace("/", "-");
        }
        return localPath;
    }

    public static ImageTexture GetLocalCosmeticResource(string serverPath)
    {
        lock (activeResourceCache)
        {
            if (activeResourceCache.TryGetImage(serverPath, out var cachedTexture))
                return cachedTexture;
        }
        
        string localPath = LocalCosmeticResourcePath(serverPath);

        if (!FileAccess.FileExists(localPath))
            return null;

        //GD.Print("file exists");
        Image resourceImage = new();
        using var imageFile = FileAccess.Open(localPath, FileAccess.ModeFlags.ReadWrite);
        var error = LoadImageWithCtx(resourceImage, imageFile.GetBuffer((long)imageFile.GetLength()), localPath);
        if (error != Error.Ok)
            return null;
        //GD.Print("file loaded");

        //make a fake modification to change the modified date when the file is disposed
        imageFile.SeekEnd(-1);
        byte temp = imageFile.Get8();
        imageFile.SeekEnd(-1);
        imageFile.Store8(temp);

        var imageSize = resourceImage.GetSize();
        var clampedSize = imageSize;
        if (clampedSize.X > 256)
            clampedSize = (Vector2I)((Vector2)clampedSize * (256.0f / clampedSize.X));
        if (clampedSize.Y > 256)
            clampedSize = (Vector2I)((Vector2)clampedSize * (256.0f / clampedSize.Y));
        if (imageSize.X != clampedSize.X || imageSize.Y != clampedSize.Y)
            resourceImage.Resize(clampedSize.X, clampedSize.Y);

        var imageTex = ImageTexture.CreateFromImage(resourceImage);
        //imageTex.ResourceName = serverPath;
        imageTex.ResourcePath = serverPath;
        lock (activeResourceCache)
        {
            activeResourceCache[serverPath] = GodotObject.WeakRef(imageTex);
        }

        return imageTex;
    }

    public static async Task<ImageTexture> GetCosmeticResource(string serverPath, bool printSuccess = false)
    {
        if (GetLocalCosmeticResource(serverPath) is ImageTexture localImageTex)
        {
            if (localImageTex is not null)
                return localImageTex;
            else
                GD.Print("NULL LOCAL TEXTURE");
        }

        bool isJamTrack = serverPath.StartsWith(fnapiJamTrackPrefix);
        bool isFNCentral = serverPath.StartsWith(dillyPrefix);
        //if (isJamTrack)
        //{
        //    GD.Print("Interpreting as Jam Track");
        //    GD.Print("/tracks/" + serverPath[fnapiJamTrackPrefix.Length..]);
        //    GD.Print(ExternalEndpoints.jamTracksEndpoint);
        //}
        string localPath = imageCacheFolderPath;
        string halfPath = "";
        var client = WebClients.fnApi;
        if (isFNCentral)
        {
            client = WebClients.dillyApi;
            localPath += "/" + serverPath.Split('/')[^1] + ".png";
            halfPath = "/v1/export?path=" + serverPath[dillyPrefix.Length..];
        }
        else if (isJamTrack)
        {
            client = WebClients.fnApiJamTrakcs;
            localPath += serverPath[fnapiJamTrackPrefix.Length..].Replace("/", "-");
            halfPath = "/tracks/" + serverPath[fnapiJamTrackPrefix.Length..];
        }
        else
        {
            localPath += serverPath[fnapiLinkPrefix.Length..].Replace("/", "-");
            halfPath = "/images/cosmetics/" + serverPath[fnapiLinkPrefix.Length..];
        }

        GD.Print($"Requesting cosmetic ({client.BaseAddress}, {halfPath}, {localPath}, {serverPath})");
        using var result = await Helpers.MakeRequestRaw(client, new(HttpMethod.Get, halfPath));
        if (!result.IsSuccessStatusCode)
        {
            GD.Print("Err: "+result);
            return null;
        }
        if (printSuccess)
            GD.Print("remote file exists");

        Image resourceImage = new();
        byte[] imageBuffer = await result.Content.ReadAsByteArrayAsync();
        var error = LoadImageWithCtx(resourceImage, imageBuffer, localPath);
        if (error != Error.Ok)
            return null;
        if (printSuccess)
            GD.Print("remote file loaded");

        if (!DirAccess.DirExistsAbsolute(imageCacheFolderPath))
            DirAccess.MakeDirAbsolute(imageCacheFolderPath);

        using (var imageFile = FileAccess.Open(localPath, FileAccess.ModeFlags.Write))
        {
            imageFile.StoreBuffer(imageBuffer);
        }

        var imageSize = resourceImage.GetSize();
        var clampedSize = imageSize;
        if (clampedSize.X > 256)
            clampedSize = (Vector2I)((Vector2)clampedSize * (256 / clampedSize.X));
        if (clampedSize.Y > 256)
            clampedSize = (Vector2I)((Vector2)clampedSize * (256 / clampedSize.Y));
        if (imageSize.X != clampedSize.X || imageSize.Y != clampedSize.Y)
            resourceImage.Resize(Mathf.Max(clampedSize.X, 1), Mathf.Max(clampedSize.Y, 1));

        var imageTex = ImageTexture.CreateFromImage(resourceImage);
        //imageTex.ResourceName = serverPath;
        imageTex.ResourcePath = serverPath;

        lock (activeResourceCache)
        {
            activeResourceCache[serverPath] = GodotObject.WeakRef(imageTex);
        }

        return imageTex;
    }

    public static JsonObject GetLocalCosmeticMeta(string pathOrTemplateID)
    {
        if(pathOrTemplateID is null)
            return null;
        lock (activeMetaCache)
        {
            if (activeMetaCache.TryGetValue(pathOrTemplateID, out var cachedMeta))
                return cachedMeta;
        }
        var localIdentifier = pathOrTemplateID.Split(".")[^1];
        string localPath = $"{metaCacheFolderPath}/{localIdentifier}.json";
        if (!FileAccess.FileExists(localPath))
            return null;
        using var metaFile = FileAccess.Open(localPath, FileAccess.ModeFlags.ReadWrite);

        var localMeta = JsonNode.Parse(metaFile.GetAsText()).AsObject();

        //make a fake modification to change the modified date when the file is disposed
        metaFile.SeekEnd(-1);
        byte temp = metaFile.Get8();
        metaFile.SeekEnd(-1);
        metaFile.Store8(temp);

        lock (activeMetaCache)
        {
            activeMetaCache.TryAdd(pathOrTemplateID, localMeta);
        }

        return localMeta;
    }

    public static async Task<JsonObject> GetCosmeticMeta(string pathOrTemplateID)
    {
        if(pathOrTemplateID is null)
            return null;
        if (GetLocalCosmeticMeta(pathOrTemplateID) is JsonObject localMeta)
            return localMeta;

        var localIdentifier = pathOrTemplateID.Split('.')[^1];
        string localPath = $"{metaCacheFolderPath}/{localIdentifier}.json";

        JsonObject metaObject = null;
        if (pathOrTemplateID.Contains('.'))
        {
            //treat as path (probably display asset)
            GD.Print("Meta: "+pathOrTemplateID.Split('.')[0]);
            var res = await WebHelpers
                .MakeRequest(WebClients.dillyApi, $"v1/export?path={pathOrTemplateID.Split('.')[0]}")
                .Send();
            var resultObject = await res.Content.ReadFromJsonAsync<JsonNode>();
            GD.Print("MetaRes: "+resultObject);
            if (resultObject is not null && resultObject?["result"]?.ToString()?.StartsWith("Too many requests") != true && resultObject["errored"]?.GetValue<bool>() != true)
            {
                metaObject = resultObject["jsonOutput"]?[0]?["Properties"]?.AsObject()?.SafeDeepClone();
            }
            GD.Print("MetaObj: "+metaObject);
        }
        else if (pathOrTemplateID.Contains(':'))
        {
            string[] remotePaths = CosmeticTemplateToPaths(pathOrTemplateID);
            foreach (var remotePath in remotePaths)
            {
                JsonNode resultObject = await Helpers.MakeRequest(
                    HttpMethod.Get,
                    WebClients.dillyApi,
                    $"v1/export?path={remotePath}",
                    "",
                    null
                );
                if (resultObject is null)
                    continue;
                if (resultObject?["result"]?.ToString()?.StartsWith("Too many requests") ?? false)
                    continue;
                if (resultObject?["errored"]?.GetValue<bool>() == true)
                    continue;

                var splitTemplateId = pathOrTemplateID.Split(":");
                var resultObjects = resultObject["jsonOutput"].AsArray();
                var cosmetic = resultObjects.FirstOrDefault(n => n["Type"]?.ToString() == $"{splitTemplateId[0]}ItemDefinition")?["Properties"]?.AsObject();
                if (cosmetic is null)
                    continue;

                metaObject = new()
                {
                    ["id"] = splitTemplateId[1],
                    ["name"] = cosmetic["ItemName"]?["sourceString"].ToString(),
                    ["description"] = cosmetic["ItemDescription"]?["sourceString"].ToString(),
                    ["type"] = new JsonObject()
                    {
                        ["backendValue"] = splitTemplateId[0],
                        ["displayValue"] = cosmetic["ItemShortDescription"]?["sourceString"].ToString(),
                    }
                };
                var dataList = cosmetic["DataList"].AsArray();
                if (dataList.FirstOrDefault(n => n["LargeIcon"] is not null)?["LargeIcon"]?["AssetPathName"]?.ToString() is string largeImagePath)
                {
                    metaObject["images"] ??= new JsonObject();
                    metaObject["images"]["icon"] = dillyPrefix + largeImagePath.Split('.')[0];
                }
                if (dataList.FirstOrDefault(n => n["Icon"] is not null)?["Icon"]?["AssetPathName"]?.ToString() is string smallImagePath)
                {
                    metaObject["images"] ??= new JsonObject();
                    metaObject["images"]["smallIcon"] = dillyPrefix + smallImagePath.Split('.')[0];
                }
            }
        }
        else
        {
            GD.Print("Unknown Meta: " + pathOrTemplateID);
            return null;
        }

        if (metaObject is null)
            return null;

        if (!DirAccess.DirExistsAbsolute(metaCacheFolderPath))
            DirAccess.MakeDirAbsolute(metaCacheFolderPath);

        using (var metaFile = FileAccess.Open(localPath, FileAccess.ModeFlags.Write))
        {
            metaFile.StoreString(metaObject.ToString());
        }

        lock (activeMetaCache)
        {
            activeMetaCache.TryAdd(pathOrTemplateID, metaObject);
        }

        return metaObject;
    }

    static string[] CosmeticTemplateToPaths(string templateId)
    {
        var splitTemplateId = templateId.Split(":");
        if (splitTemplateId.Length <= 1)
        {
            GD.Print("Can't split: " + templateId);
            return Array.Empty<string>();
        }
        return splitTemplateId[0] switch
        {
            "AthenaCharacter" => new string[] { $"BRCosmetics/Athena/Items/Cosmetics/Characters/{splitTemplateId[1]}.uasset" },
            "AthenaBackpack" => new string[] { $"BRCosmetics/Athena/Items/Cosmetics/Backpacks/{splitTemplateId[1]}.uasset" },
            "AthenaPickaxe" => new string[] { $"BRCosmetics/Athena/Items/Cosmetics/Pickaxes/{splitTemplateId[1]}.uasset" },
            "AthenaGlider" => new string[] { $"BRCosmetics/Athena/Items/Cosmetics/Gliders/{splitTemplateId[1]}.uasset" },
            "AthenaSkyDiveContrail" => new string[] { $"BRCosmetics/Athena/Items/Cosmetics/Contrails/{splitTemplateId[1]}.uasset" },
            "AthenaDance" => new string[] { $"BRCosmetics/Athena/Items/Cosmetics/Dances/{splitTemplateId[1]}.uasset" },
            "AthenaItemWrap" => new string[] { $"BRCosmetics/Athena/Items/Cosmetics/ItemWraps/{splitTemplateId[1]}.uasset" },

            //TODO: car parts, instruments, etc
            _ => Array.Empty<string>(),
        };
    }

    static Error LoadImageWithCtx(Image image, byte[] data, string path)
    {
        var urlEnding = path.Split(".")[^1].ToLower();
        switch (urlEnding)
        {
            case "png":
                return image.LoadPngFromBuffer(data);
            case "webp":
                return image.LoadWebpFromBuffer(data);
            case "jpg":
                return image.LoadJpgFromBuffer(data);
            default:
                return Error.Failed;
        }
    }

    public static void CleanCosmeticResourceCache()
    {
        if (!DirAccess.DirExistsAbsolute(imageCacheFolderPath))
            return;

        var cacheFolder = DirAccess.Open(imageCacheFolderPath);
        DateTime invalidDateTime = DateTime.Now.AddDays(-2); //images are removed if they havent been used in more than 2 days
        var invalidCacheFilePaths = cacheFolder.GetFiles()
            .Where(p => p.EndsWith(".png") || p.EndsWith(".webp"))
            .Select(p => imageCacheFolderPath + "/" + p)
            .Where(p => DateTime.Parse(Time.GetDatetimeStringFromUnixTime((long)FileAccess.GetModifiedTime(p))).CompareTo(invalidDateTime) < 0);

        foreach (var filePath in invalidCacheFilePaths)
        {
            GD.Print($"Cleaning cosmetic \"{filePath}\" ({DateTime.Parse(Time.GetDatetimeStringFromUnixTime((long)FileAccess.GetModifiedTime(filePath)))})");
            DirAccess.RemoveAbsolute(filePath);
        }
    }

}

public static class FnStorefrontTypes
{
    public const string XRayLlamaCatalog = "CardPackStorePreroll";
    public const string RandomLlamaCatalog = "CardPackStoreGameplay";
    public const string WeeklyShopCatalog = "STWRotationalEventStorefront";
    public const string EventShopCatalog = "STWSpecialEventStorefront";
    public const string WeeklyCosmeticShopCatalog = "BRWeeklyStorefront";
    public const string DailyCosmeticShopCatalog = "BRDailyStorefront";
}

public class GameStorefront
{
    #region Static Methods

    static Dictionary<RefreshTimeType, DateTime> expirationDates = new()
    {
        [RefreshTimeType.Hourly] = default,
        [RefreshTimeType.Daily] = default,
        [RefreshTimeType.Weekly] = default,
        [RefreshTimeType.Event] = default,
    };

    static Dictionary<string, JsonObject> storefrontCache;
    static Dictionary<string, GameStorefront> storefronts = [];
    public static bool RequiresUpdate(RefreshTimeType? refreshType)
    {
        return storefronts is null || refreshType is null || DateTime.UtcNow.CompareTo(expirationDates[refreshType.Value]) >= 0;
    }

    public static async Task<bool> UpdateCatalog(RefreshTimeType? refreshType = null)
    {
        if (!RequiresUpdate(refreshType))
            return true;

        var account = GameAccount.activeAccount;
        if (!await account.Authenticate())
            return false;

        GD.Print("retrieving catalog from epic...");
        var catalog = await Helpers.MakeRequest(
                HttpMethod.Get,
                FnWebAddresses.game,
                "fortnite/api/storefront/v2/catalog",
                "",
                account.AuthHeader
            );
        if (catalog["errorCode"] is not null)
        {
            await GenericConfirmationWindow.ShowErrorForWebResult(catalog.AsObject());
            return false;
        }
        storefrontCache = catalog["storefronts"].AsArray().Select(n => n.AsObject()).ToDictionary(n => n["name"].ToString());

        List<string> toRemove = [];
        foreach (var kvp in storefronts)
        {
            if(!storefrontCache.ContainsKey(kvp.Key))
            {
                toRemove.Add(kvp.Key);
                continue;
            }
            kvp.Value.CheckForChanges(storefrontCache[kvp.Key]["catalogEntries"].AsArray());
        }
        foreach (var sfKey in toRemove)
        {
            GD.Print("disconnecting SF");
            storefronts[sfKey].DisconnectAll();
            storefronts.Remove(sfKey);
        }

        foreach (var refreshTypeKey in expirationDates.Keys)
        {
            expirationDates[refreshTypeKey] = RefreshTimerController.GetRefreshTime(refreshTypeKey);
        }

        return true;
    }

    static GameStorefront GetOrCreateStorefront(string storefrontKey, RefreshTimeType? refreshType = null)
    {
        if (storefronts.ContainsKey(storefrontKey))
            return storefronts[storefrontKey];

        if (!storefrontCache.ContainsKey(storefrontKey))
            return null;

        return storefronts[storefrontKey] = new(storefrontCache[storefrontKey], refreshType);
    }

    public static async Task<GameStorefront> GetStorefront(string storefrontKey, RefreshTimeType? refreshType = null)
    {
        if (!await UpdateCatalog(refreshType))
            return null;
        return GetOrCreateStorefront(storefrontKey, refreshType);
    }

    public static async Task<GameStorefront[]> GetStorefronts(params string[] storefrontKeys) => await GetStorefronts(RefreshTimeType.Hourly, storefrontKeys);
    public static async Task<GameStorefront[]> GetStorefronts(RefreshTimeType? refreshType, params string[] storefrontKeys)
    {
        if(!await UpdateCatalog(refreshType))
            return Array.Empty<GameStorefront>();
        return storefrontKeys.Select(sfKey => GetOrCreateStorefront(sfKey)).Where(sf => sf is not null).ToArray();
    }

    public static GameOffer GetExistingOffer(string offerId)
    {
        return storefronts.Values
            .Select(s => s.offers.TryGetValue(offerId, out var offer) ? offer : null)
            .FirstOrDefault(o => o is not null);
    }

    #endregion

    public event Action<GameOffer> OnOfferAdded;
    public event Action<GameOffer> OnOfferChanged;
    public event Action<GameOffer> OnOfferRemoved;

    RefreshTimeType linkedRefreshType;
    public bool isValid { get; private set; } = true;
    public string storefrontId { get; private set; }
    Dictionary<string, GameOffer> offers;

    public GameStorefront(JsonObject rawData, RefreshTimeType? linkedRefreshType = null)
    {
        storefrontId = rawData["name"].ToString();
        offers = rawData["catalogEntries"].AsArray().Select(n => new GameOffer(this, n.AsObject())).ToDictionary(offer => offer.OfferId);
        this.linkedRefreshType = linkedRefreshType ?? RefreshTimeType.Hourly;
    }

    public async Task Update(bool force = false) => await UpdateCatalog(force ? null : linkedRefreshType);

    void CheckForChanges(JsonArray catalogEntries)
    {
        var catalogEntriesDict = catalogEntries.Select(n => n.AsObject()).ToDictionary(n => n["offerId"].ToString());
        var oldOfferIds = offers.Keys.ToArray();
        var newOfferIds = catalogEntries.Select(n => n["offerId"].ToString()).ToArray();

        var addedOffers = newOfferIds.Except(oldOfferIds);
        var removedOffers = oldOfferIds.Except(newOfferIds);
        var possiblyChangedOffers = oldOfferIds.Intersect(newOfferIds);

        foreach (var offerId in removedOffers)
        {
            var offer = offers[offerId];
            offer.NotifyRemoving();
            offers.Remove(offerId);
            OnOfferRemoved?.Invoke(offer);
            offer.DisconnectFromStorefront();
        }
        foreach (var offerId in possiblyChangedOffers)
        {
            var offer = offers[offerId];
            var from = offer.rawData.ToString();
            var to = catalogEntriesDict[offerId].ToString();
            if (from != to)
            {
                offer.SetRawData(catalogEntriesDict[offerId]);
                offer.NotifyChanged();
                OnOfferChanged?.Invoke(offer);
            }
        }
        foreach (var offerId in addedOffers)
        {
            GameOffer offer = new(this, catalogEntriesDict[offerId]);
            offers[offerId] = offer;
            OnOfferAdded?.Invoke(offer);
        }
    }

    public void DisconnectAll()
    {
        isValid = false;

        foreach (var offer in offers?.Values)
        {
            offer.DisconnectFromStorefront();
        }

        offers.Clear();
    }

    public GameOffer this[string offerId] => offers[offerId];
    public GameOffer[] Offers => offers.Values.ToArray();

}

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

    public string CosmeticDisplayAssetPath => rawData["displayAssetPath"]?.ToString();
    public string CosmeticNewDisplayAssetPath => GetMeta("NewDisplayAssetPath");
    public string CosmeticLayoutId => GetMeta("LayoutId");
    public int SortPriority => rawData["sortPriority"]?.GetValue<int>() ?? 0;

    Dictionary<string, int> GenerateRequirementList(string type) =>
        rawData["requirements"].AsArray()
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
    GameItem personalPrice;

    public GameItem[] itemGrants { get; private set; }

    public GameOffer(GameStorefront storefront, JsonObject rawData)
    {
        this.storefront = storefront;
        SetRawData(rawData);
    }

    public void SetRawData(JsonObject rawData)
    {
        this.rawData = rawData;
        itemGrants = rawData["itemGrants"].AsArray().Select(n => new GameItem(null, null, n.AsObject())).ToArray();
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
                        .Select(n => new KeyValuePair<string, int>(n["item"]["templateId"].ToString(), n["alreadyOwnedPriceReduction"].GetValue<int>()))
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
        personalPrice = null;
    }


    public CosmeticRequests.FNAPIOffer fnapiOfferData;
    public CosmeticRequests.DillyDisplayAsset dillyDisplayAssetData;
    public CosmeticRequests.DillyCosmetic dillyCosmeticData;

    ImageTexture cosmeticImage;
    public ImageTexture CosmeticImage => cosmeticImage ??=
        fnapiOfferData?.GetLocalOfferImage() ??
        dillyDisplayAssetData?.GetLocalOfferImage();

    public string CosmeticName
    {
        get
        {
            if (fnapiOfferData is not null)
                return fnapiOfferData.DisplayName;
            if (dillyCosmeticData is not null)
                return dillyCosmeticData.name;
            return "<Unknown>";
        }
    }

    public string CosmeticType
    {
        get
        {
            if (fnapiOfferData is not null)
                return fnapiOfferData.DisplayType;
            if (dillyCosmeticData is not null)
                return dillyCosmeticData.type.name;
            return "<Unknown>";
        }
    }

    CosmeticRequests.CosmeticMeta? cosmeticMetaData;
    public CosmeticRequests.CosmeticMeta CosmeticMetaData
    {
        get
        {
            if (cosmeticMetaData is not null)
                return (cosmeticMetaData).Value;
            if (fnapiOfferData is not null)
                return (cosmeticMetaData = fnapiOfferData.GenerateCosmeticMeta()).Value;
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

    public async Task LoadCosmeticData()
    {
        if (fnapiOfferData is not null)
            cosmeticImage = await fnapiOfferData.LoadOfferImage();
        if (dillyDisplayAssetData is not null)
            cosmeticImage = await dillyDisplayAssetData.LoadOfferImage();
    }

    GameItem GetRegularPrice()
    {
        int price = basePrice?.quantity ?? 0;
        price -= discountAmount;
        price = Mathf.Max(price, discountMin);
        var newPriceItem = basePrice?.template?.CreateInstance(price);

        return newPriceItem;
    }

    public async Task<int> GetPriceInInventory()
    {
        var account = GameAccount.activeAccount;
        if (!await account.Authenticate())
            return 0;
        var accountItems = await account.GetProfile(FnProfileTypes.AccountItems).Query();
        return accountItems.GetFirstTemplateItem(basePrice?.templateId)?.quantity ?? 0;
    }

    public async Task<GameItem> GetPriceInventoryItem()
    {
        var account = GameAccount.activeAccount;
        if (!await account.Authenticate())
            return null;
        var accountItems = await account.GetProfile(FnProfileTypes.AccountItems).Query();
        return accountItems.GetFirstTemplateItem(basePrice?.templateId);
    }

    public async Task<GameItem> GetPersonalPrice(bool forcePrice = false, bool forceCosmetics = false)
    {
        if (!forcePrice && personalPrice is not null)
            return personalPrice;

        int price = basePrice?.quantity ?? 0;
        price -= discountAmount;

        //if dynamic bundle, generate discount based on owned items
        var account = GameAccount.activeAccount;
        if (IsDiscountBundle && await account.Authenticate())
        {
            var cosmeticItems = await account.GetProfile(FnProfileTypes.CosmeticInventory).Query(forceCosmetics);
            foreach (var kvp in conditionalDiscounts)
            {
                if (cosmeticItems.GetTemplateItems(kvp.Key).Any())
                    price -= kvp.Value;
            }
        }

        price = Mathf.Max(price, discountMin);

        return personalPrice = basePrice?.template?.CreateInstance(price);
    }

    public async Task<GameItem> GetXRayLlamaData(GameAccount account = null)
    {
        if (!IsXRayLlama)
            return null;
        account ??= GameAccount.activeAccount;
        if (!await account.Authenticate())
            return null;
        await account.GetProfile(FnProfileTypes.AccountItems).Query();
        return GetXRayLlamaDataUnsafe(account);
    }

    public GameItem GetXRayLlamaDataUnsafe(GameAccount account = null)
    {
        if (!IsXRayLlama)
            return null;
        account ??= GameAccount.activeAccount;
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
}
