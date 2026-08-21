using Amazon.Runtime.Internal.Endpoints.StandardLibrary;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using static GameStorefront.CosmeticSectionData;

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

	public static DateTime lastUpdated { get; private set; } = DateTime.MinValue;
	static Dictionary<string, JsonObject> storefrontCache;
	static Dictionary<string, GameStorefront> storefronts = [];
	public static bool RequiresUpdate(RefreshTimeType? refreshType)
	{
		return refreshType is null || DateTime.UtcNow.CompareTo(expirationDates[refreshType.Value]) >= 0;
	}

	static async Task<JsonNode> FetchCatalog()
	{
		if (!GameAccount.ActiveAccount.isOwned)
		{
			if (DateTime.UtcNow.Minute == 0 && DateTime.UtcNow.Second == 0)
			{
				GD.Print("pausing 5 seconds to wait for lite catalog");
				await Helpers.WaitForTimer(5);
			}
			bool useRetries = DateTime.UtcNow.Hour == 0 && DateTime.UtcNow.Minute == 0 && DateTime.UtcNow.Second < 30;
			for (int i = 0; i < 5; i++)
			{
				var liteResponse = await ApiWebAddresses.pegLegLiteBucket
					.MakeRequest("latestCatalog.json")
					.Send();
				if (!await liteResponse.CheckForError())
				{
					var data = await liteResponse.ReadJson();
					var expiration = data["expiration"].Deserialize<DateTime>();
					if (expiration >= DateTime.UtcNow)
						return data;
				}
				if (!useRetries)
					break;
				if (i < 4)
				{
					GD.Print("lite catalog still out of date, pausing 5 more seconds");
					await Helpers.WaitForTimer(5);
				}
				else
				{
					GD.Print("abandoning lite catalog retries");
				}
			}
			GD.Print("Warning: lite catalog is out of date");
			return null;
		}

		GD.Print("retrieving catalog from epic...");
		var response = await FnWebAddresses.FortGame
			.MakeRequest("fortnite/api/storefront/v2/catalog")
			.SetAccount(GameAccount.ActiveAccount)
			.Send();
		if (await response.CheckForError())
			return null;
		return await response.ReadJson();
	}

	static SemaphoreSlim catalogSemaphore = new(1);
	public static async Task<bool> UpdateCatalog(RefreshTimeType? refreshType = null)
	{
		if (!RequiresUpdate(refreshType))
			return true;
		await catalogSemaphore.WaitAsync();
		try
		{
			if (!RequiresUpdate(refreshType))
				return true;

			var catalog = await FetchCatalog();
			if(catalog is null)
				return false;
			lastUpdated = DateTime.UtcNow;

			storefrontCache = catalog["storefronts"]
				.AsArray()
				.Select(n => n.AsObject())
				.ToDictionary(n => n["name"].ToString());

			List<string> toRemove = [];
			foreach (var kvp in storefronts)
			{
				if (!storefrontCache.ContainsKey(kvp.Key))
				{
					toRemove.Add(kvp.Key);
					continue;
				}
				kvp.Value.CheckForChanges(storefrontCache[kvp.Key]["catalogEntries"].AsArray());
			}
			foreach (var sfKey in toRemove)
			{
				GD.Print("a known storefront is missing in the catalog");
				storefronts[sfKey].DisconnectAll();
			}

			foreach (var refreshTypeKey in expirationDates.Keys)
			{
				expirationDates[refreshTypeKey] = RefreshTimerController.GetRefreshTime(refreshTypeKey);
			}

			SendCatalogToBucket(catalog);

			return true;
		}
		finally
		{
			catalogSemaphore.Release();
		}
	}


	static DateTime lastBucketedAt;
	static async void SendCatalogToBucket(JsonNode catalog)
	{
		if (!BucketHelper.CanUseBucket)
			return;
		if ((lastBucketedAt - DateTime.UtcNow).TotalHours < 1 && lastBucketedAt.Date == DateTime.UtcNow.Date && lastBucketedAt.Hour == DateTime.UtcNow.Hour)
			return;

		var catalogData = catalog.ToString();
		const string litePath = "user://liteCatalog.json";
		if (FileAccess.FileExists(litePath))
		{
			using var latestCatalogFile = FileAccess.Open(litePath, FileAccess.ModeFlags.Read);
			if (latestCatalogFile.GetError() == Error.Ok && catalogData.Hash() == latestCatalogFile.GetAsText().Hash())
				return;
		}

		lastBucketedAt = DateTime.UtcNow;

		using (var latestCatalogFile = FileAccess.Open(litePath, FileAccess.ModeFlags.Write))
			latestCatalogFile.StoreString(catalogData);

		await BucketHelper.SendToBucket(litePath, "latestCatalog.json");
	}

	static GameStorefront GetOrCreateStorefront(string storefrontKey, RefreshTimeType? refreshType = null)
	{
		if (storefronts.TryGetValue(storefrontKey, out GameStorefront value) == true)
			return value;

		GameStorefront storefront = new(storefrontKey, refreshType);
		storefronts[storefrontKey] = storefront;

		if (storefrontCache?.TryGetValue(storefrontKey, out JsonObject sfData) == true)
		{
			storefront.InitialiseOffers(sfData["catalogEntries"].AsArray().Select(n => new GameOffer(storefront, n.AsObject())));
		}
		return storefront;
	}

	//TODO: in BlakebeardLib, offer type customisation should provide more user control, possibly
	//using generics to constrain storefront to a specific type of GameOffer

	public static async Task<GameStorefront> GetStorefront(string storefrontKey, RefreshTimeType? refreshType = null)
	{
		if (!await UpdateCatalog(refreshType))
			return null;
		return GetOrCreateStorefront(storefrontKey, refreshType);
	}

	public static GameStorefront XRayLlamas => GetOrCreateStorefront("CardPackStorePreroll", RefreshTimeType.Hourly);
	public static GameStorefront RandomLlamas => GetOrCreateStorefront("CardPackStoreGameplay", RefreshTimeType.Hourly);
	public static GameStorefront CampaignWeekly => GetOrCreateStorefront("STWRotationalEventStorefront", RefreshTimeType.Weekly);
	public static GameStorefront CampaignEvent => GetOrCreateStorefront("STWSpecialEventStorefront", RefreshTimeType.Event);
	public static GameStorefront CosmeticWeekly => GetOrCreateStorefront("BRWeeklyStorefront", RefreshTimeType.Daily);
	public static GameStorefront CosmeticDaily => GetOrCreateStorefront("BRDailyStorefront", RefreshTimeType.Daily);

	public static GameOffer GetExistingOffer(string offerId)
	{
		return storefronts.Values
			.Select(s => s.offers.TryGetValue(offerId, out var offer) ? offer : null)
			.FirstOrDefault(o => o is not null);
	}

	public static DateTime cosmeticSectionsExpires { get; private set; } = DateTime.MinValue;
	public static Dictionary<string, CosmeticSectionData> cosmeticSectionsCache { get; private set; } = [];
	public static bool TryGetCosmeticSection(string templateId, out CosmeticSectionData result) => cosmeticSectionsCache.TryGetValue(templateId??"", out result);
	public static async Task<Dictionary<string, CosmeticSectionData>> FetchCosmeticSections()
	{
		if (cosmeticSectionsExpires > DateTime.UtcNow)
			return cosmeticSectionsCache;

		var response = await FnWebAddresses.FortContent
			.MakeRequest("/content/api/pages/fortnite-game/mp-item-shop")
			.Send();
		if (await response.CheckForError())
			return cosmeticSectionsCache;

		cosmeticSectionsExpires = RefreshTimerController.GetRefreshTime(RefreshTimeType.Hourly);

		var root = await response.ReadJson();
		var sections = root["shopData"]["sections"].Deserialize<CosmeticSectionData[]>();
		//var duplicates = sections.GroupBy(s => s.sectionID).Where(g => g.Count() > 1).ToArray();
		//GD.Print($"Duplicate sections: \n{duplicates.Select(g => $"{g.Key} = {JsonSerializer.Serialize(g.ToArray())}").JoinString("\n")}");
		return cosmeticSectionsCache = sections.DistinctBy(s=>s.sectionID).ToDictionary(s => s.sectionID);
	}
	public static DateTime jamTracksExpire { get; private set; } = DateTime.MinValue;
	public static Dictionary<string, JamTrackMeta> jamTracksCache { get; private set; } = [];
	public static bool TryGetJamTrack(string templateId, out JamTrackMeta result) => jamTracksCache.TryGetValue(templateId??"", out result);
	public static async Task FetchJamTracks()
	{
		if (jamTracksExpire > DateTime.UtcNow)
			return;

		var response = await FnWebAddresses.FortContent
			.MakeRequest("/content/api/pages/fortnite-game/spark-tracks")
			.Send();
		if (await response.CheckForError())
			return;

		jamTracksExpire = RefreshTimerController.GetRefreshTime(RefreshTimeType.Hourly);

		var root = await response.ReadJson<JsonObject>();
		root.Remove("_title");
		root.Remove("_noIndex");
		root.Remove("_activeDate");
		root.Remove("lastModified");
		root.Remove("_locale");
		root.Remove("_templateName");
		root.Remove("_suggestedPrefetch");

		var containerDict = root.Deserialize<Dictionary<string, JamTrackContainer>>();
		jamTracksCache = containerDict.ToDictionary(kvp => kvp.Value.track.templateId, kvp => kvp.Value.track);
	}

	public static async Task FetchCosmeticDependancies(bool withExternal=true)
	{
		await Task.WhenAll([
			FetchCosmeticSections(),
			FetchJamTracks(),
			CosmeticDaily.Fetch(),
			CosmeticWeekly.Fetch()
		]);
		if (!withExternal)
			return;
		try
		{
			await ExternalCosmetics.LoadCosmeticShopData();
		}
		catch { }
	}

	#endregion

	public event Action<GameOffer> OnOfferAdded;
	public event Action<GameOffer> OnOfferChanged;
	public event Action<GameOffer> OnOfferRemoved;

	RefreshTimeType linkedRefreshType;
	public bool isValid { get; private set; } = true;
	public string storefrontId { get; private set; }
	Dictionary<string, GameOffer> offers;

	public GameStorefront(string storefrontId, RefreshTimeType? linkedRefreshType = null)
	{
		this.storefrontId = storefrontId;
		//this.offers = offers.ToDictionary(offer => offer.OfferId);
		this.linkedRefreshType = linkedRefreshType ?? RefreshTimeType.Hourly;
	}

	private void InitialiseOffers(IEnumerable<GameOffer> offers)
	{
		this.offers ??= offers.ToDictionary(offer => offer.OfferId);
	}

	public async Task<GameStorefront> Fetch(bool force = false)
	{
		await UpdateCatalog(force ? null : linkedRefreshType);
		return this;
	}

	void CheckForChanges(JsonArray catalogEntries)
	{
		offers ??= [];
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
			GameOffer offer = new GameOffer(this, catalogEntriesDict[offerId]);
			offers[offerId] = offer;
			OnOfferAdded?.Invoke(offer);
		}
	}

	private void DisconnectAll()
	{
		foreach (var offer in offers?.Values)
		{
			offer.DisconnectFromStorefront();
		}
		offers.Clear();
	}

	public GameOffer this[string offerId] => offers?[offerId] ?? null;
	public GameOffer[] Offers => offers?.Values?.ToArray() ?? [];

	public Dictionary<string, Dictionary<string, GameOffer[]>> GroupCosmeticsByLayout() =>
		(offers ?? []).Values.GroupBy(o => o.CosmeticSectionId)
		.ToDictionary(
			section => section.Key,
			section => section.GroupBy(o => o.CosmeticLayoutId)
			.ToDictionary(
				group => group.Key,
				group => group.OrderBy(o => -o.SortPriority).ToArray()
			)
		);

	public record struct CosmeticSectionData : IStackRankElement
	{
		public string displayName { get; init; }
		public string category { get; init; }
		public Meta metadata { get; init; }
		public string sectionID { get; init; }
		public string subtitle { get; init; }

		[JsonIgnore]
		Rank[] IStackRankElement.StackRanks => metadata.stackRanks;

		public record struct Meta
		{
			public Background background { get; init; }
			public Row[] offerGroups { get; init; }
			public string showIneligibleOffers { get; init; }
			public Rank[] stackRanks { get; init; }
		}

		public record struct Background
		{
			public string cookedAssetKey { get; init; }
			public string customTexture { get; init; }
			public string type { get; init; }
		}

		public record struct Row: IStackRankElement
		{
			[JsonPropertyName("bUseWidePreview")]
			public bool useWidePreview { get; init; }
			public string displayType { get; init; }
			public string offerGroupId { get; init; }
			public Rank[] stackRanks { get; init; }
			[JsonIgnore]
			Rank[] IStackRankElement.StackRanks => stackRanks;
		}

		public record struct Rank
		{
			public string context { get; init; }
			public string productTag { get; init; }
			public int stackRankValue { get; init; }
			public DateTime startDate { get; init; }
		}
	}
	public interface IStackRankElement
	{
		public Rank[] StackRanks { get; }
	}
	record struct JamTrackContainer
	{
		public JamTrackMeta track { get; init; }
	}
	public record struct JamTrackMeta
	{
		[JsonPropertyName("tt")]
		public string title { get; init; }
		[JsonPropertyName("ab")]
		public string album { get; init; }
		[JsonPropertyName("an")]
		public string artist { get; init; }
		[JsonPropertyName("au")]
		public string albumArtURL { get; init; }
		[JsonPropertyName("ry")]
		public int releaseYear { get; init; }
		[JsonPropertyName("dn")]
		public int durationSeconds { get; init; }
		[JsonPropertyName("mt")]
		public int beatsPerMinute { get; init; }
		[JsonPropertyName("su")]
		public string songUUID { get; init; }
		[JsonIgnore]
		public string DurationText => $"{durationSeconds / 60}:{durationSeconds % 60:00}";
		[JsonPropertyName("ti")]
		public string templateId { get; init; }

		[JsonIgnore]
		string uniqueName => templateId.Replace(":","__");

		public ImageTexture GetCachedTexture()
		{
			if (CatalogRequests.TryGetCosmeticTexture(uniqueName, cacheOnly: true) is ImageTexture existingTexture)
				return existingTexture;
			return null;
		}

		public ImageTexture GetLocalTexture(float resolutionScale = 1)
		{
			if (CatalogRequests.TryGetCosmeticTexture(uniqueName, resolutionScale) is ImageTexture existingTexture)
				return existingTexture;
			return null;
		}

		public Image ReadLocalImageDirect()
		{
			if (uniqueName is null)
				return null;
			var path = CatalogRequests.LocalCosmeticResourcePathFromId(uniqueName);
			if (path is null)
				return null;
			return Image.LoadFromFile(path);
		}

		public async Task<ImageTexture> FetchTexture(float resolutionScale = 1)
		{
			if (CatalogRequests.TryGetCosmeticTexture(uniqueName, resolutionScale) is ImageTexture existingTexture)
				return existingTexture;
			await FetchImage();
			return CatalogRequests.TryGetCosmeticTexture(uniqueName);
		}

		public async Task<Image> FetchImage(float resolutionScale = 1)
		{
			if (albumArtURL is null)
				return null;
			if (CatalogRequests.TryGetCosmeticImage(uniqueName, resolutionScale) is Image existingTexture)
				return existingTexture;

			using var result = await WebHelpers.MakeRequest(albumArtURL).Accepts(WebMedia.Image.Any).Send();
			if (await result.CheckForError())
				return null;

			(Image image, byte[] buffer, string type) = await result.ReadImageWithBuffer();
			//Image image = await result.ReadDownloadImage(testStream);
			if (image is null)
				return null;

			CatalogRequests.RegisterCosmeticImageWithBuffer(ref image, buffer, type, uniqueName, resolutionScale);
			return CatalogRequests.TryGetCosmeticImage(uniqueName);
		}
	}
}

public static class GameStorefrontExtensions
{
	extension(GameStorefront.IStackRankElement stackRankElement)
	{
		public int RankValue
		{
			get 
			{
				var ranks = stackRankElement.StackRanks;
				if ((ranks?.Length ?? 0) == 0)
					return 0;
				var rankData = ranks.FirstOrDefault(rank => rank.productTag == "Product.BR");
				if (rankData == default)
					rankData = ranks[0];
				return rankData.stackRankValue;
			}
		}
	}
}

