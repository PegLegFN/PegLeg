using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using XmppDotNet.Xmpp.XHtmlIM;

public partial class MissionRewardsController : Control, IRecyclableElementProvider<MissionRewardPair>
{
    [Signal]
    public delegate void HasVBucksEventHandler(bool value);
    [Export]
    RecycleListContainer missionList;
    [Export]
    Control loadingIcon;
    [Export]
    LineEdit searchBar;
    [Export]
    LineEdit itemSearchBar;
    [Export]
    Button sortByPower;
    [Export]
    Button filterPower;
    [Export]
    Control filterPowerContainer;
    [Export]
    Button filterStory;
    [Export]
    Control emptyContent;
    [Export]
    TextureRect emptyIcon;
    [Export]
    CheckBox multiFilterToggle;
    [Export]
    bool notableMode;

    [Export]
	CheckButton[] rarityFilters;
    [Export]
    CheckButton[] zoneFilters;
    [Export]
    CheckButton[] typeFilters;

    [Export]
    CheckButton[] repeatabilityFilters;

    CheckButton[] allFilters;

    List<MissionRewardPair> rewards = [];


    public MissionRewardPair GetRecycleElement(int index) => index>=0 && index<rewards.Count ? rewards[index] : default;

    public int GetRecycleElementCount() => rewards.Count;

    public async void ReloadMissions()
    {
        //if (Input.IsKeyPressed(Key.Shift))
        //{
        //    await GameMission.ReparseMissions();
        //}
        //else
            await GameMission.UpdateMissions();
    }

    public override void _Ready()
	{
        missionList.SetProvider(this);
        allFilters = rarityFilters.Union(zoneFilters).Union(typeFilters).Where(f => f is not null).ToArray();
        SetupFilters(rarityFilters);
        SetupFilters(zoneFilters);
        SetupFilters(typeFilters);
        if (sortByPower is not null)
            sortByPower.Toggled += _ => FilterMissions();
        if (filterPower is not null)
            filterPower.Toggled += _ => FilterMissions();
        if (filterPowerContainer is not null)
            filterPowerContainer.Visible = GameAccount.ActiveAccount.isOwned;
        if (filterStory is not null)
            filterStory.Toggled += _ => FilterMissions();
        if(searchBar is not null)
        {
            searchBar.TextChanged += _ => UpdateSearch();
        }
        if(itemSearchBar is not null)
        {
            itemSearchBar.TextChanged += _ => UpdateSearch();
        }
        foreach (var filter in repeatabilityFilters)
        {
            if (lockFilter)
                return;
            filter.Toggled += _ => FilterMissions();
        }
        GameMission.OnMissionsUpdated += FilterMissions;
        GameMission.OnMissionsInvalidated += ClearMissions;
        GameAccount.ActiveAccountChanged += OnAccountChanged;
        GameAccount.RemindersChanged += FilterMissions;
        GameAccount.LocalDataChanged += OnAccountDataChanged;
        AppConfig.OnConfigChanged += OnConfigChanged;
        VisibilityChanged += TryRefresh;
        FilterMissions();
    }

    public override void _ExitTree()
    {
        GameMission.OnMissionsUpdated -= FilterMissions;
        GameMission.OnMissionsInvalidated -= ClearMissions;
        GameAccount.ActiveAccountChanged -= OnAccountChanged;
        GameAccount.RemindersChanged -= FilterMissions;
        GameAccount.LocalDataChanged -= OnAccountDataChanged;
        AppConfig.OnConfigChanged -= OnConfigChanged;
    }

    private void OnAccountDataChanged(string key)
    {
        if (key == "notable_mission_filter" && notableMode)
            FilterMissions();
    }

    private void OnAccountChanged()
    {
        if (filterPowerContainer is not null)
            filterPowerContainer.Visible = GameAccount.ActiveAccount.isOwned;
        FilterMissions();
    }

    private void OnConfigChanged(string section, string key, JsonValue value)
    {
        if (section != "missions")
            return;
        if (key == "lite_notable_filter" && notableMode)
            FilterMissions();
        if (key == "notable_count" && notableMode)
            FilterMissions();
    }

    void SetupFilters(CheckButton[] filters)
    {
        foreach (var filter in filters)
        {
            var current = filter;
            current.Toggled += newVal =>
            {
                if (lockFilter)
                    return;
                if (Input.IsKeyPressed(Key.Alt))
                {
                    GD.Print("Cur : " + current.Name);
                    foreach (var item in filters)
                    {
                        GD.Print(item.Name + ": " + item.ButtonPressed);
                    }
                }
                if (Input.IsKeyPressed(Key.Alt) && newVal)
                    TurnOnSelectedFilters(filters, current);
                else if (!Input.IsKeyPressed(Key.Shift) && newVal && !(multiFilterToggle?.ButtonPressed ?? false))
                    TurnOffSelectedFilters(filters, current);
                FilterMissions();
            };
        }
    }


    public void TurnOffFilters()
    {
        lockFilter = true;
        foreach (var filter in allFilters)
        {
            filter.ButtonPressed = false;
        }
        foreach (var filter in repeatabilityFilters)
        {
            filter.ButtonPressed = false;
        }
        lockFilter = false;
        FilterMissions();
    }

    bool lockFilter = false;

    void TurnOffSelectedFilters(IEnumerable<CheckButton> onlyThese, CheckButton exceptThis = null)
    {
        lockFilter = true;
        foreach (var filter in onlyThese)
        {
            filter.ButtonPressed = filter == exceptThis;
        }
        lockFilter = false;
    }
    void TurnOnSelectedFilters(IEnumerable<CheckButton> onlyThese, CheckButton exceptThis = null)
    {
        lockFilter = true;
        foreach (var filter in onlyThese)
        {
            filter.ButtonPressed = filter != exceptThis;
        }
        lockFilter = false;
    }

    void TryRefresh()
    {
        if (needsRefresh)
            FilterMissions();
    }

    PLSearch.Instruction[] missionSearchInstructions = [];
    PLSearch.Instruction[] itemSearchInstructions = [];
    void UpdateSearch()
    {
        missionSearchInstructions = PLSearch.GenerateSearchInstructions(searchBar?.Text ?? "") ?? [];
        itemSearchInstructions = PLSearch.GenerateSearchInstructions(itemSearchBar?.Text ?? "") ?? [];
        //var searchText = searchBar?.Text ?? "";
        //if (searchText.Contains("///"))
        //{
        //    string[] splitSearchText = searchText.Split("///");
        //    missionSearchInstructions = PLSearch.GenerateSearchInstructions(splitSearchText[0]) ?? [];
        //    itemSearchInstructions = PLSearch.GenerateSearchInstructions(splitSearchText[1..].Join()) ?? [];
        //}
        //else
        //{
        //    missionSearchInstructions = [];
        //    itemSearchInstructions = PLSearch.GenerateSearchInstructions(searchText) ?? [];
        //}
        FilterMissions();
    }

    public static Func<GameItem, bool> CreateNotableFilter()
    {
        var notableFilterText = GameAccount.ActiveAccount.isOwned ?
            GameAccount.ActiveAccount.GetLocalData("notable_mission_filter")?.ToString() ?? "" :
            AppConfig.Get("missions", "lite_notable_filter", "");
        if (string.IsNullOrWhiteSpace(notableFilterText))
        {
            notableFilterText = """
                (Mythic Survivor) |
                (V-Bucks | X-Ray) |
                (Upgrade Llama) |
                (Legendary Survivor !Lead) |
                REMINDER
                """;
        }
        var notableSearchInstructions = PLSearch.GenerateSearchInstructions(notableFilterText);
        //templateId="Schematic:sid_edged_axe_scavenger_sr_ore_t01"
        return item => PLSearch.EvaluateInstructions(notableSearchInstructions, item.RawData);
    }

    bool needsRefresh = false;
    void FilterMissions()
    {
        var missions = GameMission.MissionList;
        if (lockFilter || missions is null)
            return;
        if (emptyIcon is not null)
            emptyIcon.Texture = GameMission.DailyCat;
        loadingIcon.Visible = false;
        rewards = [];
        if (!IsVisibleInTree())
        {
            needsRefresh = true;
            return;
        }
        needsRefresh = false;
        int curPL = (int)GameAccount.ActiveAccount.RatingData.PowerLevel;
        int ventPL = (int)GameAccount.ActiveAccount.VentureFortStats.PowerLevel;

        Func<GameMission, bool> missionPredicate = null;
        Func<GameItem, bool> itemPredicate = null;
        if (notableMode)
        {
            itemPredicate = CreateNotableFilter();
        }
        else
        {
            List<string> requiredZones = [.. zoneFilters
                .Select(c => c.ButtonPressed && c.GetMeta("zone", "").ToString() is string result && !string.IsNullOrWhiteSpace(result) ? result : null)
                .Where(result => result is not null)
            ];

            missionPredicate = m =>
            {
                if (requiredZones.Count > 0 && !requiredZones.Contains(m.TheaterCat))
                    return false;
                if (filterPower?.ButtonPressed == true && !m.PlayableBy(GameAccount.ActiveAccount))
                    return false;
                if (filterStory?.ButtonPressed != true && m.IsStoryMission)
                    return false;
                if (!PLSearch.EvaluateInstructions(missionSearchInstructions, m.SearchObject))
                    return false;
                return true;
            };

            List<string> requiredRarities = [.. rarityFilters
                .Select(c => c.ButtonPressed && c.GetMeta("rarity", "").ToString() is string result && !string.IsNullOrWhiteSpace(result) ? result : null)
                .Where(result => result is not null)
            ];
            List<string> requiredTypes = [.. typeFilters
                .Select(c => c.ButtonPressed && c.GetMeta("type", "").ToString() is string result && !string.IsNullOrWhiteSpace(result) ? result : null)
                .Where(result => result is not null)
            ];
            itemPredicate = i =>
            {
                if (repeatabilityFilters[0].ButtonPressed && i.zcpEquivelent is not null)
                    return false;
                else if (repeatabilityFilters[1].ButtonPressed && i.zcpEquivelent is null)
                    return false;
                else if (repeatabilityFilters[2].ButtonPressed && (i.zcpEquivelent is null || i.quantity < 4))
                    return false;

                if (
                    requiredRarities.Count > 0 &&
                    !requiredRarities.Contains(i.sortingTemplate.Rarity ?? "Uncommon")
                    )
                    return false;
                if (
                    requiredTypes.Count > 0 &&
                    !requiredTypes.Any(t => i.sortingTemplate.TemplateId.StartsWith(t)) &&
                    !requiredTypes.Contains($"{i.sortingTemplate.Type}>{i.sortingTemplate.Category}")
                    )
                    return false;

                if (!PLSearch.EvaluateInstructions(itemSearchInstructions, i.RawData))
                    return false;

                return true;
            };
        }

        List<MissionRewardPair> filteredRewards = [];

        foreach (var mission in missions)
        {
            if (missionPredicate is not null && !missionPredicate(mission))
                continue;
            foreach (var item in mission.allItems ?? [])
            {
                if (
                    item.template.DisplayName == "Gold" || 
                    item.template.DisplayName == "Venture XP"
                    )
                    continue;
                if(itemPredicate is not null && !itemPredicate(item))
                    continue;
                filteredRewards.Add(new(mission, item));
            }
        }

        if (notableMode)
            EmitSignalHasVBucks(filteredRewards.Any(r => r.item.template.VBucksOrXRayTickets));

        IOrderedEnumerable<MissionRewardPair> sortedRewards;

        if (sortByPower?.ButtonPressed == true)
        {
            sortedRewards = filteredRewards
                .OrderBy(r => -r.mission.PowerLevel);
        }
        else
        {
            sortedRewards = filteredRewards
                .OrderBy(r =>
                {
                    if (!notableMode)
                        return 0;
                    var template = r.item.sortingTemplate;
                    if(GameAccount.ActiveAccount.HasReminder(template))
                        return -25; // reminder items
                    if (template.RarityLevel == 6 && template.Type == "Worker")
                        return -20; // mythic leads
                    if (template.VBucksOrXRayTickets)
                        return -19; // v-bucks
                    //if (template.TemplateId == "AccountResource:voucher_cardpack_bronze")
                    //    return -18; // upgrade llamas
                    if (template.RarityLevel == 5 && template.Type == "Worker" && template.SubType is null)
                        return -17; // legendary survivor (excl. leads)
                    return 0;
                });
        }

        sortedRewards = sortedRewards
                .ThenBy(r => !GameAccount.ActiveAccount.HasReminder(r.item.templateId))
                .ThenBy(r => !(
                    r.item.template.VBucksOrXRayTickets ||
                    r.item.sortingTemplate.HasLevel ||
                    r.item.template.DisplayName.Contains("Llama", StringComparison.InvariantCultureIgnoreCase)
                ))
                .ThenBy(r => -r.item.sortingTemplate.RarityLevel)
                .ThenBy(r => r.item.sortingTemplate.Type)
                .ThenBy(r => r.item.sortingTemplate.DisplayName.EndsWith(" XP", StringComparison.InvariantCultureIgnoreCase))
                .ThenBy(r => r.item.sortingTemplate.DisplayName)
                .ThenBy(r => r.item.sortingTemplate != r.item.template)
                .ThenBy(r => -r.item.quantity);

        rewards = [.. sortedRewards];
        int limit = AppConfig.Get("missions", "notable_count", 20);
        if (notableMode && rewards.Count > limit)
            rewards = rewards[..limit];

        if (emptyContent is not null)
            emptyContent.Visible = rewards.Count == 0;

        missionList.UpdateList(true);
        missionList.Visible = true;
    }

    void ClearMissions()
    {
        loadingIcon.Visible = true;
        missionList.Visible = false;
        if (emptyContent is not null)
            emptyContent.Visible = false;
    }
}

public record struct MissionRewardPair(GameMission mission, GameItem item);