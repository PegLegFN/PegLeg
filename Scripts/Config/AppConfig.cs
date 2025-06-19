using Godot;
using System;
using System.Text.Json.Nodes;

public static class AppConfig
{
    public delegate void ConfigChangeHandler(string section, string key, JsonValue value);
    public static event ConfigChangeHandler OnConfigChanged;

    //static ConfigFile configFile;
    const string configPath = "user://appConfig.json";
    static JsonObject configData;

    public static T Get<T>(string section, string key, T fallback = default)
    {
        LoadConfig();
        var possibleVal = configData[section]?[key]?.AsValue();
        if (possibleVal is JsonValue val)
            return val.TryGetValue<T>(out var typedVal) ? typedVal : fallback;
        return fallback;
    }

    public static void Set(string section, string key, AdaptiveJsonValue value, bool print = true)
    {
        LoadConfig();
        configData[section] ??= new JsonObject();
        configData[section][key] = value.JsonValue;
        OnConfigChanged?.Invoke(section, key, configData[section][key].AsValue());
        if (print)
            GD.Print($"Set Config ({section}:{key} = {value.JsonValue})");
        using var configFile = FileAccess.Open(configPath, FileAccess.ModeFlags.Write);
        configFile.StoreString(configData.ToString());
    }

    public static void Clear(string section, string key)
    {
        LoadConfig();
        configData[section] ??= new JsonObject();
        if (configData[section].AsObject().ContainsKey(key))
            configData[section].AsObject().Remove(key);
        OnConfigChanged?.Invoke(section, key, null);

        GD.Print($"Removed Config ({section}:{key})");
        using var configFile = FileAccess.Open(configPath, FileAccess.ModeFlags.Write);
        configFile.StoreString(configData.ToString());
    }

    public static void Clear(string section)
    {
        LoadConfig();
        if (configData.ContainsKey(section))
        {
            var oldSection = configData[section].AsObject();
            configData.Remove(section);
            foreach (var kvp in oldSection)
            {
                OnConfigChanged?.Invoke(section, kvp.Key, null);
            }
        }

        GD.Print($"Removed Config Section ({section})");
        using var configFile = FileAccess.Open(configPath, FileAccess.ModeFlags.Write);
        configFile.StoreString(configData.ToString());
    }

    static void LoadConfig()
    {
        if (configData is not null)
            return;
        using var configFile = FileAccess.Open(configPath, FileAccess.ModeFlags.Read);
        if (configFile is not null)
            configData = JsonNode.Parse(configFile.GetAsText())?.AsObject();
        configData ??= [];
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
