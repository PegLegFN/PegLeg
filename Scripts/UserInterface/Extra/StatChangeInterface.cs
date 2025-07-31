using Godot;
using System.Text.Json;
using System.Threading.Tasks;
using System.Linq;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Text.Json.Serialization;

public partial class BalanceCheckerInterface : Control
{
    public override void _Ready()
    {
        GetWindow().FilesDropped += Compare;
    }

    public override void _ExitTree()
    {
        GetWindow().FilesDropped -= Compare;
    }

    Dictionary<string, Dictionary<string, StatChange>> meleeStatChanges;
    Dictionary<string, Dictionary<string, StatChange>> rangedStatChanges;

    bool isLoading = false;
    async void Compare(string[] files)
    {
        if (files.Length != 1 || !IsVisibleInTree() || isLoading)
            return;
        if (!files[0].EndsWith("MeleeWeapons.json") && !files[0].EndsWith("RangedWeapons.json"))
            return;
        try
        {
            isLoading = true;
            Dictionary<string, Dictionary<string, float>> rows = [];
            using (var statFile = FileAccess.Open(files[0], FileAccess.ModeFlags.Read))
            {
                var doc = JsonDocument.Parse(statFile.GetAsText());
                foreach (var jRow in doc.RootElement[0].GetProperty("Rows").EnumerateObject())
                {
                    Dictionary<string, float> row = [];
                    foreach (var jStat in jRow.Value.EnumerateObject())
                    {
                        if (jStat.Value.ValueKind == JsonValueKind.Number)
                            row.Add(jStat.Name, (float)jStat.Value.GetDouble());
                    }
                    rows.Add(jRow.Name, row);
                }
            }
            var weaponMap = GameItemTemplate
                .GetTemplatesOfType("Weapon")
                .Where(t => t["RawWeaponStatRow"] is not null)
                .DistinctBy(t => t["RawWeaponStatRow"]?.ToString())
                .ToDictionary(t => t["RawWeaponStatRow"]?.ToString());
            GD.Print($"Rows: {rows?.Count}");
            if (rows.Count == 0)
                return;
            using var progressToken = LoadingOverlay.CreateToken("Comparing stats...", 0, rows.Count-2);
            ConcurrentDictionary<string, Dictionary<string, StatChange>> itemChanges = [];
            await Parallel.ForEachAsync(rows, (r, ct) =>
            {
                try
                {
                    if (!weaponMap.TryGetValue(r.Key, out var template))
                        return ValueTask.CompletedTask;
                    var weaponStats = template["RawWeaponStats"].Deserialize<Dictionary<string, float>>();
                    Dictionary<string, StatChange> changes = [];
                    foreach (var kvp in weaponStats)
                    {
                        if (!r.Value.TryGetValue(kvp.Key, out var newStat) || kvp.Value == newStat)
                            continue;
                        changes.Add(kvp.Key, new() { from = kvp.Value, to = newStat });
                    }
                    if (changes.Count > 0)
                        itemChanges.TryAdd(template.TemplateId, changes);
                    return ValueTask.CompletedTask;
                }
                finally
                {
                    progressToken.IncrementLoadingProgress();
                }
            });
            GD.Print(JsonSerializer.Serialize(itemChanges));
        }
        finally
        {
            isLoading = false;
        }
    }
}

public record struct StatChange
{
    [JsonInclude]
    public float from;
    [JsonInclude]
    public float to;
}
