using Godot;
using Json.Path;
using System.Text.Json;
using System.Text.Json.Nodes;

public partial class AppConfig
{
	public AdvancedConfig advanced = new();
	public partial class AdvancedConfig
	{
		public bool beastmode;
		public bool developer;
		public bool offsetRefresh;
	}

	public ExperimentalConfig experimental = new();
	public partial class ExperimentalConfig { }

	public delegate void ConfigChangeHandler(string section, string key, JsonNode value);
	public static event ConfigChangeHandler OnConfigChanged;

	//static ConfigFile configFile;
	const string configPath = "user://appConfig.json";
	static JsonObject _configData;
	static JsonObject ConfigData => _configData ??= LoadConfig();

	public static GithubHelper.ReleaseVersion PegLegVersion
	{
		get
		{
			GithubHelper.ReleaseVersion currentVer = default;
			string[] verData = ProjectSettings.GetSetting("application/config/version").AsString().Split(".");
			if (verData.Length == 3)
			{
				int betaAndPatch = int.Parse(verData[2]);
				int patch = betaAndPatch / 1000;
				int beta = betaAndPatch % 1000;

				//release version
				currentVer = new(
					int.Parse(verData[0]),
					int.Parse(verData[1]),
					betaAndPatch
				);
			}
			return currentVer;
		}
	}

	static bool TryGetNodeNew(string path, out JsonNode node)
	{
		node = null;

		if (!JsonPath.TryParse(path, out var pathData) || !pathData.IsSingular)
			return false;
		var results = pathData.Evaluate(ConfigData);
		if (!results.Matches.TryGetSingleValue(out var result))
			return false;

		node = result;
		return true;
	}

	public static bool TryRead<T>(string path, out T value)
	{
		value = default;
		//reflection or source generation?...

		if (!TryGetNodeNew(path, out var node))
			return false;

		if (node is T tNode)
		{
			value = tNode;
			return true;
		}

		if (node.Deserialize<T>() is not T result)
			return false;

		value = result;
		return true;
	}

	public static bool TryWrite<T>(string path, T value)
	{
		//reflection or source generation?...

		if (!JsonPath.TryParse(path, out var pathData) || !pathData.IsSingular)
			return false;

		return false;
	}

	public static bool TryGetValue<T>(string path, out T value)
	{
		value = default;
		return TryGetNode(path, out JsonNode val) && val is JsonValue jval && jval.TryGetValue(out value);
	}

	public static bool TryGetNode(string path, out JsonNode node) =>
		ConfigData.TryGetNodeFromPath(path, out node);

	public static void TrySet(string path, AdaptiveJsonValue newVal, bool print = false)
	{
		var newJVal = newVal.JsonValue;
		if (!TryGetNode(path, out var node))
			return;
		if (node is JsonValue val && val.GetValueKind() == newJVal.GetValueKind() && val.ToString() != newJVal.ToString())
		{
			if (node.Parent is JsonArray)
				node.Parent[node.GetElementIndex()] = newJVal;
			else
				node.Parent[node.GetPropertyName()] = newJVal;
			//write to file
			if (print)
				GD.Print($"Set Config ({path} = {newJVal})");
			//worth applying schema before serialising?
			using var configFile = FileAccess.Open(configPath, FileAccess.ModeFlags.Write);
			configFile.StoreString(ConfigData.ToString());
		}
	}

	public static bool TryGet<T>(string section, string key, out T value)
	{
		value = default;
		LoadConfig();
		var possibleNode = ConfigData[section ?? ""]?[key ?? ""];
		if (possibleNode is not JsonValue val || val.TryGetValue<T>(out var typedVal) != true)
			return false;
		value = typedVal;
		return true;
	}

	public static bool TryDeserialise<T>(string section, string key, out T value)
	{
		value = default;
		LoadConfig();
		var possibleNode = ConfigData[section ?? ""]?[key ?? ""];
		if (possibleNode is null)
			return false;
		try
		{
			value = possibleNode.Deserialize<T>();
			return true;
		}
		catch
		{
			return false;
		}
	}

	public static T Get<T>(string section, string key, T fallback = default)
	{
		if (TryGet<T>(section, key, out var val))
			return val;
		if (TryDeserialise<T>(section, key, out var deserialised))
			return deserialised;
		return fallback;
	}

	public static void Set(string section, string key, AdaptiveJsonValue value, bool print = true)
	{
		ConfigData[section] ??= new JsonObject();
		ConfigData[section][key] = value.JsonValue;
		OnConfigChanged?.Invoke(section, key, ConfigData[section][key]);
		if (print)
			GD.Print($"Set Config ({section}:{key} = {value.JsonValue})");
		using var configFile = FileAccess.Open(configPath, FileAccess.ModeFlags.Write);
		configFile.StoreString(ConfigData.ToString());
	}

	public static void SetSerialised<T>(string section, string key, T value, bool print = true, bool printValue = false)
	{
		var serialisedValue = JsonSerializer.SerializeToNode(value);
		ConfigData[section] ??= new JsonObject();
		ConfigData[section][key] = serialisedValue;
		OnConfigChanged?.Invoke(section, key, ConfigData[section][key]);
		if (print)
			GD.Print($"Set Config ({section}:{key} = {(printValue ? serialisedValue : "<Object>")})");
		using var configFile = FileAccess.Open(configPath, FileAccess.ModeFlags.Write);
		configFile.StoreString(ConfigData.ToString());
	}

	public static void Clear(string section, string key, bool print = true)
	{
		var data = (ConfigData[section] ??= new JsonObject()).AsObject();
		if (!data.ContainsKey(key))
			return;
		data.Remove(key);
		OnConfigChanged?.Invoke(section, key, null);
		if (print)
			GD.Print($"Cleared Config ({section}:{key}");
		using var configFile = FileAccess.Open(configPath, FileAccess.ModeFlags.Write);
		configFile.StoreString(ConfigData.ToString());
	}

	static readonly string[] webhookMigrations = ["dailySummary", "missionArchive", "powerHour"];
	public static void MigrateAndPreloadConfig()
	{
		_configData ??= LoadConfig();
		if (_configData is null)
			return;

		//migrate notable mission filter
		if (TryGet("missions", "notable_filter", out string notableFilter))
		{
			foreach (var a in GameAccount.OwnedAccounts)
			{
				if (a.GetLocalData("notable_mission_filter") is null)
					a.SetLocalData("notable_mission_filter", notableFilter);
			}
			Set("missions", "lite_notable_filter", notableFilter);
			Clear("missions", "notable_filter");
		}

		//migrate auto recycle filter
		if (TryGet("automation", "recycle_filter", out string recycleFilter))
		{
			foreach (var a in GameAccount.OwnedAccounts)
			{
				if (a.GetLocalData("RecycleFilter") is null)
					a.SetLocalData("RecycleFilter", recycleFilter);
			}
			Clear("automation", "recycle_filter");
		}

		if(TryGet("advanced", "webhooks", out bool oldWebhookState))
		{
			foreach (var shareType in webhookMigrations)
			{
				//enable publishing if webhooks were previously enabled for that type
				if (Get("webhooks", $"{shareType}_enabled", false))
				{
					Set("publishing", $"{shareType}_enabled", true);

					//disable the webhook portion if useSync was previously active
					if(Get("webhooks", $"{shareType}_useSync", false))
						Set("webhooks", $"{shareType}_enabled", false);
				}

				Clear("webhooks", $"{shareType}_useSync");
				Clear("webhooks", $"{shareType}_sync");
			}
			Set("advanced", "publishing", oldWebhookState);
			Clear("advanced", "webhooks");
		}
	}

	static JsonObject LoadConfig()
	{
		using var configFile = FileAccess.Open(configPath, FileAccess.ModeFlags.Read);
		if (configFile?.GetError() != Error.Ok)
		{
			GD.PushWarning($"config load failed: {configFile?.GetError()}");
			return [];
		}
		//var configStructure = JsonSerializer.Deserialize<AppConfig>(configFile.GetAsText(), jsonOptions);
		//return JsonNode.Parse(JsonSerializer.Serialize(configStructure))?.AsObject();
		string fileContent = configFile.GetAsText();
		return JsonNode.Parse(fileContent)?.AsObject();
	}

	public readonly struct AdaptiveJsonValue
	{
		public JsonValue JsonValue { get; private init; }
		public AdaptiveJsonValue(JsonValue val)
		{
			JsonValue = val;
		}
		public static implicit operator AdaptiveJsonValue(bool value) => new(JsonValue.Create(value));
		public static implicit operator AdaptiveJsonValue(int value) => new(JsonValue.Create(value));
		public static implicit operator AdaptiveJsonValue(float value) => new(JsonValue.Create(value));
		public static implicit operator AdaptiveJsonValue(double value) => new(JsonValue.Create(value));
		public static implicit operator AdaptiveJsonValue(string value) => new(JsonValue.Create(value));
	}
}
