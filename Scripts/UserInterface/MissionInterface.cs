using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;

public partial class MissionInterface : Control, IRecyclableElementProvider<GameMission>
{
    #region Statics
    static MissionInterface instance;

    static NotificationData? unexpectedResetNotif;
    static NotificationData UnexpectedResetNotif => unexpectedResetNotif ??= new()
    {
        header = "Unexpected Reset Detected",
        icon = instance?.unexpectedResetNotifIcon,
        sound = instance?.unexpectedResetSound,
        itemColor = Color.FromHtml("#ff5555"),
        urgent = true,
    };

    static readonly string[] theaterFilters =
    [
        "spct",
        "spctv",
        "s",
        "p",
        "c",
        "t",
        "v",
    ];

    static readonly string[][] itemFilters =
    [
        [
            "Worker:*",
        ],
        [
            "Schematic:*",
        ],
        [
            "Hero:*",
            "Defender:*",
        ],
        [
            "AccountResource:reagent_c_t01",
            "AccountResource:reagent_c_t02",
            "AccountResource:reagent_c_t03",
            "AccountResource:reagent_c_t04",
        ],
        [
            "AccountResource:reagent_alteration_generic",
            "AccountResource:reagent_alteration_upgrade_uc",
            "AccountResource:reagent_alteration_upgrade_r",
            "AccountResource:reagent_alteration_upgrade_vr",
            "AccountResource:reagent_alteration_upgrade_sr",
        ],
        [
            "AccountResource:currency_mtxswap",
            "AccountResource:voucher_cardpack_bronze",
        ],
    ];
    #endregion

    public static void SearchInMissions(string searchText, bool showMissionTab = false)
    {
        instance.searchBar.Text = searchText;
        instance.UpdateFilters();
        if (showMissionTab)
        {
            instance.Visible = true;
            instance.FilterMissions();
        }
    }

    [Export]
    Texture2D unexpectedResetNotifIcon;
    [Export]
    AudioStream unexpectedResetSound;

    [Export]
    VirtualTabBar zoneFilterTabBar;
    [Export]
	LineEdit searchBar;
    [Export]
    LineEdit itemSearchBar;

    [Export]
    CheckButton[] itemFilterButtons = [];

    [Export]
    CheckButton playableFilter;
    [Export]
    CheckButton storyFilter;

    [Export]
    RecycleListContainer missionList;

    [Export]
	TextureProgressBar loadingIcon;

    List<GameMission> filteredMissions = [];
    public GameMission GetRecycleElement(int index) => index >= 0 && index < filteredMissions.Count ? filteredMissions[index] : null;
    public int GetRecycleElementCount() => filteredMissions.Count;

    public override void _Ready()
    {
        instance = this;
        missionList.Visible = false;
        loadingIcon.Visible = true;
        //searchBar.Text = AppConfig.Get("missions", "default_search", "");

        VisibilityChanged += () =>
        {
            if (needsFilter)
                FilterMissions();
        };

        zoneFilterTabBar.TabsChanged += FilterMissions;
        searchBar.TextSubmitted += _ => UpdateFilters();
		searchBar.TextChanged += _ => UpdateFilters();
        itemSearchBar.TextSubmitted += _ => UpdateFilters();
        itemSearchBar.TextChanged += _ => UpdateFilters();
        playableFilter.Pressed += FilterMissions;
        storyFilter.Pressed += FilterMissions;
        foreach (var button in itemFilterButtons)
        {
            button.Pressed += UpdateFilters;
        }
        UpdateFilters();

        missionList.SetProvider(this);

        RefreshTimerController.OnDayChanged += ForceReloadMissions;
        RefreshTimerController.OnDayChanged += StartUpdateCheckTimer;

        GameAccount.ActiveAccountChanged += FilterMissions;
        GameMission.OnMissionsUpdated += OnMissionsUpdated;
        GameMission.OnMissionsInvalidated += OnMissionsInvalidated;

        //GameAccount.ActiveAccountChanged += ForceReloadMissions;

        if (GameMission.currentMissions is not null)
            OnMissionsUpdated();
        else
            GameMission.UpdateMissions().StartTask();
        StartUpdateCheckTimer();
    }

    public override void _ExitTree()
    {
        RefreshTimerController.OnDayChanged -= ForceReloadMissions;
        RefreshTimerController.OnDayChanged -= StartUpdateCheckTimer;

        GameAccount.ActiveAccountChanged -= FilterMissions;
        GameMission.OnMissionsUpdated -= OnMissionsUpdated;
        GameMission.OnMissionsInvalidated -= OnMissionsInvalidated;
    }

    CancellationTokenSource updateCheckCTS = new();
    async void StartUpdateCheckTimer()
    {
        var now = DateTime.UtcNow;
        if (now.Hour > 1 || !AppConfig.Get("missions", "reset_detection", true))
            return;
        GD.Print("Starting update check timer");
        updateCheckCTS.CancelAndRegenerate(out var ct);
        while (true)
        {
            now = DateTime.UtcNow;
            if (now.Hour > 1 || !AppConfig.Get("missions", "reset_detection", true))
            {
                GD.Print("Ending update check timer");
                return;
            }
            int duration = now.Minute < 10 ? 15 : 90;
            while (duration > 0)
            {
                await Helpers.WaitForTimer(1);
                if (ct.IsCancellationRequested || !AppConfig.Get("missions", "reset_detection", true))
                    return;
                duration--;
            }
            if (await GameMission.MissionsNeedUpdate() && GameMission.currentMissions is not null)
            {
                GD.Print("Unexpected reset detected");
                NotificationManager.Push([UnexpectedResetNotif]);
                await GameMission.UpdateMissions();
            }
        }
    }

    public void FakeUnexpectedReset()
    {
        NotificationManager.Push([UnexpectedResetNotif with {
            expires = DateTime.UtcNow.AddSeconds(5)
        }]);
        //NotificationManager.PushNotification(unexpectedResetNotif);
        //NotificationManager.PushNotification(unexpectedResetNotif);
    }

    void OnMissionsInvalidated()
    {
        missionList.Visible = false;
        loadingIcon.Visible = true;
    }

    void OnMissionsUpdated()
    {
        missionList.Visible = true;
        loadingIcon.Visible = false;
        FilterMissions();
    }

    public void ForceReloadMissions() => GameMission.UpdateMissions().StartTask();

    PLSearch.Instruction[] missionSearchInstructions = [];
    PLSearch.Instruction[] itemSearchInstructions = [];
    string[] extraItemFilters = [];
    void UpdateFilters()
    {
        missionSearchInstructions = PLSearch.GenerateSearchInstructions(searchBar.Text) ?? [];
        itemSearchInstructions = PLSearch.GenerateSearchInstructions(itemSearchBar.Text) ?? [];

        List<string> extraItemFilterList = [];
        for (int i = 0; i < itemFilters.Length; i++)
        {
            if (itemFilterButtons.Length > i && itemFilterButtons[i].ButtonPressed)
            {
                extraItemFilterList.AddRange(itemFilters[i]);
            }
        }
        extraItemFilters = [.. extraItemFilterList];
        FilterMissions();
    }

    public static bool MatchItemOrEquivelent(GameItem item, PLSearch.Instruction[] itemInstructions, string[] extraItemFilters = null) =>
        MatchItem(item, itemInstructions, extraItemFilters) ||
        (
            item.zcpEquivelent is not null &&
            MatchItem(item.zcpEquivelent, itemInstructions, extraItemFilters)
        );

    public static bool MatchItem(GameItem item, PLSearch.Instruction[] itemInstructions, string[] extraItemFilters = null)
    {
        extraItemFilters ??= [];
        bool matchesItemFilters = extraItemFilters.Length == 0;
        foreach (var itemFilter in extraItemFilters)
        {
            matchesItemFilters = item.templateId.Contains(itemFilter);
            if (itemFilter.EndsWith('*'))
                matchesItemFilters = item.templateId.StartsWith(itemFilter[..^1]);

            if (itemFilter == "MYTHICLEAD")
                matchesItemFilters = Regex.Match(item.templateId, "Worker:manager\\w+_sr_\\w*").Success;

            if (matchesItemFilters)
                break;
        }
        if (!matchesItemFilters)
            return false;
        matchesItemFilters = PLSearch.EvaluateInstructions(itemInstructions, item.RawData); //search item instructions
        return matchesItemFilters;
    }

    string theaterFilter => theaterFilters[Mathf.Max(zoneFilterTabBar.LatestTab, 0)];
    bool MissionFilter(GameMission mission)
    {
        if (!theaterFilter.Contains(mission.TheaterCat[0]))
            return false;
        if (playableFilter?.ButtonPressed == true && !mission.PlayableBy(GameAccount.ActiveAccount))
            return false;
        if (storyFilter?.ButtonPressed != true && mission.IsStoryMission)
            return false;

        //var currentItemInstructions = itemSearchInstructions;
        if (!PLSearch.EvaluateInstructions(missionSearchInstructions, mission.SearchObject))
        {
            //if (currentItemInstructions.Length == 0)
            //    currentItemInstructions = missionSearchInstructions;
            //else
                return false;
        }

        return mission.allItems?.Any(item => MatchItemOrEquivelent(item, itemSearchInstructions, extraItemFilters)) ?? false;
    }

    bool needsFilter = false;
    void FilterMissions()
    {
        needsFilter = true;
        if (!IsVisibleInTree())
            return;
        needsFilter = false;
        filteredMissions = GameMission.currentMissions?
            .Where(MissionFilter)
            .ToList() ?? [];
        missionList.UpdateList(true);
    }
}

