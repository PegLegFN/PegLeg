using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

public static class ExternalCosmetics
{
	static event Action InvalidateDashOffers;
	//public static event Action OnCosmeticsChanged;

	static Dictionary<string, FNDashOffer> dashOffers = [];
	static Dictionary<string, FNDotDisplayAsset> newDisplayAssets = [];
	static Dictionary<string, string> rawCosmeticPaths = [];
	static Dictionary<string, RawCosmetic> rawCosmetics = [];
	static Dictionary<string, RawDisplayAsset> rawDisplayAssets = [];

	static Dictionary<string, WeakRef> imageCache = [];

	static Queue<DateTime> exportTimestamps = [];

	public static async Task LoadCosmeticShopData()
	{
		//preloads the FNDashAPI shop, the latest display assets from FNDotAPI, and a cosmetic lookup table from FNDotAPI
		var fnDashShopTask = ApiWebAddresses.fnDashApi
			.MakeRequest("v2/shop?responseFlags=4")
			.AddCosmeticHeader()
			.Send();
		var fnDotDisplayAssetTask = ApiWebAddresses.fnDotApi.MakeRequest("v1/export/displayassets/parsed").Send();
		var rawCosmeticPathsTask = ApiWebAddresses.fnDotApi.MakeRequest("v1/export/cosmetics/all").Send();
		try
		{
			await Task.WhenAll(fnDashShopTask, fnDotDisplayAssetTask, rawCosmeticPathsTask);
		}
		catch { }

		var fnDashShopResponse = await fnDashShopTask;
		var newDisplayAssetResponse = await fnDotDisplayAssetTask;
		var cosmeticPathsResponse = await rawCosmeticPathsTask;

		InvalidateDashOffers?.Invoke();
		if (await fnDashShopResponse.CheckForError())
		{
			dashOffers = [];
		}
		else
		{
			dashOffers = (await fnDashShopResponse.ReadJson())["data"]?["entries"]?
				.Deserialize<FNDashOffer[]>(Helpers.JsonOptions.Fields)
				.ToDictionary(o => o.offerId);
		}

		if (!await newDisplayAssetResponse.CheckForError())
		{
			JsonArray unknownNewDisplayAssets = [..
				(await newDisplayAssetResponse.ReadJson())?
				["jsonOutput"]?
				.AsArray()?
				.Where(n => !newDisplayAssets.ContainsKey(n["id"].ToString()))
			];
			newDisplayAssets = newDisplayAssets.Union(
				unknownNewDisplayAssets
				.Deserialize<FNDotDisplayAsset[]>(Helpers.JsonOptions.Fields)
				.ToDictionary(da => da.id)
			).ToDictionary();
		}

		if (!await cosmeticPathsResponse.CheckForError())
		{
			rawCosmeticPaths = (await cosmeticPathsResponse.ReadJson())?
				["jsonOutput"]?.AsArray()
				.ToDictionary(kvp => kvp["id"].ToString(), kvp => kvp["path"].ToString(), StringComparer.OrdinalIgnoreCase);
		}
	}

	public static FNDashOffer GetFNDashOffer(string offerId) => dashOffers.TryGetValue(offerId, out var offer) ? offer : null;
	public static FNDotDisplayAsset GetFNDotDisplayAsset(string displayAssetId)
	{
		if (displayAssetId.Contains('.'))
			displayAssetId = displayAssetId.Split('.')[^1];
		return newDisplayAssets.TryGetValue(displayAssetId, out var displayAsset) ? displayAsset : null;
	}

	static void CheckTimestamps()
	{
		var now = DateTime.Now;
		while (exportTimestamps.Count > 0 && (now - exportTimestamps.Peek()).TotalSeconds > 60)
		{
			exportTimestamps.Dequeue();
		}
	}

	public static bool CanAffordRequests(int plannedRequests = 1)
	{
		CheckTimestamps();
		return exportTimestamps.Count + plannedRequests < 30;
	}

	static string FilenameFromPath(string path)
	{
		int substringStart = path.LastIndexOf('/');
		int substringEnd = path.LastIndexOf('.');
		if (substringEnd == -1)
			substringEnd = path.Length;
		return path[substringStart..substringEnd];
	}

	public static RawDisplayAsset LoadLocalRawDisplayAsset(string displayAssetPath) =>
		LoadLocalRawDisplayAsset(displayAssetPath, out var _);
	public static RawDisplayAsset LoadLocalRawDisplayAsset(string displayAssetPath, out string filename)
	{
		filename = null;
		if (displayAssetPath is null)
			return null;

		filename = FilenameFromPath(displayAssetPath);

		if (rawDisplayAssets.TryGetValue(filename, out var cached))
			return cached;

		if (FileAccess.FileExists($"user://cosmetic_meta/{filename}.json"))
		{
			using var metaFile = FileAccess.Open($"user://cosmetic_meta/{filename}.json", FileAccess.ModeFlags.ReadWrite);
			var localDisplayAsset = JsonSerializer.Deserialize<RawDisplayAsset>(metaFile.GetAsText(), Helpers.JsonOptions.Fields);
			//make a fake modification to change the modified date when the file is disposed
			metaFile.SeekEnd(-1);
			byte temp = metaFile.Get8();
			metaFile.SeekEnd(-1);
			metaFile.Store8(temp);

			lock (rawCosmetics)
			{
				rawDisplayAssets[filename] = localDisplayAsset;
			}
			return localDisplayAsset;
		}

		return null;
	}

	public static async Task<RawDisplayAsset> LoadRawDisplayAsset(string displayAssetPath)
	{
		if (displayAssetPath is null)
			return null;

		if (LoadLocalRawDisplayAsset(displayAssetPath, out var filename) is RawDisplayAsset cachedDisplayAsset)
			return cachedDisplayAsset;

		if (!CanAffordRequests())
			return null;
		exportTimestamps.Enqueue(DateTime.Now);

		var assetResponse = await ApiWebAddresses.fnDotApi
			.MakeRequest($"v1/export?Path={displayAssetPath}")
			.Send();

		if (await assetResponse.CheckForError())
			return null;

		var displayAsset = await assetResponse.ReadJson<RawDisplayAsset>(Helpers.JsonOptions.Fields);

		if (!DirAccess.DirExistsAbsolute("user://cosmetic_images"))
			DirAccess.MakeDirAbsolute("user://cosmetic_images");

		var serialisedDisplayAsset = JsonSerializer.Serialize(displayAsset);
		using var metafile = FileAccess.Open($"user://cosmetic_meta/{filename}.json", FileAccess.ModeFlags.Write);
		metafile.StoreString(serialisedDisplayAsset);

		return displayAsset;
	}

	public static RawCosmetic LoadLocalRawCosmetic(string itemId)
	{
		if (itemId is null)
			return null;

		if (rawCosmetics.TryGetValue(itemId, out var cached))
			return cached;

		if (FileAccess.FileExists($"user://cosmetic_meta/{itemId}.json"))
		{
			using var metaFile = FileAccess.Open($"user://cosmetic_meta/{itemId}.json", FileAccess.ModeFlags.ReadWrite);
			var localMeta = JsonSerializer.Deserialize<RawCosmetic>(metaFile.GetAsText());
			//make a fake modification to change the modified date when the file is disposed
			metaFile.SeekEnd(-1);
			byte temp = metaFile.Get8();
			metaFile.SeekEnd(-1);
			metaFile.Store8(temp);

			lock (rawCosmetics)
			{
				rawCosmetics[itemId] = localMeta;
			}
			return localMeta;
		}

		return null;
	}

	public static async Task<RawCosmetic> LoadRawCosmetic(string itemId, bool saveToFile = false)
	{
		if (itemId.Contains(':'))
			itemId = itemId.Split(':')[^1];

		if (LoadLocalRawCosmetic(itemId) is RawCosmetic cachedCosmetic)
			return cachedCosmetic;

		if (!rawCosmeticPaths.TryGetValue(itemId, out var itemPath))
			return null;

		if (!CanAffordRequests())
			return null;
		exportTimestamps.Enqueue(DateTime.Now);

		try
		{
			var rawResponse = await ApiWebAddresses.fnDotApi.MakeRequest($"v1/export?Path={itemPath}").Send();
			if (await rawResponse.CheckForError())
				return null;
			var rawObjArray = (await rawResponse.ReadJson())?["jsonOutput"]?.AsArray();
			var rawCosmeticNode = rawObjArray.FirstOrDefault(n => n["Name"].ToString().Equals(itemId, StringComparison.InvariantCultureIgnoreCase));
			var rawCosmetic = rawCosmeticNode.Deserialize<RawCosmetic>(Helpers.JsonOptions.Fields) with
			{
				variants = [
					..rawObjArray.Select(n =>
					{
						try
						{
							return n.Deserialize<RawCosmetic.VariantChannel>(Helpers.JsonOptions.Fields);
						}
						catch { }
						return null;
					}).Where(n => n is not null)
				]
			};

			lock (rawCosmetics)
			{
				rawCosmetics.Add(itemId, rawCosmetic);
			}

			if (saveToFile)
			{
				if (!DirAccess.DirExistsAbsolute("user://cosmetic_meta"))
					DirAccess.MakeDirAbsolute("user://cosmetic_meta");
				var serialisedCosmetic = JsonSerializer.Serialize(rawCosmetic);
				using var metafile = FileAccess.Open($"user://cosmetic_meta/{itemId}.json", FileAccess.ModeFlags.Write);
				metafile.StoreString(serialisedCosmetic);
			}

			return rawCosmetic;
		}
		catch { }
		return null;
	}

	public static ImageTexture LoadLocalImageFromGamePath(string path) =>
		LoadLocalImageFromGamePath(path, out _);
	static ImageTexture LoadLocalImageFromGamePath(string path, out string filename)
	{
		filename = null;
		if (path is null)
			return null;
		filename = FilenameFromPath(path);
		return LoadLocalImage(filename);
	}

	public static async Task<ImageTexture> LoadImageFromGamePath(string path)
	{
		if (path is null)
			return null;

		if (LoadLocalImageFromGamePath(path, out var filename) is ImageTexture cachedImage)
			return cachedImage;

		if (!CanAffordRequests())
			return null;
		exportTimestamps.Enqueue(DateTime.Now);

		return await LoadRemoteImage(() => ApiWebAddresses.fnDotApi.MakeRequest($"v1/export?Path={path}"), filename);
	}

	const float imageSizeLimit = 256;

	static ImageTexture LoadLocalImage(string identifier)
	{
		if (identifier is null)
			return null;

		lock (imageCache)
		{
			if (imageCache.TryGetWeakRef<ImageTexture>(identifier, out var cachedImage))
				return cachedImage;
		}

		if (FileAccess.FileExists($"user://cosmetic_images/{identifier}.png"))
		{
			Image image = new();
			using var imageFile = FileAccess.Open($"user://cosmetic_images/{identifier}.png", FileAccess.ModeFlags.ReadWrite);
			if (image.LoadPngFromBuffer(imageFile.GetBuffer((long)imageFile.GetLength())) == Error.Ok)
			{
				//make a fake modification to change the modified date when the file is disposed
				imageFile.SeekEnd(-1);
				byte temp = imageFile.Get8();
				imageFile.SeekEnd(-1);
				imageFile.Store8(temp);

				var imageSize = image.GetSize();
				var startingSize = imageSize;
				var clampedSize = imageSize;
				if (clampedSize.X > imageSizeLimit)
					clampedSize = (Vector2I)((Vector2)clampedSize * (imageSizeLimit / clampedSize.X));
				if (clampedSize.Y > imageSizeLimit)
					clampedSize = (Vector2I)((Vector2)clampedSize * (imageSizeLimit / clampedSize.Y));
				if (imageSize.X != clampedSize.X || imageSize.Y != clampedSize.Y)
				{
					if (imageSize.X < 1 || imageSize.Y == 1)
						GD.PushWarning($"Cosmetic Size Error: {startingSize} >> {imageSize}");
					image.Resize(Mathf.Max(clampedSize.X, 1), Mathf.Max(clampedSize.Y, 1));
				}

				var imageTex = ImageTexture.CreateFromImage(image);
				imageTex.ResourcePath = identifier;
				lock (imageCache)
				{
					imageCache[identifier] = GodotObject.WeakRef(imageTex);
				}
				return imageTex;
			}
		}
		return null;
	}

	static async Task<ImageTexture> LoadRemoteImage(Func<WebHelpers.BoundHttpsRequestMessage> requestBuilder, string identifier)
	{
		try
		{
			var rawResponse = await requestBuilder().Send();
			Image image = new();
			byte[] imageBuffer = await rawResponse.Content.ReadAsByteArrayAsync();
			if (image.LoadPngFromBuffer(imageBuffer) == Error.Ok)
			{
				if (!DirAccess.DirExistsAbsolute("user://cosmetic_images"))
					DirAccess.MakeDirAbsolute("user://cosmetic_images");

				using (var imageFile = FileAccess.Open($"user://cosmetic_images/{identifier}.png", FileAccess.ModeFlags.Write))
				{
					imageFile.StoreBuffer(imageBuffer);
				}

				var imageSize = image.GetSize();
				var startingSize = imageSize;
				var clampedSize = imageSize;
				if (clampedSize.X > imageSizeLimit)
					clampedSize = (Vector2I)((Vector2)clampedSize * (imageSizeLimit / clampedSize.X));
				if (clampedSize.Y > imageSizeLimit)
					clampedSize = (Vector2I)((Vector2)clampedSize * (imageSizeLimit / clampedSize.Y));
				if (imageSize.X != clampedSize.X || imageSize.Y != clampedSize.Y)
				{
					if (imageSize.X < 1 || imageSize.Y == 1)
						GD.PushWarning($"Cosmetic Size Error: {startingSize} >> {imageSize}");
					image.Resize(Mathf.Max(clampedSize.X, 1), Mathf.Max(clampedSize.Y, 1));
				}

				var imageTex = ImageTexture.CreateFromImage(image);
				imageTex.ResourcePath = identifier;
				lock (imageCache)
				{
					imageCache[identifier] = GodotObject.WeakRef(imageTex);
				}
			}
		}
		catch { }
		return null;
	}

	#region FNDash Data Structures
	[JsonSerializable(typeof(FNDashOffer))]
	public record FNDashOffer : IJsonOnDeserialized
	{
		public string offerId;
		public LayoutData? layout;
		public DisplayData? newDisplayAsset;
		public BundleData? bundle;
		public FNDashCosmetic[] brItems;
		public FNDashCosmetic[] cars;
		public FNDashCosmetic[] instruments;
		public FNDashJamTrack[] tracks;
		[JsonIgnore]
		public bool Valid { get; private set; }
#pragma warning disable CS0649 //Field is never assigned to, and will always have its default value
		[JsonInclude]
		DateTime inDate;
		[JsonInclude]
		DateTime outDate;
#pragma warning restore CS0649 //Field is never assigned to, and will always have its default value

		[JsonIgnore]
		FNDashCosmetic FirstCosmeticInternal =>
			(brItems.Length > 0 ? brItems[0] : null) ??
			(cars.Length > 0 ? cars[0] : null) ??
			(instruments.Length > 0 ? instruments[0] : null);

		[JsonIgnore]
		GameOffer offer;
		[JsonIgnore]
		public GameOffer Offer
		{
			get
			{
				if (offer.storefront == null)
					offer = null;
				return offer ??= GameStorefront.GetExistingOffer(offerId);
			}
		}

		[JsonIgnore]
		public IFNDashCosmetic FirstCosmetic =>
			(brItems.Length > 0 ? brItems[0] : null) ??
			(cars.Length > 0 ? cars[0] : null) ??
			(instruments.Length > 0 ? instruments[0] : null) ??
			(tracks.Length > 0 ? (IFNDashCosmetic)tracks[0] : null);

		public IFNDashCosmetic[] AllCosmetics => [
			..brItems.Cast<IFNDashCosmetic>(),
			..cars.Cast<IFNDashCosmetic>(),
			..instruments.Cast<IFNDashCosmetic>(),
			..tracks.Cast<IFNDashCosmetic>()
		];

		public void OnDeserialized()
		{
			Valid = true;
			InvalidateDashOffers += Invalidate;
		}

		private void Invalidate()
		{
			Valid = false;
			InvalidateDashOffers -= Invalidate;
		}

		public string DisplayName => bundle?.name ?? FirstCosmetic?.Name ?? "<Unknown>";
		public string DisplayType
		{
			get
			{
				var items = AllCosmetics;
				if (items.Length == 0)
					return "<Empty?>";
				if (bundle is not null)
					return $"Bundle [{items.Length} item{(items.Length > 1 ? "s" : "")}]";
				return $"{items[0].DisplayType}" + (items.Length > 1 ? $" [+{items.Length - 1} item{(items.Length > 2 ? "s" : "")}]" : "");
			}
		}

		public ImageTexture GetLocalOfferImage()
		{
			if (newDisplayAsset is not null)
				return LoadLocalImage(newDisplayAsset?.id);
			if (FirstCosmeticInternal is FNDashCosmetic first)
				return LoadLocalImage(first.Id);
			if (tracks?.Length > 0)
				return LoadLocalImage(tracks[0].Id);
			return null;
		}

		public async Task<ImageTexture> LoadOfferImage()
		{
			if (GetLocalOfferImage() is ImageTexture cached)
				return cached;

			if (newDisplayAsset is not null)
				return await LoadRemoteImage(() => WebHelpers.MakeRequest(newDisplayAsset?.renderImages[0].image), newDisplayAsset?.id);
			if (FirstCosmeticInternal is FNDashCosmetic first)
				return await LoadRemoteImage(() => WebHelpers.MakeRequest(first.Images?.featured), first.Id);
			if (tracks?.Length > 0)
				return await LoadRemoteImage(() => WebHelpers.MakeRequest(tracks[0].AlbumArt), tracks[0].Id);
			return null;
		}

		public CosmeticMeta GenerateCosmeticMeta()
		{
			return new(AllCosmetics.Select(c => c.GenerateCosmeticMeta(inDate, outDate)).ToArray());
		}

		public record struct BundleData
		{
			public string name;
			public string info;
		}

		public record struct LayoutData
		{
			public string id;
			public string name;
			public int index;
			public int rank;
			public string displayType;
			//todo: deserialise to proper dictionaries
			public LayoutKVP[] textureMetadata;
			public LayoutKVP[] stringMetadata;
			public LayoutKVP[] textMetadata;
			public record struct LayoutKVP
			{
				public string key;
				public string value;
			}
		}

		public record struct DisplayData
		{
			public string id;
			public DisplayImage[] renderImages;
			public record struct DisplayImage
			{
				public string productTag;
				public string image;
			}
		}
	}

	public interface IFNDashCosmetic
	{
		public string Id { get; }
		public string Name { get; }
		public string Description { get; }
		public string DisplayType { get; }
		protected DateTime[] ShopHistory { get; }

		public DateTime? LastSeen(DateTime inDate)
		{
			var shopHistory = ShopHistory;

			for (int i = shopHistory.Length - 1; i >= 0; i--)
			{
				DateTime utcDate = shopHistory[i].ToUniversalTime();
				if (utcDate.CompareTo(inDate) == -1)
				{
					return utcDate;
				}
			}

			return null;
		}

		public bool IntroducedRecently(int dayThreshold = 7)
		{
			var shopHistory = ShopHistory;
			if (shopHistory.Length == 0)
				return true;
			return (shopHistory[0].ToUniversalTime() - DateTime.UtcNow.Date).TotalDays < 7;
		}

		public CosmeticMeta GenerateCosmeticMeta(DateTime inDate, DateTime outDate)
		{
			var lastSeen = LastSeen(inDate);
			var introducedRecently = IntroducedRecently();
			return new()
			{
				lastSeenDaysAgo = lastSeen is null ? 0 : (int)(DateTime.UtcNow.Date - lastSeen.Value).TotalDays,
				isRecentlyNew = introducedRecently,
				isAddedToday = inDate == DateTime.UtcNow.Date,
				isLeavingSoon = (outDate - DateTime.UtcNow.Date).TotalHours < 24,
				lastAddedDate = lastSeen is null ? DateTime.UtcNow.Date : lastSeen.Value
			};
		}
	}

	[JsonSerializable(typeof(FNDashCosmetic))]
	public record FNDashCosmetic : IFNDashCosmetic
	{
		[JsonPropertyName("id")]
		public string Id { get; private set; }
		[JsonPropertyName("name")]
		public string Name { get; private set; }
		[JsonPropertyName("description")]
		public string Description { get; private set; }
		[JsonPropertyName("type")]
		public TypeData? Type { get; private set; }
		[JsonPropertyName("introduction")]
		public IntroductionData? Introduction { get; private set; }
		[JsonPropertyName("images")]
		public ImagePathData? Images { get; private set; }
		[JsonPropertyName("shopHistory")]
		public DateTime[] ShopHistory { get; private set; }

		[JsonIgnore]
		public string DisplayType => Type?.displayValue;

		public record struct TypeData
		{
			public string value;
			public string displayValue;
			public string backendValue;
		}
		public record struct IntroductionData
		{
			public string chapter;
			public string season;
			public string text;
			public int backendValue;
		}
		public record struct ImagePathData
		{
			public string smallIcon;
			public string icon;
			public string featured;

			[JsonIgnore]
			public string Main => featured ?? icon ?? smallIcon;
		}
	}

	[JsonSerializable(typeof(FNDashJamTrack))]
	public record FNDashJamTrack : IFNDashCosmetic
	{
		[JsonPropertyName("id")]
		public string Id { get; private set; }
		[JsonPropertyName("title")]
		public string Title { get; private set; }
		[JsonPropertyName("artist")]
		public string Artist { get; private set; }
		[JsonPropertyName("album")]
		public string Album { get; private set; }
		[JsonPropertyName("releaseYear")]
		public string ReleaseYear { get; private set; }
		[JsonPropertyName("albumArt")]
		public string AlbumArt { get; private set; }
		[JsonPropertyName("shopHistory")]
		public DateTime[] ShopHistory { get; private set; }

		[JsonIgnore]
		public string Name => Title;
		[JsonIgnore]
		public string Description => $"{Artist}\n{ReleaseYear}";
		[JsonIgnore]
		public string DisplayType => "Jam Track";
	}
	#endregion

	#region FNDot Data Structures
	[JsonSerializable(typeof(FNDotDisplayAsset))]
	public record FNDotDisplayAsset
	{
		public string id;
		public string cosmeticId;
		public DisplayImage[] renderImages;
		public record struct DisplayImage
		{
			public string productTag;
			public string imagePath;
		}
		public ImageTexture GetLocalOfferImage() => GetLocalOfferImage(out _);
		ImageTexture GetLocalOfferImage(out string targetPath)
		{
			targetPath = null;
			if (renderImages.Length == 0)
				return null;
			DisplayImage? target = renderImages.FirstOrDefault(i => i.productTag == "Product.BR");
			target ??= renderImages[0];
			targetPath = target?.imagePath;
			return LoadLocalImageFromGamePath(targetPath);
		}

		public async Task<ImageTexture> LoadOfferImage()
		{
			if (GetLocalOfferImage(out var path) is ImageTexture cached)
				return cached;
			return await LoadImageFromGamePath(path);
		}
	}

	[JsonSerializable(typeof(FnDotCosmetic))]
	public record FnDotCosmetic
	{
		public string id;
		public string name;
		public string description;
		public TypeData type;
		public TypeData series;
		public IntroductionData introduction;
		public ImagePathData images;

		public record struct TypeData
		{
			public string id;
			public string name;
		}
		public record struct IntroductionData
		{
			public int id;
			public string name;
			public string chapter;
			public string season;
		}
		public record struct ImagePathData
		{
			public string smallIcon;
			public string icon;
		}
	}

	public abstract record RawAsset<PropertyType> where PropertyType : class
	{
		public string Type { get; private set; }
		public string Name { get; private set; }
		public string Outer { get; private set; }
		[JsonPropertyName("Properties")]
		public PropertyType properties { get; private set; }

		public record struct RawLocString
		{
			public string localizedString;
			public static implicit operator string(RawLocString s) => s.localizedString;
		}

		public record struct RawTagContainer
		{
			public string TagName;
			public static implicit operator string(RawTagContainer t) => t.TagName;
		}

		public record struct RawObjectReference
		{
			public string ObjectName;
			public string ObjectPath;
			public async Task<RawCosmetic> LoadCosmetic() => await LoadRawCosmetic(ObjectName.Split('\'')[1]);
			public RawCosmetic LoadLocalCosmetic() => LoadLocalRawCosmetic(ObjectName.Split('\'')[1]);
		}

		public record struct RawAssetReference
		{
			public string AssetPathName;
			public string SubPathString;

			public async Task<ImageTexture> LoadAsImage() =>
				await LoadImageFromGamePath(AssetPathName);
			public ImageTexture LocalImage =>
				LoadLocalImageFromGamePath(AssetPathName);
		}
	}

	[JsonSerializable(typeof(RawDisplayAsset))]
	public record RawDisplayAsset : RawAsset<RawDisplayAsset.Properties>
	{
		public record Properties
		{
			public RawLocString DisplayName { get; private set; }
		}
	}

	[JsonSerializable(typeof(RawCosmetic))]
	public record RawCosmetic : RawAsset<RawCosmetic.Properties>
	{
		public record Properties
		{
			public RawLocString ItemName { get; private set; }
			public RawLocString ItemDescription { get; private set; }
			public RawLocString ItemShortDescription { get; private set; }
			[JsonPropertyName("cosmetic_item")]
			public RawObjectReference CosmeticItem { get; private set; }
		}

		public VariantChannel[] variants { get; init; }

		public async Task<RawCosmetic> VarientTokenBaseItem()
		{
			return await properties.CosmeticItem.LoadCosmetic() ?? this;
		}

		public async Task<bool> LoadVariantIcons()
		{
			var unloaded = variants.Where(v => v.Options.Any(o => o.LocalImage == null));
			if (!unloaded.Any())
				return true;
			var imageTotal = unloaded.Select(v => v.Options.Length).Sum();
			if (!CanAffordRequests(imageTotal))
			{
				var firstUnloaded = unloaded.FirstOrDefault();
				return false;
			}

			await Task.WhenAll(variants.SelectMany(v => v.Options.Select(o => o.LoadImage())));

			return true;
		}

		public record VariantChannel : RawAsset<VariantChannel.Properties>
		{
			public record Properties
			{
				public RawLocString VariantChannelName;
				public RawTagContainer VariantChannelTag;
				public VariantOption[] GenericPropertyOptions;
				public VariantOption[] MaterialOptions;
				public VariantOption[] PartOptions;
			}
			[JsonIgnore]
			public VariantOption[] Options => properties?.GenericPropertyOptions ?? properties?.MaterialOptions ?? properties?.PartOptions;

			public record VariantOption
			{
				public RawLocString VariantName;
				[JsonInclude]
				RawAssetReference PreviewImage;
				ImageTexture imageTexture;
				bool hasCheckedForLocalImage;

				[JsonIgnore]
				public ImageTexture LocalImage
				{
					get
					{
						if (hasCheckedForLocalImage)
							return imageTexture;
						hasCheckedForLocalImage = true;
						return imageTexture = PreviewImage.LocalImage;
					}
				}

				public async Task<ImageTexture> LoadImage() =>
					LocalImage ?? await PreviewImage.LoadAsImage();
			}
		}
	}
	#endregion

	public record struct CosmeticMeta
	{
		public int lastSeenDaysAgo;
		public bool isRecentlyNew;
		public bool isAddedToday;
		public bool isLeavingSoon;
		public bool isBestseller;
		public DateTime? lastAddedDate;

		public CosmeticMeta() { }

		public CosmeticMeta(CosmeticMeta[] itemMetadatas)
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

			lastAddedDate = itemMetadatas.Select(m => m.lastAddedDate).OrderBy(d => d).FirstOrDefault();
		}
	}
}
