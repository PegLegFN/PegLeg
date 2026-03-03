using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class MissionCollection : Control, IMissionHighlightProvider, IRecyclableElementProvider<GameMission>
{
    public event Action OnHighlightedItemFilterChanged;
    [Signal]
    public delegate void NameChangedEventHandler(string name);
    [Export]
	string testName;
	[Export]
	string testSearch;
    [Export]
    RecycleListContainer missionList;
    [Export]
    Control loadingIcon;
    [Export]
    CheckButton playableFilter;
    [Export]
    bool sortByPower;
    [Export]
    bool sortByZoneCat;
    [Export]
    bool requireAnyUnlockedForVisibility;
    [Export]
    bool alwaysHidePlayableFilter;
    [Export]
    bool ignoreLargeXPSetting;

    List<GameMission> filteredMissions = [];

    public GameMission GetRecycleElement(int index) => (index >= 0 && index < filteredMissions.Count) ? filteredMissions[index] : null;
    public int GetRecycleElementCount() => filteredMissions.Count;

    PLSearch.Instruction[] missionSearchInstructions = [];
    PLSearch.Instruction[] itemSearchInstructions = [];

    public override void _Ready()
    {
        missionList.SetProvider(this);
        GameAccount.ActiveAccountChanged += UpdateAccount;
        GameMission.OnMissionsUpdated += SetMissionsDirty;
        GameMission.OnMissionsInvalidated += ClearMissions;
        AppConfig.OnConfigChanged += OnConfigChanged;
        CtrlParent.VisibilityChanged += FilterMissions;
        if (playableFilter?.IsInsideTree() == true)
        {
            playableFilter.Pressed += SetMissionsDirty;
            playableFilter.Visible = GameAccount.ActiveAccount.isOwned && !alwaysHidePlayableFilter;
        }
        EmitSignal(SignalName.NameChanged, testName);
        ClearMissions();
        UpdateFilters();
        SetMissionsDirty();
    }

    private void OnConfigChanged(string section, string key, System.Text.Json.Nodes.JsonValue value)
    {
        if (section != "missions")
            return;
        if (key == "excludeLargeXP")
        {
            UpdateFilters();
            SetMissionsDirty();
        }
    }

    public override void _ExitTree()
    {
        GameAccount.ActiveAccountChanged -= UpdateAccount;
        GameMission.OnMissionsUpdated -= SetMissionsDirty;
        GameMission.OnMissionsInvalidated -= ClearMissions;
        AppConfig.OnConfigChanged -= OnConfigChanged;
    }

    void UpdateAccount()
    {
        if (playableFilter?.IsInsideTree()==true)
            playableFilter.Visible = GameAccount.ActiveAccount.isOwned && !alwaysHidePlayableFilter;
        SetMissionsDirty();
    }

    public void GoToSearch()
    {
        MissionInterface.SearchInMissions(testSearch, true);
    }

    public void SetSortByPower(bool val)
    {
        sortByPower = val;
        FilterMissions();
    }

    public void OnElementSpawned(IRecyclableEntry entry)
    {
        if (entry is not MissionEntry missionEntry) 
            return;
        missionEntry.SetHighlightProvider(this);
    }

    string activeSearchText;
    public void UpdateFilters()
	{
        var searchText = testSearch;
        if (AppConfig.Get("missions", "excludeLargeXP", false) && !ignoreLargeXPSetting)
            searchText = searchText.Trim() + " !XP !Gold";
        activeSearchText = searchText;
        if (searchText.Contains("///"))
        {
            string[] splitSearchText = searchText.Split("///");
            missionSearchInstructions = PLSearch.GenerateSearchInstructions(splitSearchText[0]) ?? [];
            itemSearchInstructions = PLSearch.GenerateSearchInstructions(splitSearchText[1..].Join()) ?? [];
        }
        else
        {
            missionSearchInstructions = PLSearch.GenerateSearchInstructions(searchText) ?? [];
            itemSearchInstructions = [];
        }
        OnHighlightedItemFilterChanged?.Invoke();
    }

    bool MissionFilter(GameMission mission)
    {
        if (playableFilter?.ButtonPressed == true && !mission.PlayableBy(GameAccount.ActiveAccount))
            return false;
        if (!PLSearch.EvaluateInstructions(missionSearchInstructions, mission.SearchObject))
            return false;

        return mission.allItems.Any(item => MissionInterface.MatchItemOrEquivelent(item, itemSearchInstructions));
    }

    public Func<GameItem, bool> HighlightedItemFilter => ItemFilter;
    bool ItemFilter(GameItem item) => itemSearchInstructions.Length > 0 && MissionInterface.MatchItemOrEquivelent(item, itemSearchInstructions);
    public void ClearMissions()
    {
        loadingIcon.Visible = true;
        missionList.Visible = false;
        if (requireAnyUnlockedForVisibility)
            Visible = false;
    }

    public void SetMissionsDirty()
    {
        missionsDirty = true;
        FilterMissions();
    }

    bool missionsDirty = false;

    Control CtrlParent => GetParent() as Control;

    public void FilterMissions()
    {
        if (!missionsDirty || !CtrlParent.IsVisibleInTree())
            return;

        missionsDirty = false;

        loadingIcon.Visible = false;
        missionList.Visible = true;

        var sortedMissions =
            (GameMission.MissionList?
            .Where(MissionFilter) ?? []).OrderBy(m=>1);

        if(requireAnyUnlockedForVisibility)
        {
            Visible = sortedMissions.Any();
            if (!Visible)
                return;
        }

        if (sortByZoneCat)
        {
            sortedMissions = sortedMissions
                .ThenBy(m => m.TheaterCat switch
                {
                    "t" => -4,
                    "c" => -3,
                    "p" => -2,
                    "s" => -1,
                    "v" => 0,
                    _ => 0
                })
                .ThenBy(m => -m.PowerLevel);
        }
        else if (sortByPower)
        {
            sortedMissions = sortedMissions
                .ThenBy(m => -m.PowerLevel);
        }
        else
        {
            sortedMissions = sortedMissions
                .ThenBy(m => m.allItems
                    .Where(ItemFilter)
                    .Select(i => -i.sortingTemplate.RarityLevel * i.quantity)
                    .Sum()
                );
        }

        filteredMissions = [.. sortedMissions];

        missionList.UpdateList(true);
    }
}
