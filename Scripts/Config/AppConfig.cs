using Godot;
using System;
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

    public delegate void ConfigChangeHandler(string section, string key, JsonValue value);
    public static event ConfigChangeHandler OnConfigChanged;

    //static ConfigFile configFile;
    const string configPath = "user://appConfig.json";
    static JsonObject _configData;
    static JsonObject ConfigData => _configData ??= LoadConfig();

    public static bool TryRead<T>(string path, out T value)
    {
        //reflection or source generation?...
        value = default;
        return false;
    }
    public static bool TryWrite<T>(string path, T value)
    {
        //reflection or source generation?...
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
        if(node is JsonValue val && val.GetValueKind()== newJVal.GetValueKind() && val.ToString()!= newJVal.ToString())
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
        var possibleVal = ConfigData[section]?[key]?.AsValue();
        if(possibleVal is JsonValue val && val.TryGetValue<T>(out var typedVal))
        {
            value = typedVal;
            return true;
        }
        return false;
    }

    public static T Get<T>(string section, string key, T fallback = default)
    {
        if (TryGet<T>(section, key, out var val))
            return val;
        return fallback;
    }

    public static void Set(string section, string key, AdaptiveJsonValue value, bool print = true)
    {
        ConfigData[section] ??= new JsonObject();
        ConfigData[section][key] = value.JsonValue;
        OnConfigChanged?.Invoke(section, key, ConfigData[section][key].AsValue());
        if (print)
            GD.Print($"Set Config ({section}:{key} = {value.JsonValue})");
        using var configFile = FileAccess.Open(configPath, FileAccess.ModeFlags.Write);
        configFile.StoreString(ConfigData.ToString());
    }

    public static void PreloadConfig() => _configData ??= LoadConfig();

    static JsonObject LoadConfig()
    {
        using var configFile = FileAccess.Open(configPath, FileAccess.ModeFlags.Read);
        if (configFile is not null)
        {
            //var configStructure = JsonSerializer.Deserialize<AppConfig>(configFile.GetAsText(), jsonOptions);
            //return JsonNode.Parse(JsonSerializer.Serialize(configStructure))?.AsObject();
            string fileContent = configFile.GetAsText();
            return JsonNode.Parse(fileContent)?.AsObject();
        }

        return [];
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
