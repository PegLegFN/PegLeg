using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

static class CosmeticRequests
{
    public static Dictionary<string, FNAPIOffer> CosmeticOffers { get; private set; } = [];
    public static Dictionary<string, DillyDisplayAsset> DisplayAssets { get; private set; } = [];
    public static Dictionary<string, DillyCosmetic> NewCosmetics { get; private set; } = [];

    static Dictionary<string, string> rawCosmeticPaths = [];
    static Dictionary<string, RawCosmetic> rawCosmetics = [];

    static Dictionary<string, WeakRef> imageCache = [];

    static Queue<DateTime> exportTimestamps = [];

    public static async Task LoadCosmeticShopData()
    {
        //preloads the FNAPI shop, the latest cosmetics and display assets from dillyapi, and a cosmetic lookup table from dillyapi
        var fnapiShopTask = WebClients.fnApi.MakeRequest("v2/shop?responseFlags=4")
            .AddHeader("x-api-key", "676b8175-a049-4f03-b829-323c95153a43")
            .Send();
        var dillyDisplayAssets = WebClients.dillyApi.MakeRequest("v1/export/displayassets/parsed").Send();
        var dillyNewCosmetics = WebClients.dillyApi.MakeRequest("v1/cosmetics/new").Send();
        var dillyCosmeticPaths = WebClients.dillyApi.MakeRequest("v1/export/cosmetics/all").Send();
        try
        {
            await Task.WhenAll(fnapiShopTask, dillyDisplayAssets, dillyNewCosmetics, dillyCosmeticPaths);
        }
        catch { }
        CosmeticOffers = (await fnapiShopTask.Result.Content.ReadFromJsonAsync<JsonNode>())?
            ["data"]?["entries"]?
            .Deserialize<FNAPIOffer[]>()
            .ToDictionary(o => o.offerId);

        DisplayAssets = (await dillyDisplayAssets.Result.Content.ReadFromJsonAsync<JsonNode>())?
            ["jsonOutput"]?
            .Deserialize<DillyDisplayAsset[]>()
            .ToDictionary(da => da.id);

        NewCosmetics = (await dillyNewCosmetics.Result.Content.ReadFromJsonAsync<JsonNode>())?
            ["data"]?
            .Deserialize<DillyCosmetic[]>()
            .ToDictionary(c => c.id);

        rawCosmeticPaths = (await dillyNewCosmetics.Result.Content.ReadFromJsonAsync<JsonNode>())?
            ["jsonOutput"]?.AsArray()
            .ToDictionary(kvp => kvp["id"].ToString(), kvp => kvp["path"].ToString());
    }

    static void CheckTimestamps()
    {
        var now = DateTime.Now;
        while (exportTimestamps.Count>0 && (now-exportTimestamps.Peek()).TotalSeconds>60)
        {
            exportTimestamps.Dequeue();
        }
    }

    public static bool CanAffordRequests(int plannedRequests = 1)
    {
        CheckTimestamps();
        return exportTimestamps.Count + plannedRequests < 30;
    }

    public static RawCosmetic LoadLocalRawCosmetic(string itemId)
    {
        lock (rawCosmetics)
        {
            if (rawCosmetics.TryGetValue(itemId, out var cached))
                return cached;
        }

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
        if(LoadLocalRawCosmetic(itemId) is RawCosmetic cachedCosmetic)
            return cachedCosmetic;

        if(!rawCosmeticPaths.TryGetValue(itemId, out var itemPath))
            return null;

        if (!CanAffordRequests())
            return null;
        exportTimestamps.Enqueue(DateTime.Now);

        try
        {
            var rawResponse = await WebClients.dillyApi.MakeRequest($"v1/export?Path={itemPath}").Send();
            var rawObjArray = (await rawResponse.Content.ReadFromJsonAsync<JsonNode>())?["jsonOutput"]?.AsArray();
            var rawCosmeticNode = rawObjArray.FirstOrDefault(n => n["Name"].ToString().Equals(itemId, StringComparison.InvariantCultureIgnoreCase));
            var rawCosmetic = rawCosmeticNode.Deserialize<RawCosmetic>();
            rawObjArray.Remove(rawCosmeticNode);
            rawCosmetic.variants = rawObjArray.Select(n =>
            {
                try
                {
                    return n.Deserialize<RawCosmeticVariantChannel>();
                }
                catch { }
                return null;
            }).Where(n => n is not null).ToArray();

            lock (rawCosmetics)
            {
                rawCosmetics.Add(itemId, rawCosmetic);
            }

            if (saveToFile)
            {
                if (!DirAccess.DirExistsAbsolute("user://cosmetic_meta"))
                    DirAccess.MakeDirAbsolute("user://cosmetic_meta");
                var serialisedCosmetic = JsonSerializer.Serialize(rawCosmetic);
                using var metafile = FileAccess.Open($"user://cosmetic_meta/{itemId}.json", FileAccess.ModeFlags.ReadWrite);
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

        lock (imageCache)
        {
            if (imageCache.TryGetImage(path, out var cachedImage))
                return cachedImage;
        }

        int substringStart = path.LastIndexOf('/');
        int substringEnd = path.LastIndexOf('.');
        if (substringEnd == -1)
            substringEnd = path.Length;
        filename = path[substringStart..substringEnd];

        if (FileAccess.FileExists($"user://cosmetic_images/{filename}.png"))
        {
            Image image = new();
            using var imageFile = FileAccess.Open($"user://cosmetic_images/{filename}.png", FileAccess.ModeFlags.ReadWrite);
            if (image.LoadPngFromBuffer(imageFile.GetBuffer((long)imageFile.GetLength())) == Error.Ok)
            {
                //make a fake modification to change the modified date when the file is disposed
                imageFile.SeekEnd(-1);
                byte temp = imageFile.Get8();
                imageFile.SeekEnd(-1);
                imageFile.Store8(temp);

                var imageTex = ImageTexture.CreateFromImage(image);
                imageTex.ResourceName = path;
                lock (imageCache)
                {
                    imageCache[path] = GodotObject.WeakRef(imageTex);
                }
                return imageTex;
            }
        }
        return null;
    }

    public static async Task<ImageTexture> LoadImageFromGamePath(string path)
    {
        if (LoadLocalImageFromGamePath(path, out var filename) is ImageTexture localImg)
            return localImg;

        if (!CanAffordRequests())
            return null;
        exportTimestamps.Enqueue(DateTime.Now);

        try
        {
            var rawResponse = await WebClients.dillyApi.MakeRequest($"v1/export?Path={path}").Send();
            Image image = new();
            byte[] imageBuffer = await rawResponse.Content.ReadAsByteArrayAsync();
            if (image.LoadPngFromBuffer(imageBuffer) == Error.Ok)
            {

                if (!DirAccess.DirExistsAbsolute("user://cosmetic_images"))
                    DirAccess.MakeDirAbsolute("user://cosmetic_images");

                using (var imageFile = FileAccess.Open($"user://cosmetic_images/{filename}.png", FileAccess.ModeFlags.Write))
                {
                    imageFile.StoreBuffer(imageBuffer);
                }

                var imageTex = ImageTexture.CreateFromImage(image);
                imageTex.ResourceName = path;
                lock (imageCache)
                {
                    imageCache[path] = GodotObject.WeakRef(imageTex);
                }
            }
        }
        catch { }
        return null;
    }

    #region FNAPI Data Structures
    public record FNAPIOffer
    {
        public string offerId;
        public LayoutData? layout;
        public DisplayData? newDisplayAsset;
        public FNAPICosmetic[] brItems;
        public FNAPICosmetic[] cars;
        public FNAPICosmetic[] instruments;
        public FNAPITrack[] tracks;

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

    public record FNAPICosmetic
    {
        public string id;
        public string name;
        public string description;
        public TypeData? type;
        public IntroductionData? introduction;
        public ImagePathData? images;
        public DateTime[] shopHistory;

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
            public string value;
            public string displayValue;
            public string backendValue;
        }
    }

    public record FNAPITrack
    {
        public string id;
        public string title;
        public string artist;
        public string album;
        public int releaseYear;
        public string albumArt;
        public DateTime[] shopHistory;
    }
    #endregion

    #region DillyAPI Data Structures
    public record DillyDisplayAsset
    {
        public string id;
        public string cosmeticId;
        public DisplayImage[] renderImages;
        public record struct DisplayImage
        {
            public string productTag;
            public string imagePath;
        }
    }

    public record DillyCosmetic
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

    public record RawCosmetic
    {
        public RawLocString ItemName;
        public RawLocString ItemDescription;
        public RawLocString ItemShortDescription;
        public RawCosmeticVariantChannel[] variants;

        public async Task<bool> LoadVariantIcons()
        {
            var unloaded = variants.Where(v => v.Options.Any(o => o.LocalImage == null));
            if(!unloaded.Any())
                return true;
            var imageTotal = unloaded.Select(v=>v.Options.Length).Sum();
            if (!CanAffordRequests(imageTotal))
            {
                var firstUnloaded = unloaded.FirstOrDefault();
                return false;
            }

            await Task.WhenAll(variants.SelectMany(v => v.Options.Select(o => o.LoadImage())));

            return true;
        }
    }

    public record RawCosmeticVariantChannel
    {
        public RawLocString VariantChannelName;
        public VariantOption[] GenericPropertyOptions;
        public VariantOption[] MaterialOptions;
        public VariantOption[] PartOptions;
        [JsonIgnore]
        public VariantOption[] Options => GenericPropertyOptions ?? MaterialOptions ?? PartOptions;

        public record struct VariantOption
        {
            public RawLocString VariantName;
            public PreviewImagePath PreviewImage;
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
                    return imageTexture = LoadLocalImageFromGamePath(PreviewImage.FormattedAssetPathName);
                }
            }

            public async Task LoadImage()
            {
                if (LocalImage is not null)
                    return;
                imageTexture = await LoadImageFromGamePath(PreviewImage.FormattedAssetPathName);
            }

            public record struct PreviewImagePath
            {
                public string AssetPathName;
                public string FormattedAssetPathName
                {
                    get
                    {
                        var noDot = AssetPathName[AssetPathName.IndexOf('.')..];
                        var splitBySlash = noDot.Split('/');
                        return $"FortniteGame/Plugins/GameFeatures/{splitBySlash[0]}/Content/{splitBySlash[1..].Join("/")}";
                    }
                }
                public string SubPathString;
            }
        }
    }

    public record struct RawLocString
    {
        public string localizedString;
    }
    #endregion
}
