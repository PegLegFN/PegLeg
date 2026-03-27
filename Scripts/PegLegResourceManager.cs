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
	public static event Action OnResourcesLoaded;

	static FrozenDictionary<string, JsonObject> dataSources;
	public static FrozenDictionary<string, JsonObject> itemSources { get; private set; }

	//TODO: platform specific substitutions (Win64/Linux)
	static RegEx versionRegex;
	static RegEx VersionRegex
	{
		get
		{
			if (versionRegex is not null)
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
		PackageVersion[] AllRequirements => [MajorBasis, MinorBasis, this];
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
			if (version.minor > 0)
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
			downloadProgress.OnProgress += () => onProgress?.Invoke($"Downloading Resource Pack\n{requirement.version}", downloadProgress.ProgressPercent / 100);
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
		OnResourcesLoaded?.Invoke();
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
		var tasks = itemTypeNames.Select(name =>
		{
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
		//int concurrentTemplates = 0;
		var templates = GameItemTemplate.GetTemplates().ToArray();
		int templatesTotal = templates.Length;
		float templateIncrement = 1f / templatesTotal;

		if (templatesTotal == 0)
		{
			GD.Print("No templates???");
			return;
		}
		GD.Print("begin loading template textures");
		void PrintProgress(float assetProgress) => onProgress?.Invoke("Caching Textures", ((float)templatesProcessed / templatesTotal));

		List<Task> concurrentTemplateTasks = [];

		PrintProgress(0);
		foreach (var template in templates)
		{
			//await template.GetTextureAsync(onProgress: PrintProgress);
			concurrentTemplateTasks.Add(template.GetTextureAsync());
			//template.GetTexture();
			//concurrentTemplates--;
			templatesProcessed++;
			//if (concurrentTemplates < 0)
			//{
			//    await Helpers.WaitForFrame();
			//    concurrentTemplates = OS.HasFeature("mobile") ? 40 : 60;
			//    PrintProgress(0);
			//}
			if (concurrentTemplateTasks.Count >= (OS.HasFeature("mobile") ? 500 : 500))
			{
				await Task.WhenAll(concurrentTemplateTasks);
				concurrentTemplateTasks.Clear();
				PrintProgress(0);
			}
		}

		await Task.WhenAll(concurrentTemplateTasks);

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
		if (DirAccess.DirExistsAbsolute(fallbackResourcePath + "Themes"))
		{
			using var fallbackDir = DirAccess.Open(fallbackResourcePath + "Themes");
			var dirs = fallbackDir.GetDirectories();
			themeList.AddRange(dirs);
		}
		var indexedThemes = LoadResourceArray<string>("Themes/builtinThemeIndex.json", false).Select(s => "builtin_" + s);
		themeList.AddRange(indexedThemes);
		if (DirAccess.DirExistsAbsolute(overrideResourcePath + "Themes"))
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
		catch (Exception e)
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
			catch (JsonException e)
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

	public static async Task<T> LoadResourceAssetAsync<T>(string resource, Action<float> onProgress = null, bool cache = false) where T : Resource
	{
		bool exclude = externalResourceExclusions.Any(resource.StartsWith);
		//if (allowOverrides && !exclude && FileAccess.FileExists(overrideResourcePath + resource))
		//{
		//    //todo: handle importing external resources and caching with weakrefs
		//    //return null;
		//}
		if (!exclude && ResourceLoader.Exists(resourcePath + resource))
		{
			return await LoadResourceAsyncFromPath<T>(resourcePath + resource, onProgress);
		}
		if (ResourceLoader.Exists(fallbackResourcePath + resource))
		{
			return await LoadResourceAsyncFromPath<T>(fallbackResourcePath + resource, onProgress);
		}
		//missing game asset textures are expected when using fallback resources
		if (!typeof(T).IsSubclassOf(typeof(Texture)) || !resource.StartsWith("GameAssets/"))
		{
			GD.PushWarning($"Asset not found: \"{resource}\"");
		}
		return null;
	}

	static async Task<T> LoadResourceAsyncFromPath<T>(string fullPath, Action<float> onProgress) where T : Resource
	{
		var reqErr = ResourceLoader.LoadThreadedRequest(fullPath, useSubThreads: true);
		if (reqErr != Error.Ok)
		{
			GD.PushWarning($"Async asset request failure: {reqErr} (\"{fullPath}\")");
			return null;
		}

		Godot.Collections.Array progress = [];
		var stage = ResourceLoader.LoadThreadedGetStatus(fullPath, progress);
		while (stage == ResourceLoader.ThreadLoadStatus.InProgress)
		{
			onProgress?.Invoke((float)progress[0]);
			await Helpers.WaitForFrame();
			stage = ResourceLoader.LoadThreadedGetStatus(fullPath);
		}

		if (stage != ResourceLoader.ThreadLoadStatus.Loaded)
		{
			GD.PushWarning($"Async asset load failure: {stage} (\"{fullPath}\")");
			return null;
		}
		onProgress?.Invoke(1);
		return (T)ResourceLoader.LoadThreadedGet(fullPath);
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

	DataTableCurve() { }

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
				while (time <= times[toIndex] && toIndex > 0)
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