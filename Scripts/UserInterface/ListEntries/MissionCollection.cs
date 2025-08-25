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

    List<GameMission> filteredMissions = [];

    public GameMission GetRecycleElement(int index) => (index >= 0 && index < filteredMissions.Count) ? filteredMissions[index] : null;
    public int GetRecycleElementCount() => filteredMissions.Count;

    PLSearch.Instruction[] missionSearchInstructions = [];
    PLSearch.Instruction[] itemSearchInstructions = [];

    public override void _Ready()
    {
        missionList.SetProvider(this);
        GameAccount.ActiveAccountChanged += SetMissionsDirty;
        GameMission.OnMissionsUpdated += SetMissionsDirty;
        GameMission.OnMissionsInvalidated += ClearMissions;
        VisibilityChanged += FilterMissions;
        if (playableFilter is not null)
            playableFilter.Pressed += SetMissionsDirty;
        EmitSignal(SignalName.NameChanged, testName);
        UpdateFilters();
        SetMissionsDirty();
    }

    public override void _ExitTree()
    {
        GameMission.OnMissionsUpdated -= SetMissionsDirty;
        GameMission.OnMissionsInvalidated -= ClearMissions;
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

    public void UpdateFilters()
	{
        var searchText = testSearch;
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
        if (playableFilter?.ButtonPressed == true && !mission.PlayableBy(GameAccount.activeAccount))
            return false;
        if (!PLSearch.EvaluateInstructions(missionSearchInstructions, mission.SearchObject))
            return false;

        return mission.allItems.Any(item => MissionInterface.MatchItemOrEquivelent(item, itemSearchInstructions));
    }

    public Predicate<GameItem> HighlightedItemFilter => ItemFilter;
    bool ItemFilter(GameItem item) => itemSearchInstructions.Length > 0 && MissionInterface.MatchItemOrEquivelent(item, itemSearchInstructions);
    public void ClearMissions()
    {
        loadingIcon.Visible = true;
        missionList.Visible = false;
    }

    public void SetMissionsDirty()
    {
        missionsDirty = true;
        FilterMissions();
    }

    bool missionsDirty = false;

    public void FilterMissions()
    {
        if (!missionsDirty || !IsVisibleInTree())
            return;
        missionsDirty = false;

        loadingIcon.Visible = false;
        missionList.Visible = true;

        var sortedMissions =
            GameMission.currentMissions?
            .Where(MissionFilter).OrderBy(m=>1) ?? default;

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
