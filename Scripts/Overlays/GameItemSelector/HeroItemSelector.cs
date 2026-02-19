using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

public partial class HeroItemSelector : GameItemSelectorBase<HeroItemSelector.Config>
{
    static HeroItemSelector instance;

    public enum DisplayMode
    {
        Standard,
        Commander,
        TeamPerk,
        Support
    }

    public record class Config : BaseConfig
    {
        public string title = "Select Hero";
        public string commanderType;
        public string teamPerkType;
        public string lastSelectedId;
        public DisplayMode displayMode;
    }

    protected override Config DefaultConfigInternal => DefaultConfig;
    public static Config DefaultConfig { get; private set; } = new();

    public static Config CommanderConfig { get; private set; } = new()
    {
        displayMode = DisplayMode.Commander,
    };

    public static Config TeamPerkConfig { get; private set; } = new()
    {
        title = "Select Team Perk",
        displayMode = DisplayMode.TeamPerk,
        allowEmptySelection = true,
    };

    public static Config SupportConfig { get; private set; } = new()
    {
        displayMode = DisplayMode.Support,
        allowEmptySelection = true,
    };

    [Export]
    Label header;
    [Export]
    Control heroFiltersLayout;
    [Export]
    RecycleListContainer standardContainer;
    [Export]
    RecycleListContainer commanderContainer;
    [Export]
    RecycleListContainer teamperkContainer;
    [Export]
    RecycleListContainer supportContainer;
    [Export]
    LineEdit searchInput;
    [Export]
    VirtualTabBar classFilter;
    [Export]
    VirtualTabBar perkFilter;
    [Export]
    Control abilityFilterLayout;
    [Export]
    VirtualTabBar[] abilityFilters;
    [Export]
    Control abilityFilterNotice;
    [Export]
    Control rootPanel;

    public GameItemTemplate Commander { get; private set; }
    public GameItemTemplate TeamPerk { get; private set; }

    public override void _Ready()
    {
        base._Ready();
        standardContainer.SetProvider(this);
        commanderContainer.SetProvider(this);
        teamperkContainer.SetProvider(this);
        supportContainer.SetProvider(this);
        instance = this;
        rootPanel.OffsetLeft = -rootPanel.GetCombinedMinimumSize().X;

        classFilter.SetTabPressed(0);
        perkFilter.SetTabPressed(0);
        searchInput.TextChanged += UpdateSearchFilters;
        classFilter.TabsChanged += UpdateTabFilters;
        perkFilter.TabsChanged += UpdateTabFilters;
        foreach (var filter in abilityFilters)
        {
            filter.TabsChanged += UpdateTabFilters;
        }
        UpdateTabFilters();
    }

    string classRequirement;
    string[] descriptionRequirements = [];
    string abilityRequirement;
    bool requireAbilityBuff;
    bool requireTeamPerkContribution;
    PLSearch.Instruction[] searchInstructions;

    bool lockFilters = false;
    void UpdateTabFilters()=>UpdateTabFilters(true);
    void UpdateTabFilters(bool filterAfter)
    {
        if (lockFilters)
            return;
        lockFilters = true;

        requireTeamPerkContribution = classFilter.LatestTab == 1;
        classRequirement = classFilter.LatestTab switch
        {
            2 => "Constructor",
            3 => "Ninja",
            4 => "Outlander",
            5 => "Soldier",
            _ => null
        };

        descriptionRequirements = perkFilter.LatestTab switch
        {
            1 => ["Ranged Weapon", "Fire Rate", "Reload"],
            2 => ["Melee"],
            3 => ["Trap", "B.A.S.E."],
            4 => ["Health"],
            5 => ["Shield"],
            6 => ["(?<!Damage type to )(?<!trail of )Energy(?! Damage)(?! affliction)"],
            7 => ["Fire Damage", "Water Damage", "Nature Damage", "Energy Damage", "Damage type to Energy", "Physical Damage"],
            _ => []
        };

        bool wasAbilityVisible = abilityFilterLayout.Visible;
        abilityFilterLayout.Visible = (commanderContainer.Visible && classFilter.LatestTab > 1) || perkFilter.LatestTab == perkFilter.TabCount - 1;

        if (abilityFilterLayout.Visible && heroFiltersLayout.Visible)
        {
            for (int i = 0; i < abilityFilters.Length; i++)
            {
                abilityFilters[i].Visible = classFilter.LatestTab == (i + 2);
                if (!wasAbilityVisible)
                    abilityFilters[i].SetTabPressed(0);
            }
            abilityFilterNotice.Visible = classFilter.LatestTab <= 1;
            requireAbilityBuff = perkFilter.LatestTab == perkFilter.TabCount - 1;
            abilityRequirement = classFilter.LatestTab switch
            {
                2 => abilityFilters[0].LatestTab switch // constructor
                {
                    1 => "Granted.Ability.Constructor.BullRush",
                    2 => "Granted.Ability.Constructor.Decoy",
                    3 => "Granted.Ability.Constructor.GoinConstructor",
                    4 => "Granted.Ability.Constructor.PlasmaPulse",
                    5 => "Granted.Ability.Constructor.MountedTurret",
                    _ => null,
                },
                3 => abilityFilters[1].LatestTab switch // ninja
                {
                    1 => "Granted.Ability.Ninja.CrescentKick",
                    2 => "Granted.Ability.Ninja.DragonSlash",
                    3 => "Granted.Ability.Ninja.SmokeBomb",
                    4 => "Granted.Ability.Ninja.ThrowingStars",
                    5 => "Granted.Ability.Ninja.KunaiStorm",
                    _ => null,
                },
                4 => abilityFilters[2].LatestTab switch // outlander
                {
                    1 => "Granted.Ability.Outlander.Bear",
                    2 => "Granted.Ability.Outlander.PhaseShift",
                    3 => "Granted.Ability.Outlander.GroundWave",
                    4 => "Granted.Ability.Outlander.ShockTower",
                    _ => null,
                },
                5 => abilityFilters[3].LatestTab switch // soldier
                {
                    1 => "Granted.Ability.Commando.GoinCommando",
                    2 => "Granted.Ability.Commando.FragGrenade",
                    3 => "Granted.Ability.Commando.LeftyAndRighty",
                    4 => "Granted.Ability.Commando.Shockwave",
                    5 => "Granted.Ability.Commando.WarCry",
                    _ => null,
                },
                _ => null,
            };

        }
        else
            abilityRequirement = null;

        lockFilters = false;
        if (filterAfter)
            FilterItems();
    }

    void UpdateSearchFilters(string _) => UpdateSearchFilters();
    void UpdateSearchFilters(bool filterAfter = true)
    {
        searchInstructions = PLSearch.GenerateSearchInstructions(searchInput.Text);
        if (filterAfter)
            FilterItems();
    }

    protected override Func<GameItem, bool> FilterFunction => ItemFilter;
    protected override Func<IOrderedEnumerable<GameItem>, IOrderedEnumerable<GameItem>> SortingFunction => ItemSorter;
    private bool ItemFilter(GameItem item)
    {
        GameItemTemplate[] abilities = item.template?.GetHeroAbilities();
        if (abilities is null)
            return !supportContainer.Visible && !commanderContainer.Visible;
        if (classRequirement is not null)
        {
            if (item.template?.SubType != classRequirement)
                return false;
        }
        if (requireTeamPerkContribution && TeamPerk?.TeamPerkBoostedByHero(item.template) != true)
            return false;
        if (descriptionRequirements.Length > 0)
        {
            abilities ??= item.template?.GetHeroAbilities();
            var perk = commanderContainer.Visible ? abilities[1] : abilities[0];
            if (!descriptionRequirements.Any(r => Regex.Match(perk.Description, r, RegexOptions.IgnoreCase).Success))
                return false;
        }
        if (abilityRequirement is not null)
        {
            if (requireAbilityBuff)
            {
                if (item.template["HeroPerkRequirement"]?["CommanderTag"]?.Deserialize<string[]>().Contains(abilityRequirement) != true)
                    return false;
            }
            else
            {
                if (item.template["HeroTags"]?.Deserialize<string[]>().Contains(abilityRequirement) != true)
                    return false;
            }
        }
        var activeAbility = (commanderContainer.Visible && item.template.Tier > 1) ? abilities[1] : abilities[0];
        return PLSearch.EvaluateInstructions(searchInstructions, item.CustomSearchObject(() => [activeAbility.Description, Deacronymise(activeAbility.Description)], true));
    }

    private IOrderedEnumerable<GameItem> ItemSorter(IOrderedEnumerable<GameItem> items) => items
        .ThenBy(item => item.uuid != CurrentConfig.lastSelectedId)
        .ThenBy(item => item.template?.PerkCompatibleWithCommander(Commander, out _) != true)
        .ThenBy(item => -item.CalculateRating())
        .ThenBy(item => -item.template?.RarityLevel);

    static string Deacronymise(string input) =>
        input
        .Replace("B.A.S.E.", "BASE")
        .Replace("R.O.S.I.E.", "ROSIE")
        .Replace("D.E.C.O.Y.", "DECOY")
        .Replace("T.E.D.D.Y.", "TEDDY");

    public static async Task<GameItem> OpenSelector(IEnumerable<GameItem> itemOptions, Config config = null)
    {
        var result = await instance.OpenSelectorInternal(itemOptions, config);
        return result is null ? null : (result.FirstOrDefault() ?? GameItem.Empty);
    }

    protected override void InitialiseSelector(IEnumerable<GameItem> itemOptions)
    {
        Commander = GameItemTemplate.Get(CurrentConfig.commanderType);
        TeamPerk = GameItemTemplate.Get(CurrentConfig.teamPerkType);

        header.Text = CurrentConfig.title;

        standardContainer.Visible = false;
        commanderContainer.Visible = false;
        teamperkContainer.Visible = false;
        supportContainer.Visible = false;
        activeContainer = CurrentConfig.displayMode switch
        {
            DisplayMode.Commander => commanderContainer,
            DisplayMode.TeamPerk => teamperkContainer,
            DisplayMode.Support => supportContainer,
            _ => standardContainer
        };
        activeContainer.Visible = true;

        heroFiltersLayout.Visible = commanderContainer.Visible || supportContainer.Visible;
        if (heroFiltersLayout.Visible)
        {
            //is support mode and team perk exists
            var hideTeamPerk = commanderContainer.Visible || TeamPerk is null;
            classFilter.SetTabHidden(1, hideTeamPerk);
        }

        base.InitialiseSelector(itemOptions);
    }

    protected override void SetDefaultSortingAndFilter()
    {
        UpdateTabFilters(false);
        UpdateSearchFilters(false);
        //sorting
    }

    //override buildtween
    protected override bool UseWindowAnim => false;
    protected override Tween BuildTween(bool openState, double duration)
    {
        var tween = CreateTween().SetParallel();
        tween.TweenSubtween(base.BuildTween(openState, duration));
        tween.TweenProperty(rootPanel, "offset_left", openState ? 0 : -rootPanel.GetCombinedMinimumSize().X, duration);
        return tween;
    }
}
