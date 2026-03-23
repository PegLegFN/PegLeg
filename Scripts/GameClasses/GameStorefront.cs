using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

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
		return refreshType is null || DateTime.UtcNow.CompareTo(expirationDates[refreshType.Value]) >= 0;
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

			GD.Print("retrieving catalog from epic...");
			var response = await FnWebAddresses.FortGame
				.MakeRequest("fortnite/api/storefront/v2/catalog")
				.SetAccount(GameAccount.ActiveAccount)
				.Send();
			if (await response.CheckForError())
				return false;
			var catalog = await response.ReadJson();

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

			return true;
		}
		finally
		{
			catalogSemaphore.Release();
		}
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

	public GameOffer this[string offerId] => offers[offerId];
	public GameOffer[] Offers => [.. offers.Values];

	public Dictionary<string, Dictionary<string, GameOffer[]>> GroupCosmeticsByLayout() =>
		offers.Values.GroupBy(o => o.CosmeticSectionId)
		.ToDictionary(
			section => section.Key,
			section => section.GroupBy(o => o.CosmeticLayoutId)
			.ToDictionary(
				group => group.Key,
				group => group.OrderBy(o => -o.SortPriority).ToArray()
			)
		);
}

