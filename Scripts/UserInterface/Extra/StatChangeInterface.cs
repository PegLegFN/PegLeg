using Godot;
using System.Text.Json;
using System.Threading.Tasks;
using System.Linq;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

public partial class StatChangeInterface : Control
{
    [Export]
    Button importMeleeBtn;
    [Export]
    Button clearMeleeBtn;
    [Export]
    Button importRangedBtn;
    [Export]
    Button clearRangedBtn;

    [Export]
    PackedScene statWeaponScene;
    [Export]
    Control statWeaponParent;
    [Export]
    Control emptyChanges;
    [Export]
    SubViewport screenshotViewport;
    [Export]
    StatChangeWeapon screenshotWeapon;

    public override void _Ready()
    {
        GetWindow().FilesDropped += HandleDrop;
        clearMeleeBtn.Visible = false;
        clearRangedBtn.Visible = false;
        emptyChanges.Visible = false;
    }

    public override void _ExitTree()
    {
        GetWindow().FilesDropped -= HandleDrop;
    }

    Dictionary<string, Dictionary<string, StatChange>> meleeStatChanges;
    Dictionary<string, Dictionary<string, StatChange>> rangedStatChanges;

    List<StatChangeWeapon> statWeapons = [];

    void HandleDrop(string[] files)
    {
        if (files.Length != 1 || !IsVisibleInTree())
            return;
        if (files[0].EndsWith("MeleeWeapons.json"))
            ImportMelee(files[0]);
        else if (files[0].EndsWith("RangedWeapons.json"))
            ImportRanged(files[0]);
    }

    bool isLoading = false;
    async void ImportMelee(string filePath)
    {
        if (!IsVisibleInTree() || isLoading)
            return;
        try
        {
            isLoading = true;
            meleeStatChanges = await Compare(filePath);
            await UpdateList();
        }
        finally
        {
            isLoading = false;
        }
    }

    async void ClearMelee()
    {
        if (!IsVisibleInTree() || isLoading)
            return;
        try
        {
            isLoading = true;
            meleeStatChanges = null;
            await UpdateList();
        }
        finally
        {
            isLoading = false;
        }
    }

    async void ImportRanged(string filePath)
    {
        if (!IsVisibleInTree() || isLoading)
            return;
        try
        {
            isLoading = true;
            rangedStatChanges = await Compare(filePath);
            await UpdateList();
        }
        finally
        {
            isLoading = false;
        }
    }

    async void ClearRanged()
    {
        if (!IsVisibleInTree() || isLoading)
            return;
        try
        {
            isLoading = true;
            rangedStatChanges = null;
            await UpdateList();
        }
        finally
        {
            isLoading = false;
        }
    }

    async Task<Dictionary<string, Dictionary<string, StatChange>>> Compare(string filePath)
    {
        Dictionary<string, Dictionary<string, float>> rows = [];
        using (var statFile = FileAccess.Open(filePath, FileAccess.ModeFlags.Read))
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
            return [];
        ConcurrentDictionary<string, Dictionary<string, StatChange>> itemChanges = [];
        await Parallel.ForEachAsync(rows, (r, ct) =>
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
        });
        //GD.Print(JsonSerializer.Serialize(itemChanges));
        return itemChanges.ToDictionary();
    }

    async Task UpdateList()
    {
        importMeleeBtn.Visible = meleeStatChanges is null;
        clearMeleeBtn.Visible = !importMeleeBtn.Visible;
        importRangedBtn.Visible = rangedStatChanges is null;
        clearRangedBtn.Visible = !importRangedBtn.Visible;

        bool noChanges = meleeStatChanges is not null || rangedStatChanges is not null;
        if (meleeStatChanges is not null)
            noChanges &= meleeStatChanges.Count == 0;
        if (rangedStatChanges is not null)
            noChanges &= rangedStatChanges.Count == 0;
        emptyChanges.Visible = noChanges;

        using var progressToken = LoadingOverlay.CreateToken("Comparing stats...");
        var groupedChanges = (meleeStatChanges ?? []).Union(rangedStatChanges ?? [])
            .GroupBy(kvp =>
            {
                var groups = WeaponGroupNameRegex().Match(kvp.Key).Groups;
                if (!groups.TryGetValue("key", out var keyCapture))
                    return "";
                var rarityAddon = groups.ContainsKey("rarity") ? "{r}" : "";
                var coreAddon = groups.ContainsKey("evotype") ? "{c}" : "";
                var tierAddon = groups.ContainsKey("tier") ? "{t}" : "";
                return keyCapture.Captures.FirstOrDefault().Value + rarityAddon + coreAddon + tierAddon;
            }
            )
            .OrderBy(kvp => kvp.Key)
            .ToArray();
        progressToken.SetLoadingProgress(0, groupedChanges.Length);
        for (int i = 0; i < groupedChanges.Length; i++)
        {
            if (statWeapons.Count <= i)
            {
                var newNode = statWeaponScene.Instantiate<StatChangeWeapon>();
                statWeaponParent.AddChild(newNode);
                statWeapons.Add(newNode);
            }
            var weaponNode = statWeapons[i];
            weaponNode.Visible = true;
            weaponNode.SetStatChanges(groupedChanges[i].ToDictionary(), groupedChanges[i].Key);
            await Helpers.WaitForFrame();
            progressToken.IncrementLoadingProgress();
        }
        for (int i = groupedChanges.Length; i < statWeapons.Count; i++)
        {
            statWeapons[i].Visible = false;
        }
    }


    [GeneratedRegex("""
            ^
            (?<key>.+?)?                                # key
            (?:_(?<rarity>C|UC|R|VR|SR|UR))?            # rarity
            (?:_(?<evotype>Ore|Crystal))?               # weapon core
            (?:_?T(?<tier>\d+))?                        # tier
            $
            """, RegexOptions.Singleline | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.IgnorePatternWhitespace)]
    private static partial Regex WeaponGroupNameRegex();
}

public record struct StatChange
{
    [JsonInclude]
    public float from;
    [JsonInclude]
    public float to;
}
