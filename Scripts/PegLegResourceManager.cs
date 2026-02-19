using Godot;
using System;
using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using FileAccess = Godot.FileAccess;

public class PegLegResourceManager
{
    public const string packageFolderPath = "user://PegLegResourcePacks/";
    public const string resourcePath = "res://PegLegResources/";
    public const string fallbackResourcePath = "res://FallbackResources/";
    public const string overrideResourcePath = "user://CustomResources/";

    public static readonly string globalPackageFolderPath = Helpers.GlobalisePath(packageFolderPath);

    public static readonly Texture2D defaultIcon = ResourceLoader.Load<Texture2D>("res://Images/InterfaceIcons/T-Icon-Unknown-128.png");
    public static readonly BanjoSuppliments supplimentaryData = ResourceLoader.Load<BanjoSuppliments>("res://DataResources/BanjoSuppliments.tres");

    static FrozenDictionary<string, JsonObject> dataSources;
    public static FrozenDictionary<string, JsonObject> itemSources { get; private set; }
    
    //TODO: platform specific substitutions (Win64/Linux)
    static RegEx versionRegex;
    static RegEx VersionRegex
    {
        get
        {
            if(versionRegex is not null)
                return versionRegex;
            versionRegex = new();
            versionRegex.Compile($"^(?:PegLegResources\\-)?v(\\d+)\\.(\\d+)\\.(\\d+)(?:.pck)?$");
            return versionRegex;
        }
    }
    record struct PackageVersion(GithubHelper.ReleaseVersion version) : IComparable<PackageVersion>
    {
        public readonly int CompareTo(PackageVersion other) =>
            version.CompareTo(other.version);

        PackageVersion MajorBasis => this with { version = version with { minor = 0, patch = 0 } };
        PackageVersion MinorBasis => this with { version = version with { patch = 0 } };
        PackageVersion[] AllRequirements => [ MajorBasis, MinorBasis, this ];
        public PackageVersion[] Requirements => [.. AllRequirements.Distinct().Order()];

        public readonly string PackageFilename => $"PegLegResources-{version}.pck";
        public readonly string LocalPackagePath => globalPackageFolderPath + PackageFilename;

        public readonly bool HasLocalPackage() =>
            FileAccess.FileExists(LocalPackagePath);

        public void ClearOutdatedPackages()
        {
            using DirAccess packageDir = DirAccess.Open(globalPackageFolderPath);
            var files = packageDir.GetFiles().Where(f => f.EndsWith(".pck")).ToList();
            files.Remove("ExtraPatch.pck");
            files.Remove(PackageFilename);
            if (version.patch > 0)
                files.Remove(MinorBasis.PackageFilename);
            if(version.minor>0)
                files.Remove(MajorBasis.PackageFilename);
            foreach (var item in files)
            {
                packageDir.Remove(item);
            }
        }

        public void LoadAllPackages()
        {
            ProjectSettings.LoadResourcePack(LocalPackagePath, false);
            if (version.patch > 0)
                ProjectSettings.LoadResourcePack(MinorBasis.LocalPackagePath, false);
            if (version.minor > 0)
                ProjectSettings.LoadResourcePack(MajorBasis.LocalPackagePath, false);
        }

        public override string ToString() => version.ToString();
    }

    static bool hasLoadedResources = false;
    public static async Task FetchAndLoadPackages(int targetMajor, int targetMinor, Action<string, float> onProgress = null)
    {
        if (hasLoadedResources)
            return;
        onProgress?.Invoke("Checking for resource updates", -1);
        Dictionary<PackageVersion, GithubHelper.ReleaseAsset> releases = [];
        try
        {
            var releasesArray = await GithubHelper.FetchReleases("PegLegFN", "PegLegResourcePackager");
            releases = releasesArray?
                .Where(r => !r.prerelease || AppConfig.Get("advanced", "prerelease_resources", false))
                .ToDictionary(
                    r => new PackageVersion(r.TryGetVersion(out var v, VersionRegex) ? v : default), //todo: fix risk of duplicate key when failing version parse
                    r =>
                    {
                        if (OS.HasFeature("mobile"))
                            return r.assets.FirstOrDefault(a => a.name.StartsWith("PegLegResources-m-v"));
                        else
                            return r.assets.FirstOrDefault(a => a.name.StartsWith("PegLegResources-v"));
                    }
                );
        }
        catch
        {
            GD.PushWarning("Failed to fetch resource versions, resources may fail to load or be out of date");
            return;
        }

        var latestVersion = releases
            .Keys
            .Where(pv =>
                pv.version.major == targetMajor &&
                pv.version.minor == targetMinor
            )
            .OrderBy(pv => pv.version.patch)
            .LastOrDefault();
        if (latestVersion == default)
        {
            GD.PushWarning("No compatible versions found, did I time travel?");
            return;
        }
        GD.Print("Latest Pack Ver: " + latestVersion);
        if (!DirAccess.DirExistsAbsolute(globalPackageFolderPath))
            DirAccess.MakeDirAbsolute(globalPackageFolderPath);
        foreach (var requirement in latestVersion.Requirements)
        {
            if (requirement.HasLocalPackage())
                continue;
            if (!releases.TryGetValue(requirement, out var asset))
            {
                GD.PushWarning($"Version list is missing requirement \"{requirement}\"");
                continue;
            }
            WebHelpers.DownloadProgressHandle downloadProgress = new();
            downloadProgress.OnProgress += () => onProgress?.Invoke($"Downloading Resource Pack\n{requirement.version}", downloadProgress.ProgressPercent/100);
            using FileAccessStream fileStream = new(requirement.LocalPackagePath, FileAccess.ModeFlags.Write);
            await asset.DownloadTo(fileStream, downloadProgress);
        }
        await Helpers.WaitForFrame();
        if (FileAccess.FileExists(globalPackageFolderPath + "ExtraPatch.pck"))
            ProjectSettings.LoadResourcePack(globalPackageFolderPath + "ExtraPatch.pck", false);
        latestVersion.LoadAllPackages();
        latestVersion.ClearOutdatedPackages();
        onProgress?.Invoke("Loading Resources", -1);
        await Task.WhenAll(
            LoadDataSources(),
            LoadNamedItems()
        );
        hasLoadedResources = true;
    }

    //temporary until proper resource versioning system is ready
    public static async Task TempImportResources()
    {
        var tempPckPath = Helpers.GlobalisePath("res://PegLegResources.pck");
        if (FileAccess.FileExists(tempPckPath))
        {
            ProjectSettings.LoadResourcePack(tempPckPath, false);
        }
        await Task.WhenAll(
            LoadDataSources(),
            LoadNamedItems()
        );
    }

    static readonly string[] dataSourceNames = 
    [
        "AlterationLoadouts",
        "HeroStats",
        "ItemLevelsToXP",
        "ItemRatings",
        "MainQuestLines",
        "VenturesSeasons",
        "DifficultyInfo",
        "EventQuestLines",
        "ExpeditionCriteria",
    ];
    static async Task LoadDataSources()
    {
        ConcurrentDictionary<string, JsonObject> dataSourcesCC = [];
        var tasks = dataSourceNames.Select(name => Task.Run(() =>
        {
            dataSourcesCC.TryAdd(name, LoadResourceObj($"GameAssets/{name}.json"));
        }));
        await Task.WhenAll(tasks);
        dataSources = dataSourcesCC.ToFrozenDictionary(StringComparer.InvariantCultureIgnoreCase);
    }

    //todo: automate named item types by reading an index file generated by the exporter or the packager
    static readonly string[] itemTypeNames =
    [
        "Ability",
        "Accolades",
        "AccountResource",
        "Alteration",
        "Ammo",
        "CardPack",
        "CampaignHeroLoadout",
        "ConsumableAccountItem",
        "Defender",
        "Expedition",
        "Gadget",
        "GameplayModifier",
        "Hero",
        "HomebaseNode",
        "Ingredient",
        "MissionGen",
        "Quest",
        "Schematic",
        "TeamPerk",
        "Token",
        "Trap",
        "Weapon",
        "Worker",
        "WorkerPortrait",
        "WorldItem",
        "ZoneTheme",
        "PersonalVehicle",
    ];

    static async Task LoadNamedItems()
    {
        GD.Print("loading items");
        ConcurrentDictionary<string, GameItemTemplate> namedItemsCC = [];
        var tasks = itemTypeNames.Select(name => {
            var curName = name;
            return Task.Run(() =>
            {
                var itemData = LoadResourceObj($"GameAssets/NamedItems/{curName}.json", false).DetachAll();
                GD.Print($"loaded \"GameAssets/NamedItems/{curName}.json\", with {itemData.Length} items");
                foreach (var kvp in itemData)
                {
                    namedItemsCC.TryAdd(kvp.Key, new(kvp.Value.AsObject()));
                }
            });
        });
        await Task.WhenAll(tasks);
        GameItemTemplate.SetImportedTemplates(namedItemsCC.ToFrozenDictionary(StringComparer.InvariantCultureIgnoreCase));
    }

    static bool hasPreloaded;
    public static async Task PreloadTemplateTextures(Action<string, float> onProgress = null)
    {
        if (hasPreloaded)
            return;
        int templatesProcessed = 0;
        int templatesPerFrame = 0;
        var templates = GameItemTemplate.GetTemplates().ToArray();
        int templatesTotal = templates.Length;

        if (templatesTotal == 0)
        {
            GD.Print("No templates???");
            return;
        }
        GD.Print("begin loading template textures");
        foreach (var template in templates)
        {
            template.GetTexture();
            templatesPerFrame--;
            templatesProcessed++;
            onProgress?.Invoke("Caching Textures", (float)templatesProcessed / templatesTotal);
            if (templatesPerFrame < 0)
            {
                await Helpers.WaitForFrame();
                templatesPerFrame = OS.HasFeature("mobile") ? 40 : 60;
            }
        }
        hasPreloaded = true;
        GD.Print($"loaded {templatesProcessed} template textures");
    }

    public static bool ResourceExists(string resource, bool allowOverrides = true)
    {
        return true;
    }

    static string[] externalResourceExclusions = 
    [
        "Themes/builtin_blank/"
    ];

    public static string[] LoadThemeList()
    {
        List<string> themeList = [];
        if(DirAccess.DirExistsAbsolute(fallbackResourcePath + "Themes"))
        {
            using var fallbackDir = DirAccess.Open(fallbackResourcePath + "Themes");
            var dirs = fallbackDir.GetDirectories();
            themeList.AddRange(dirs);
        }
        var indexedThemes = LoadResourceArray<string>("Themes/builtinThemeIndex.json", false).Select(s => "builtin_" + s);
        themeList.AddRange(indexedThemes);
        if(DirAccess.DirExistsAbsolute(overrideResourcePath + "Themes"))
        {
            using var overrideDir = DirAccess.Open(overrideResourcePath + "Themes");
            var dirs = overrideDir.GetDirectories();
            themeList.AddRange(dirs);
        }
        return [.. themeList.Distinct()];
    }

    public static FileAccess LoadResourceFile(string resource, bool allowOverrides = true, bool onlyOverride = false)
    {
        bool exclude = externalResourceExclusions.Any(resource.StartsWith);
        if (allowOverrides && !exclude && FileAccess.FileExists(overrideResourcePath + resource))
        {
            return FileAccess.Open(overrideResourcePath + resource, FileAccess.ModeFlags.Read);
        }
        if (onlyOverride)
            return null;
        if (!exclude && FileAccess.FileExists(resourcePath + resource))
        {
            return FileAccess.Open(resourcePath + resource, FileAccess.ModeFlags.Read);
        }
        //GD.Print("fallback file: " + fallbackResourcePath + resource);
        return FileAccess.Open(fallbackResourcePath + resource, FileAccess.ModeFlags.Read);
    }

    public static T LoadResourceObj<T>(string resource, bool allowOverrides = true, JsonSerializerOptions options = null) where T : class
    {
        using var standardFile = LoadResourceFile(resource, allowOverrides);
        var text = standardFile.GetAsText();
        try
        {
            return JsonSerializer.Deserialize<T>(text, options);
        }
        catch(Exception e)
        {
            JsonNode node = JsonNode.Parse(text);
            GD.PushError(e);
        }
        return null;
    }

    public static T[] LoadResourceArray<T>(string resource, bool allowOverrides = true)
    {
        bool exclude = externalResourceExclusions.Any(resource.StartsWith);
        List<T> list = null;
        using (var standardFile = LoadResourceFile(resource, false))
        {
            try
            {
                list = [.. JsonSerializer.Deserialize<T[]>(standardFile.GetAsText())];
            }
            catch(JsonException e)
            {
                GD.PushWarning(e.Message);
            }
            catch { }
        }
        list ??= [];
        if (LoadResourceFile(resource, allowOverrides, true) is FileAccess overrideFile)
        {
            using var file = overrideFile;
            try
            {
                list.AddRange(JsonSerializer.Deserialize<T[]>(file.GetAsText()));
            }
            catch (JsonException e)
            {
                GD.Print($"Failed to load override for \"{resource}\",\n{e.Message}");
            }
            catch { }
        }
        return [.. list];
    }
    public static Dictionary<string, T> LoadResourceDict<T>(string resource, bool allowOverrides = true)
    {
        bool exclude = externalResourceExclusions.Any(resource.StartsWith);
        Dictionary<string, T> dict = null;
        using (var standardFile = LoadResourceFile(resource, false))
        {
            try
            {
                dict = JsonSerializer.Deserialize<Dictionary<string, T>>(standardFile.GetAsText());
            }
            catch (JsonException e)
            {
                GD.PushWarning(e.Message);
            }
            catch { }
        }
        dict ??= [];
        if (LoadResourceFile(resource, allowOverrides, true) is FileAccess overrideFile)
        {
            using var file = overrideFile;
            try
            {
                var overrides = JsonSerializer.Deserialize<Dictionary<string, T>>(file.GetAsText());
                dict = dict
                    .Where(kvp => !overrides.ContainsKey(kvp.Key))
                    .Union(overrides)
                    .ToDictionary();
            }
            catch (JsonException e)
            {
                GD.Print($"Failed to load override for \"{resource}\",\n{e.Message}");
            }
            catch { }
        }
        return dict;
    }

    public static JsonObject LoadResourceObj(string resource, bool allowOverrides = true)
    {
        bool exclude = externalResourceExclusions.Any(resource.StartsWith);
        string standardPath = !exclude && FileAccess.FileExists(resourcePath + resource) ? (resourcePath + resource) : (fallbackResourcePath + resource);
        JsonObject jObj = null;
        using (var standardFile = FileAccess.Open(standardPath, FileAccess.ModeFlags.Read))
        {
            try
            {
                jObj = JsonNode.Parse(standardFile.GetAsText()).AsObject();
            }
            catch { }
        }
        jObj ??= [];
        if (LoadResourceFile(resource, allowOverrides, true) is FileAccess overrideFile)
        {
            using var file = overrideFile;
            try
            {
                var overrides = JsonNode.Parse(file.GetAsText()).AsObject();
                GD.Print($"{overrides.Count} overrides exist for \"{resource}\"");
                foreach (var kvp in overrides.ToArray())
                {
                    jObj[kvp.Key] = overrides.DetachNode(kvp.Key);
                }
            }
            catch { }
        }
        return jObj;
    }
    public static JsonArray LoadResourceArray(string resource, bool allowOverrides = true)
    {
        bool exclude = externalResourceExclusions.Any(resource.StartsWith);
        string standardPath = !exclude && FileAccess.FileExists(resourcePath + resource) ? (resourcePath + resource) : (fallbackResourcePath + resource);
        JsonArray array = [];
        using (var standardFile = FileAccess.Open(standardPath, FileAccess.ModeFlags.Read))
        {
            try
            {
                array = JsonNode.Parse(standardFile.GetAsText()).AsArray();
            }
            catch { }
        }
        array ??= [];
        if (LoadResourceFile(resource, allowOverrides, true) is FileAccess overrideFile)
        {
            using var file = overrideFile;
            try
            {
                var toAdd = JsonNode.Parse(file.GetAsText()).AsArray().DetachAll();
                foreach (var node in toAdd)
                {
                    array.Add(node);
                }
            }
            catch { }
        }
        return array;
    }

    public static T LoadResourceAsset<T>(string resource, bool cache = false) where T : Resource
    {
        bool exclude = externalResourceExclusions.Any(resource.StartsWith);
        //if (allowOverrides && !exclude && FileAccess.FileExists(overrideResourcePath + resource))
        //{
        //    //todo: handle importing external resources and caching with weakrefs
        //    //return null;
        //}
        if (!exclude && ResourceLoader.Exists(resourcePath + resource))
        {
            return ResourceLoader.Load<T>(resourcePath + resource);
        }
        if (ResourceLoader.Exists(fallbackResourcePath + resource))
        {
            return ResourceLoader.Load<T>(fallbackResourcePath + resource);
        }
        //missing game asset textures are expected when using fallback resources
        if (!typeof(T).IsSubclassOf(typeof(Texture)) || !resource.StartsWith("GameAssets/"))
        {
            GD.PushWarning($"Asset not found: \"{resource}\"");
        }
        return null;
    }

    public static JsonObject AlterationLoadouts => dataSources?["AlterationLoadouts"];
    public static JsonObject HeroStats => dataSources?["HeroStats"];
    public static JsonObject ItemLevelsToXP => dataSources?["ItemLevelsToXP"];
    public static JsonObject ItemRatings => dataSources?["ItemRatings"];
    public static JsonObject MainQuestLines => dataSources?["MainQuestLines"];
    public static JsonObject VenturesSeasons => dataSources?["VenturesSeasons"];
    public static JsonObject DifficultyInfo => dataSources?["DifficultyInfo"];
    public static JsonObject EventQuestLines => dataSources?["EventQuestLines"];
    public static JsonObject ExpeditionCriteria => dataSources?["ExpeditionCriteria"];

    static JsonObject magicNumbers;
    public static JsonObject MagicNumbers => magicNumbers ??= LoadResourceObj("magicNumbers.json") ?? [];

    //public static bool TryGetDataSource(string dataType, out JsonObject source)
    //{
    //    bool exists = dataSources.ContainsKey(dataType);
    //    if (!exists && TryLoadJsonFile(dataType, out var json))
    //    {
    //        dataSources[dataType] = source = json;
    //        return true;
    //    }
    //    source = exists ? dataSources[dataType] : null;
    //    return exists;
    //}

    //public static bool TryGetItemSource(string itemType, out JsonObject source)
    //{
    //    bool exists = itemSources.ContainsKey(itemType);
    //    if (!exists && TryLoadJsonFile("NamedItems/" + itemType, out var json))
    //    {
    //        itemSources[itemType] = source = json;
    //        return true;
    //    }
    //    source = exists ? itemSources[itemType] : null;
    //    return exists;
    //}

    //public static Texture2D GetReservedTexture(string texturePath)
    //{
    //    if (texturePath is null)
    //        return null;
    //    if (iconCache.ContainsKey(texturePath) && iconCache[texturePath].GetRef().Obj is Texture2D cachedTexture)
    //        return cachedTexture;

    //    string filePath = $"{banjoFolderPath}/{texturePath}";
    //    if(!FileAccess.FileExists(filePath))
    //    {
    //        //GD.PushWarning($"Missing Image file: {Helpers.ProperlyGlobalisePath(fullPath)}");
    //        return null;
    //    }
    //    Texture2D loadedTexture = ImageTexture.CreateFromImage(Image.LoadFromFile(filePath));
    //    //Texture2D loadedTexture = ResourceLoader.Load<Texture2D>(fullPath);
    //    iconCache[texturePath] = GodotObject.WeakRef(loadedTexture);

    //    return loadedTexture;
    //}

    //static bool TryLoadJsonFile(string fileName, out JsonObject json)
    //{
    //    json = null;
    //    string filePath = $"{banjoFolderPath}/{fileName}.json";
    //    if (!FileAccess.FileExists(filePath))
    //        return false;
    //    using FileAccess fileAccessor = FileAccess.Open(filePath, FileAccess.ModeFlags.Read);
    //    json = JsonNode.Parse(fileAccessor.GetAsText(), new() { PropertyNameCaseInsensitive = true }).AsObject();
    //    //GD.Print(fileName + " file loaded");
    //    return true;
    //}
}

public static class HeroStats
{
    public const string MaxHealth = "FortHealthSet.MaxHealth";
    public const string MaxShields = "FortHealthSet.Shield";
    public const string HealthRegenRate = "FortRegenHealthSet.HealthRegenRate";
    public const string ShieldRegenRate = "FortRegenHealthSet.ShieldRegenRate";
    public const string AbilityDamage = "FortDamageSet.OutgoingBaseAbilityDamageMultiplier";
    public const string HealingModifier = "FortHealthSet.HealingSourceBaseMultiplier";
}

public static class SurvivorBonus
{
    public const string MaxHealth = "IsFortitudeLow";
    public const string MaxShields = "IsResistanceLow";
    public const string ShieldRegenRate = "IsShieldRegenLow";

    public const string RangedDamage = "IsRangedDamageLow";
    public const string MeleeDamage = "IsMeleeDamageLow";
    public const string AbilityDamage = "IsAbilityDamageLow";
    public const string TrapDamage = "IsTrapDamageLow";

    public const string TrapDurability = "IsTrapDurabilityHigh";
}

public class GameItemTemplate
{
    #region Static Values

    static Texture2D goldLlama = ResourceLoader.Load<Texture2D>("res://Images/Llamas/PinataGold.png", "Texture2D");

    public static string[] rarityIds = new string[]
    {
        null,
        "C",
        "UC",
        "R",
        "VR",
        "SR",
        "UR"
    };

    public static string[] tierIds = new string[]
    {
        "T00",
        "T01",
        "T02",
        "T03",
        "T04",
        "T05",
    };

    public static readonly Color[] rarityColours = new Color[]
    {
        Colors.Transparent,
        Color.FromString("#bfbfbf", Colors.White),
        Color.FromString("#83db00", Colors.White),
        Color.FromString("#008bf1", Colors.White),
        Color.FromString("#a952ff", Colors.White),
        Color.FromString("#ff7b3d", Colors.White),
        Color.FromString("#ffff40", Colors.White),
    };

    static readonly string[] cardPackFromRarity = new string[]
    {
        "CardPack:cardpack_choice_all_r",
        "CardPack:cardpack_choice_all_r",
        "CardPack:cardpack_choice_all_r",
        "CardPack:cardpack_choice_all_r",
        "CardPack:cardpack_choice_all_vr",
        "CardPack:cardpack_choice_all_sr",
    };

    #endregion

    #region Static Methods

    static FrozenDictionary<string, GameItemTemplate> importedTemplates = null;
    public static void SetImportedTemplates(FrozenDictionary<string, GameItemTemplate> newImportedTemplates) =>
        importedTemplates = newImportedTemplates;

    static ConcurrentDictionary<string, GameItemTemplate> customTemplates = [];

    public static GameItemTemplate Get(string templateId)
    {
        if (templateId is null || templateId.Count(c => c == ':') != 1)
            return null;

        if (templateId.StartsWith("STWAccoladeReward"))
            templateId = templateId.Replace("STWAccoladeReward:stwaccolade_", "Accolades:accoladeid_stw_");

        if (templateId == "AccountResource:currency_mtxswap")
            templateId = "AccountResource:currency_hybrid_mtx_xrayllama";

        if (customTemplates.TryGetValue(templateId, out var custom))
            return custom;

        if (importedTemplates?.TryGetValue(templateId, out var imported) ?? false)
            return imported;

        return null;
    }

    public static GameItemTemplate GetOrCreate(string templateId, Func<GameItemTemplate> constructor)
    {
        if (templateId is null || templateId.Count(c => c == ':') != 1)
            return null;

        if (Get(templateId) is GameItemTemplate foundTemplate)
            return foundTemplate;

        GameItemTemplate newTemplate = constructor();

        if (newTemplate is not null)
            lock (customTemplates)
            {
                bool exists = customTemplates.TryAdd(newTemplate.TemplateId, newTemplate);
                return exists ? customTemplates[newTemplate.TemplateId] : newTemplate;
            }

        return null;
    }

    public static IEnumerable<GameItemTemplate> GetTemplates()
    {
        return importedTemplates?.Union(customTemplates)?.Select(kvp => kvp.Value);
    }

    //probably pretty performance heavy, use sparingly
    public static IEnumerable<GameItemTemplate> GetTemplatesOfType(string templateType, Func<GameItemTemplate, bool> filter = null) =>
        importedTemplates?
        .Where(kvp =>
            kvp.Key.StartsWith(templateType + ":") &&
            (filter is null || filter(kvp.Value)
        ))?
        .Union(customTemplates
            .Where(kvp =>
                kvp.Key.StartsWith(templateType + ":") &&
                (filter is null || filter(kvp.Value)
            ))
        )?
        .Select(kvp => kvp.Value) ?? Array.Empty<GameItemTemplate>();

    public static Texture2D GetSubtypeTexture(string key, Texture2D fallbackIcon = null)
    {
        key ??= "";
        var dict = PegLegResourceManager.supplimentaryData.ItemTypeAndSubtypeIcons;
        if (dict.TryGetValue(key, out Texture2D value))
            return value;
        return fallbackIcon;
    }

    #endregion

    public GameItemTemplate(JsonObject rawData)
    {
        isReal = true;
        this.rawData = rawData;
    }

    public GameItemTemplate(string templateId = "Custom:item", string displayName = "Custom Item", string description = null, string iconPath = null, JsonObject extraData = null)
    {
        extraData ??= [];
        var splitTemplateId = templateId.Split(":");
        extraData["Type"] = splitTemplateId[0];
        extraData["Name"] = splitTemplateId[1];
        if (displayName is not null)
            extraData["DisplayName"] = displayName;
        if (description is not null)
            extraData["Description"] = description;
        if (iconPath is not null)
            extraData["ImagePaths"] = new JsonObject() { ["LargePreview"] = iconPath };
        rawData = extraData;
    }

    public bool isReal { get; private set; }
    public JsonObject rawData { get; private set; }
    public JsonNode this[string propertyName] => rawData[propertyName];
    public bool ContainsKey(string propertyName) => rawData.ContainsKey(propertyName);
    public string TemplateId => $"{Type}:{Name.ToLower()}";
    public bool VBucksOrXRayTickets => Type == "AccountResource" && Name.ToLower() is string lowername && (
            lowername == "currency_hybrid_mtx_xrayllama" ||
            lowername == "currency_mtxswap" ||
            lowername == "currency_xrayllama"
        );

    public string Type
    {
        get
        {
            //var type = rawData.TryGetPropertyValue("Type", out var typeNode) ? typeNode.ToString() : null;
            var type = rawData["Type"]?.ToString();
            if (type is null)
            {
                GD.Print("WOAH NELLY");
                return "";
            }
            return type;
        }
    }

    public bool IsCollectable => Type switch
    {
        "Hero" or "Worker" or "Defender" or "Schematic" => true,
        _ => false
    };
    public bool CanBeLeveled => Tier > 0 && Type switch
    {
        "Hero" or "Worker" or "Weapon" or "Trap" => true,
        "Schematic" => !Unrecyclable || Category != "Trap",
        "Defender" => !Unrecyclable || RarityLevel > 1,
        _ => false
    };
    public bool CanBeUnseen=> Type switch
    {
        "Hero" or "Worker" or "Defender" or "Schematic" or "Quest" or "AccountResource" or "ConsumableAccountItem" or "CardPack" => true,
        _ => false
    };
    public bool CanBeFavourited => Type switch
    {
        "Hero" or "Worker" or "Defender" or "Schematic" or "AccountResource"=> true,
        _ => false
    };

    public string CollectionProfile => Type == "Schematic" ? FnProfileTypes.SchematicCollection : FnProfileTypes.PeopleCollection;
    public string Name => rawData["Name"].ToString();
    public string DisplayName => rawData["DisplayName"]?.ToString();
    public string SortingDisplayName => DisplayName.StartsWith("The ") ? DisplayName[4..] : DisplayName;
    public string Description => rawData["Description"]?.ToString();
    public string Category => rawData["Category"]?.ToString();
    public string SubType => rawData["SubType"]?.ToString();
    public string Rarity => rawData["Rarity"]?.ToString();
    public int RarityLevel => (Rarity ?? "").ConvertRarityString();
    public Color RarityColor => Name.StartsWith("ZCP_") ? Colors.Transparent : rarityColours[RarityLevel];

    public int Tier => rawData["Tier"]?.GetValue<int>() ?? 0;
    //public int Tier => rawData["Tier"] is JsonValue tierVal ? (tierVal.TryGetValue<int>(out var tier) ? tier : 0) : 0;
    public string Personality => rawData["Personality"]?.ToString();

    public bool Unrecyclable => rawData["RecycleRecipe"] is null;
    public bool Undismantlable => rawData["DismantleResults"] is null;

    AlterationSlot[] alterationSlots;
    public AlterationSlot[] AlterationSlots => alterationSlots ??= AlterationSlot.SlotsFromRow(
        rawData["AlterationLoadoutRow"]?.ToString(), 
        rawData["AlterationNamedExclusions"]?.Deserialize<string[]>() ?? []
    );

    FrozenSet<string> heroTags = null;
    public FrozenSet<string> HeroTags => heroTags ??= [.. rawData["HeroTags"]?.Deserialize<string[]>() ?? []];

    public Texture2D GetTexture(FnItemTextureType textureType = FnItemTextureType.Preview, bool largePreview = false) => GetTexture(textureType, PegLegResourceManager.defaultIcon, largePreview);
    public Texture2D GetTexture(Texture2D fallbackIcon, bool largePreview = false) => GetTexture(FnItemTextureType.Preview, fallbackIcon, largePreview);

    Dictionary<FnItemTextureType, Texture2D> persistantTextureCache = [];
    public Texture2D GetTexture(FnItemTextureType textureType, Texture2D fallbackIcon, bool largePreview = false)
    {
        if (persistantTextureCache.TryGetValue(textureType, out var cachedTex) && (!largePreview || textureType != FnItemTextureType.Preview))
            return cachedTex;

        if ((Type == "TeamPerk" ||  Type == "Ability") && textureType == FnItemTextureType.Preview)
            textureType = FnItemTextureType.Icon;

        if(Type == "Worker" &&
            (
                rawData["ImagePaths"]?
                ["SmallPreview"]?
                .ToString()
                .Contains("GenericWorker") ?? false
            ))
            return GetSubtypeTexture(SubType ?? "Survivor", fallbackIcon);

        if 
        (
            Type == "CardPack" && 
            textureType == FnItemTextureType.Preview && 
            DisplayName.Contains("Legendary") && 
            DisplayName.Contains("Llama") && 
            !Name.StartsWith("ZCP_")
        )
            return goldLlama;

        if (!TryGetTexturePath(out var texturePath, out var wasLargePreview, textureType, largePreview))
            return fallbackIcon;
        var loadedTex = PegLegResourceManager.LoadResourceAsset<Texture2D>("GameAssets/" + texturePath);
        if (loadedTex is not null && !wasLargePreview)
            persistantTextureCache[textureType] = loadedTex;
        return loadedTex ?? fallbackIcon;
    }

    public bool TryGetTexturePath(out string foundPath, FnItemTextureType textureType = FnItemTextureType.Preview) =>
        TryGetTexturePath(out foundPath, out _, textureType, false);
    

    public bool TryGetTexturePath(out string foundPath, out bool wasLargePreview, FnItemTextureType textureType, bool preferLargePreview)
    {
        foundPath = null;
        wasLargePreview = false;
        JsonObject imagePaths = rawData["ImagePaths"]?.AsObject();
        if (imagePaths is null)
            return false;

        if (textureType == FnItemTextureType.Preview)
        {
            if (preferLargePreview)
            {
                wasLargePreview = imagePaths["LargePreview"] is not null;
                foundPath = (imagePaths["LargePreview"] ?? imagePaths["SmallPreview"])?.ToString();
            }
            else
                foundPath = (imagePaths["SmallPreview"] ?? imagePaths["LargePreview"])?.ToString();
        }
        else
            foundPath = imagePaths[textureType.ToString()]?.ToString();

        if (string.IsNullOrWhiteSpace(foundPath) || !foundPath.StartsWith("ExportedImages"))
            return false;
        return true;
    }

    public Texture2D GetSubtypeTexture(Texture2D fallbackIcon = null)
    {
        switch (Type)
        {
            case "Schematic":
                if (Category == "Trap")
                    return GetSubtypeTexture("Trap", fallbackIcon);
                else
                    return GetSubtypeTexture(SubType, fallbackIcon);
            case "Worker":
                if (rawData["ImagePaths"]?["SmallPreview"]?.ToString().Contains("GenericWorker") ?? false)
                    return null;
                else
                    return GetSubtypeTexture(SubType ?? "Survivor", fallbackIcon);
            case "Trap":
                return GetSubtypeTexture("Trap", fallbackIcon);
            default:
                return GetSubtypeTexture(SubType, fallbackIcon);
        }
    }

    public GameItemTemplate TryGetNextRarity()
    {
        if (rawData["RarityUpRecipe"]?["Result"]?.ToString() is string rarityUpResult)
            return Get(rarityUpResult);
        return null;
    }

    public GameItemTemplate TryGetNextTier()
    {
        if (rawData["TierUpRecipe"]?["Result"]?.ToString() is string tierUpResult)
            return Get(tierUpResult);
        return null;
    }

    public Texture2D GetAmmoTexture(Texture2D fallbackIcon = null)
    {
        if (Type != "Schematic" && Type != "Weapon" && Type != "Trap")
            return fallbackIcon;

        if (Category == "Trap" || Type == "Trap")
            return GetSubtypeTexture(SubType, fallbackIcon);

        if (
            rawData["RangedWeaponStats"]?["AmmoType"]?.ToString() is string ammoType && 
            PegLegResourceManager.supplimentaryData.AmmoIcons.TryGetValue(ammoType.Split(" ")[0], out Texture2D value)
            )
            return value;

        return fallbackIcon;
    }

    public string GetCompactRarityAndTier(int givenTier = 0)
    {
        var rarityId = rarityIds[RarityLevel];
        var tierId = givenTier <= 0 ? tierIds[Tier] : tierIds[givenTier];
        return rarityId + "_" + tierId;
    }

    GameItemTemplate[] heroAbilities;
    public GameItemTemplate[] GetHeroAbilities()
    {
        if (Type != "Hero")
            return null;
        return heroAbilities ??=
        [
            Get(rawData["HeroPerkTemplate"]?.ToString()),
            Get(rawData["CommanderPerkTemplate"]?.ToString()),
            Get(rawData["HeroAbilities"]?[0].ToString()),
            Get(rawData["HeroAbilities"]?[1].ToString()),
            Get(rawData["HeroAbilities"]?[2].ToString()),
        ];
    }

    GameItemTemplate teamPerk;
    public GameItemTemplate GetTeamPerk()
    {
        if (Type != "Hero")
            return null;
        return teamPerk ??= Get(rawData["UnlocksTeamPerk"]?.ToString());
    }

    GameItem[] questRewards;
    GameItem[] visibleQuestRewards;
    GameItem[] hiddenQuestRewards;
    public GameItem[] GetQuestRewards()
    {
        if (Type != "Quest")
            return null;
        return questRewards ??= [.. GetVisibleQuestRewards().Union(GetHiddenQuestRewards())];
    }

    public GameItem[] GetVisibleQuestRewards()
    {
        if (Type != "Quest")
            return null;
        return visibleQuestRewards ??= GenerateQuestRewards(false);
    }

    public GameItem[] GetHiddenQuestRewards()
    {
        if (Type != "Quest")
            return null;
        return hiddenQuestRewards ??= GenerateQuestRewards(true);
    }

    GameItem[] GenerateQuestRewards(bool hidden)
    {
        var allRewards = rawData["Rewards"]
            .AsArray()
            .Where(r => r["Hidden"].GetValue<bool>() == hidden);

        var rewards = allRewards
            .Where(r => !r["Selectable"].GetValue<bool>())
            .Select(r => Get(r["Item"].ToString())?.CreateInstance(r["Quantity"].GetValue<int>()))
            .Where(r => r is not null)
            .ToList();

        var dynamicRewards = allRewards
            .Where(r => r["Selectable"].GetValue<bool>());

        if (dynamicRewards.Any())
        {
            //fake a cardpack to show a choice reward
            var cardpackID = cardPackFromRarity[dynamicRewards.Select(q => Get(q["Item"]?.ToString())?.RarityLevel ?? 0).Max()];
            JsonObject attributes = new()
            {
                ["options"] = new JsonArray([.. dynamicRewards.Select(r => new JsonObject()
                {
                    ["itemType"] = r["Item"].ToString(),
                    ["attributes"] = new JsonObject(),
                    ["quantity"] = r["Quantity"].GetValue<int>()
                })]),
                ["quest_selectable"] = true
            };
            var choiceReward = Get(cardpackID).CreateInstance(1, attributes);
            rewards.Insert(0, choiceReward);
        }
        return [.. rewards];
    }

    CommanderRequirement? commanderReq;
    public bool PerkCompatibleWithCommander(GameItemTemplate commanderTemplate, out string warning)
    {
        warning = null;
        if (commanderTemplate?.Type != "Hero" || (Type != "Hero" && Type != "TeamPerk"))
            return false;
        commanderReq ??= rawData[Type == "TeamPerk" ? "CommanderRequirement" : "HeroPerkRequirement"]?
            .Deserialize<CommanderRequirement>(Helpers.JsonOptions.Fields);
        if (commanderReq?.IsMatch(commanderTemplate) != false)
            return true;
        warning = commanderReq?.Description;
        return false;
    }

    TeamPerkSupportRequirements? teamperkReq;
    public bool TeamPerkBoostedByHero(GameItemTemplate heroTemplate)
    {
        if (heroTemplate?.Type != "Hero" || Type != "TeamPerk")
            return false;
        teamperkReq ??= rawData["SupportRequirements"]?
            .Deserialize<TeamPerkSupportRequirements>(Helpers.JsonOptions.Fields);
        if (teamperkReq?.IsMatch(heroTemplate) == true)
            return true;
        return false;
    }

    public int TeamPerkMinRequirements => (teamperkReq ??= rawData["SupportRequirements"]?.Deserialize<TeamPerkSupportRequirements>(Helpers.JsonOptions.Fields)).Value.MinimumQuantity;

    struct CommanderRequirement
    {
#pragma warning disable CS0649 //Field is never assigned to, and will always have its default value
        public string Description;
        public string[] CommanderTag;
        public string CommanderSubType;
#pragma warning restore CS0649 //Field is never assigned to, and will always have its default value

        public bool IsMatch(GameItemTemplate template)
        {
            if (template is null)
                return false;
            if (CommanderSubType is not null && template.SubType != CommanderSubType)
                return false;
            else if (CommanderTag is not null)
            {
                var targetTags = CommanderTag.ToHashSet();
                var commanderTags = template["HeroTags"]?.Deserialize<string[]>().ToHashSet();
                if (targetTags.All(t => !commanderTags.Contains(t)))
                    return false;
            }

            return true;
        }
    }

    struct TeamPerkSupportRequirements()
    {
#pragma warning disable CS0649 //Field is never assigned to, and will always have its default value
        public string Description;
        public int MinimumQuantity = 1;
        public string[] HeroTags;
        public string HeroSubType;
        public int? MinimumTier;
        public string MinimumRarity;
#pragma warning restore CS0649 //Field is never assigned to, and will always have its default value

        public bool IsMatch(GameItemTemplate template)
        {
            if (template is null)
                return false;

            if (HeroSubType is not null && template.SubType != HeroSubType)
                return false;

            if (HeroTags is not null && HeroTags.Length > 0)
            {
                var targetTags = HeroTags.ToHashSet();
                var heroTags = template.HeroTags;
                if (targetTags.All(t => !heroTags.Contains(t)))
                    return false;
            }

            if (MinimumTier is int tier && template.Tier < tier)
                return false;

            if (MinimumRarity is not null && template.RarityLevel < MinimumRarity.ConvertRarityString())
                return false;

            return true;
        }
    }

    public struct AlterationSlot
    {
        public string[] options;
        public string[] OptionsForLevel(int level) => [.. options.Select(o => o.EndsWith("_t01") ? $"{o[..^4]}_t0{level}" : o)];
        public int requiredLevel;
        public string requiredRarity;
        public int RequiredRarityLevel => requiredRarity.ConvertRarityString();
        
        public static AlterationSlot[] SlotsFromRow(string alterationSlotRow, string[] exclusions = null)
        {
            if (alterationSlotRow is null)
                return [];
            var row = PegLegResourceManager.AlterationLoadouts[alterationSlotRow].AsArray();
            var exclusionSet = (exclusions ?? []).ToHashSet();
            return [..row?
                .Select(slot => new AlterationSlot()
                {
                    options = [..slot["RawAlterations"]
                        .AsArray()
                        .Where(a => !exclusionSet.Overlaps(a["ExclusionNames"].Deserialize<string[]>()))
                        .Select(a => a["AID"].ToString())
                    ],
                    requiredLevel = slot["RequiredLevel"].GetValue<int>(),
                    requiredRarity = slot["RequiredRarity"].ToString(),
                })
            ];
        }
    }

    public JsonArray GenerateSearchTags(bool assumeUncommon = true)
    {
        if(rawData["searchTags"] is JsonArray existingSearchTags)
            return existingSearchTags;

        List<string> tags =
        [
            DisplayName,
            //Description,
            Rarity ?? (assumeUncommon ? "Uncommon" : null),
            Type,
            SubType,
            Category,
            Personality?[2..]
        ];

        if(GetHeroAbilities() is GameItemTemplate[] abilities)
        {
            foreach (var ability in abilities)
            {
                if (!ability?.DisplayName?.EndsWith('+') ?? false)
                {
                    tags.Add(ability.DisplayName);
                    //tags.Add(ability.Description);
                }
            }
        }
        if(GetTeamPerk() is GameItemTemplate teamPerk)
            tags.Add(teamPerk.DisplayName);

        if (tags.Contains("Worker"))
            tags.Add("Survivor");
        if (rawData["RecycleRecipe"] is null)
            tags.Add("Permanent");
        var searchTags = new JsonArray(tags.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => (JsonNode)t).ToArray());
        lock (rawData)
        {
            rawData["RarityLv"] = RarityLevel;
            rawData["searchTags"] = searchTags;
        }
        return searchTags;
    }

    public GameItem CreateInstance(int quantity = 1, JsonObject attributes = null, GameItem inspectorOverride = null, JsonObject customData = null)
    {
        customData ??= [];
        customData["generated_by_pegleg"] = true;
        return new(this, quantity, attributes, inspectorOverride, customData);
    }

    public GameItem PriceForItem()
    {
        int quantity = Type switch
        {
            "Hero" => Rarity switch
            {
                "Mythic" => 3200,
                "Legendary" => 2800,
                "Epic" => 1000,
                _ => 100
            },
            "Schematic" => Rarity switch
            {
                "Legendary" => 1680,
                "Epic" => 600,
                _ => 100
            },
            _ => 100
        };
        return Get("AccountResource:eventcurrency_scaling").CreateInstance(quantity);
    }

    public GameOffer CreateOffer(GameItem price = null, int quantity = 1, int limit = 1, JsonObject rawData = null) =>
        GameOffer.CreateFake([CreateInstance(quantity)], price ?? PriceForItem(), limit, rawData);
}

public enum FnItemTextureType
{
    Preview,
    Icon,
    LoadingScreen,
    PackImage,

    Personality,
    SetBonus
}

class DataTable
{
    readonly Dictionary<string, DataTableCurve> curves = [];

    public DataTable(string filepath)
    {
        if (!FileAccess.FileExists(filepath))
            return;
        using FileAccess dataTableFile = FileAccess.Open(filepath, FileAccess.ModeFlags.Read);
        var curveJsonMap = JsonNode.Parse(dataTableFile.GetAsText())[0]["Rows"].AsObject();

        foreach (var curveKvp in curveJsonMap)
        {
            //GD.Print(survivorCurveKvp.Key);
            curves[curveKvp.Key] = new(curveKvp.Value.AsObject());
        }
    }
    public bool ContainsKey(string key) => curves.ContainsKey(key);
    public DataTableCurve this[string key] => curves[key];
}

public class DataTableCurve
{
    public readonly List<double> times = [];
    public readonly List<double> values = [];
    double minTime = 0;
    double maxTime = 0;

    public DataTableCurve(string filepath, string curveKey)
    {
        using FileAccess dataTableFile = PegLegResourceManager.LoadResourceFile(filepath, false);
        if (dataTableFile is null)
            return;

        var curveJsonMap = JsonNode.Parse(dataTableFile.GetAsText())[0]["Rows"].AsObject();

        if (!curveJsonMap.ContainsKey(curveKey))
            return;

        var dataTableCurveJson = curveJsonMap[curveKey].AsObject();

        var keysArray = dataTableCurveJson["Keys"].AsArray();

        minTime = keysArray[0]["Time"].GetValue<double>();
        maxTime = keysArray[^1]["Time"].GetValue<double>();

        foreach (var curvePointKey in keysArray)
        {
            times.Add(curvePointKey["Time"].GetValue<double>());
            values.Add(curvePointKey["Value"].GetValue<double>());
        }
    }

    public DataTableCurve(JsonObject dataTableCurveJson)
    {
        var keysArray = dataTableCurveJson["Keys"].AsArray();
        minTime = keysArray[0]["Time"].GetValue<double>();
        maxTime = keysArray[^1]["Time"].GetValue<double>();

        foreach (var curvePointKey in keysArray)
        {
            times.Add(curvePointKey["Time"].GetValue<double>());
            values.Add(curvePointKey["Value"].GetValue<double>());
        }
    }

    public static DataTableCurve LoadHomebaseRatingMap()
    {
        using FileAccess dataTableFile = PegLegResourceManager.LoadResourceFile("GameAssets/HomebaseRatingMap.json", false);
        if (dataTableFile is null)
            return new();

        var keysArray = JsonNode.Parse(dataTableFile.GetAsText()).AsArray();

        DataTableCurve toReturn = new()
        {
            minTime = keysArray[0]["Key"].GetValue<double>(),
            maxTime = keysArray[^1]["Key"].GetValue<double>()
        };

        foreach (var curvePointKey in keysArray)
        {
            toReturn.times.Add(curvePointKey["Key"].GetValue<double>());
            toReturn.values.Add(curvePointKey["Value"].GetValue<double>());
        }
        return toReturn;
    }

    DataTableCurve(){}

    public double Sample(double time)
    {
        if (time < minTime)
        {
            //handle pre-infinity
            return values[0];
        }
        if (time > maxTime)
        {
            //handle post-infinity
            return values[^1];
        }

        // higher/lower search for time range
        int GetClosestTimeIndexFloored(int fromIndex, int toIndex, double time)
        {
            if (toIndex - fromIndex < 3)
            {
                toIndex = Mathf.Clamp(toIndex, 0, times.Count);
                while (time <= times[toIndex] && toIndex>0)
                    toIndex--;
                return toIndex;
            }

            int middleIndex = Mathf.CeilToInt((toIndex - fromIndex) * 0.5f) + fromIndex;

            if (time == times[middleIndex])
                return middleIndex;
            else if (time > times[middleIndex])
                return GetClosestTimeIndexFloored(middleIndex, toIndex, time);
            else
                return GetClosestTimeIndexFloored(fromIndex, middleIndex, time);
        }

        int lowerIndex = GetClosestTimeIndexFloored(0, times.Count - 1, time);

        double lowerTime = times[lowerIndex];
        double upperTime = times[lowerIndex + 1];

        double betweenTimeBlend = (time - lowerTime) / (upperTime - lowerTime);

        double lowerValue = values[lowerIndex];
        double upperValue = values[lowerIndex + 1];

        return lowerValue + ((upperValue - lowerValue) * betweenTimeBlend);
    }
    public double SampleInverse(double value)
    {
        if (value < values[0])
        {
            //handle pre-infinity
            return minTime;
        }
        if (value > values[^1])
        {
            //handle post-infinity
            return maxTime;
        }

        // higher/lower search for time range
        int GetClosestValueIndexFloored(int fromIndex, int toIndex, double value)
        {
            if (toIndex - fromIndex < 3)
            {
                toIndex = Mathf.Clamp(toIndex, 0, values.Count);
                while (value <= values[toIndex] && toIndex > 0)
                    toIndex--;
                return toIndex;
            }

            int middleIndex = Mathf.CeilToInt((toIndex - fromIndex) * 0.5f) + fromIndex;

            if (value == values[middleIndex])
                return middleIndex;
            else if (value > values[middleIndex])
                return GetClosestValueIndexFloored(middleIndex, toIndex, value);
            else
                return GetClosestValueIndexFloored(fromIndex, middleIndex, value);
        }

        int lowerIndex = GetClosestValueIndexFloored(0, times.Count - 1, value);

        double lowerVal = values[lowerIndex];
        double upperVal = values[lowerIndex + 1];

        double betweenValBlend = (value - lowerVal) / (upperVal - lowerVal);

        double lowerTime = times[lowerIndex];
        double upperTime = times[lowerIndex + 1];

        return lowerTime + ((upperTime - lowerTime) * betweenValBlend);
    }
}