using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

public partial class VenturesInterface : Control
{
    [ExportGroup("Status Bar")]
    [Export]
    Label currentLevelLabel;
    [Export]
    ProgressBar levelProgress;
    [Export]
    Label nextLevelLabel;
    [Export]
    GameItemEntry nextReward;
    [Export]
    Control majorLevelSection;
    [Export]
    Label nextMajorLevelLabel;
    [Export]
    GameItemEntry nextMajorReward;

    [ExportGroup("Modifiers")]
    [Export(PropertyHint.ArrayType)]
    GameItemEntry[] modifierEntries;

    [ExportGroup("Main Rewards")]
    [Export(PropertyHint.ArrayType)]
    GameItemEntry[] mainItems;
    [Export(PropertyHint.ArrayType)]
    Control[] mainCheckmarks;
    Control[] mainCheckmarkImages;

    [ExportGroup("Extra Rewards")]
    [Export(PropertyHint.ArrayType)]
    GameItemEntry[] extraItems;
    [Export]
    Control nextExtraHighlight;

    bool hasSeason = false;
    VentureSeasonProgressData currentSeason;
    Dictionary<string, VentureSeasonProgressData> ventureSeasons;
    public override void _Ready()
    {
        ventureSeasons = PegLegResourceManager.LoadResourceObj<Dictionary<string, VentureSeasonProgressData>>("GameAssets/VenturesSeasons.json");
        RefreshTimerController.OnDayChanged += CheckSeason;
        mainCheckmarkImages = mainCheckmarks.Select(c => c.GetChild(0) as Control).ToArray();
        CheckSeason();

        GameAccount.ActiveAccountChanged += UpdateAccount;
    }

    public override void _ExitTree()
    {
        GameAccount.ActiveAccountChanged -= UpdateAccount;
        RefreshTimerController.OnDayChanged -= CheckSeason;
    }

    async void CheckSeason()
    {
        await CalenderRequests.CheckCalender();
        string currentSeasonFlag = null;
        hasSeason = false;
        currentSeason = default;
        foreach (var key in ventureSeasons.Keys)
        {
            if (CalenderRequests.EventFlagActive(key))
            {
                hasSeason = true;
                if (currentSeason == ventureSeasons[key])
                    return;
                currentSeasonFlag = key;
                currentSeason = ventureSeasons[key];
                break;
            }
        }

        string[] modifiers = currentSeasonFlag switch
        {
            "EventFlag.Phoenix.NewBeginnings" => [
                "GameplayModifier:gm_phoenix_escalation"
            ],
            "EventFlag.Phoenix.Adventure" => [
                "GameplayModifier:gm_phoenix_superhusks_huskmods",
                "GameplayModifier:gm_phoenix_superhusks_playerbenefits"
            ],
            "EventFlag.Phoenix.RoadTrip" => [
                "GameplayModifier:gm_phoenix_ragemeter"
            ],
            "EventFlag.Phoenix.Fortnitemares" => [
                "GameplayModifier:gm_phoenix_closequarters"
            ],
            "EventFlag.Phoenix.Winterfest" => [
                "GameplayModifier:gm_phoenix_superheroic",
                "GameplayModifier:gm_phoenix_superconstructor",
                "GameplayModifier:gm_phoenix_superninja",
                "GameplayModifier:gm_phoenix_superoutlander"
            ],
            _ => []
        };

        for (int i = 0; i < modifiers.Length; i++)
        {
            modifierEntries[i].Visible = true;
            modifierEntries[i].SetItem(GameItemTemplate.Get(modifiers[i]).CreateInstance());
        }
        for (int i = modifiers.Length; i < modifierEntries.Length; i++)
        {
            modifierEntries[i].Visible = false;
        }

        currentSeason.Levels ??= [];
        for (int i = 1; i < currentSeason.Levels.Length; i++)
        {
            mainItems[i-1].Visible = true;
            mainItems[i-1].SetItem(currentSeason.Levels[i].Rewards[0].AsItem());
            mainCheckmarks[i-1].Visible = true;
        }
        for (int i = currentSeason.Levels.Length-1; i < mainItems.Length; i++)
        {
            if (i < 0)
                continue;
            mainItems[i].Visible = false;
            mainCheckmarks[i].Visible = false;
        }

        currentSeason.PastLevels ??= [];
        for (int i = 0; i < currentSeason.PastLevels.Length; i++)
        {
            extraItems[i].Visible = true;
            extraItems[i].SetItem(currentSeason.PastLevels[i].AsItem());
        }
        for (int i = currentSeason.PastLevels.Length; i < extraItems.Length; i++)
        {
            extraItems[i].Visible = false;
        }

        UpdateAccount();
    }

    async void UpdateAccount()
    {
        if (!hasSeason)
            return;

        var profile = await GameAccount.activeAccount.GetProfile(FnProfileTypes.AccountItems).Query();
        var ventureXP = profile.GetFirstTemplateItem("AccountResource:phoenixxp")?.quantity ?? 0;
        int currentLevel = 0;

        int currentProgress = 0;
        int targetProgress = 0;
        int totalTarget = 0;

        GameItem nextItem = null;
        GameItem nextMilestone = null;
        int milestoneLevel = 0;

        for (int i = 1; i < currentSeason.Levels.Length; i++)
        {
            bool aboveLevel = ventureXP > currentSeason.Levels[i].TotalRequiredXP;
            mainCheckmarkImages[i-1].Visible = aboveLevel;
            if (aboveLevel)
            {
                currentLevel = i;
            }
            else if(currentLevel+1 == i)
            {
                nextItem = currentSeason.Levels[i].Rewards[0].AsItem();
                int startProgress = currentSeason.Levels[i - 1].TotalRequiredXP;
                currentProgress = ventureXP - startProgress;
                targetProgress = currentSeason.Levels[i].TotalRequiredXP - startProgress;
                totalTarget = currentSeason.Levels[i].TotalRequiredXP;
            }
            else if(nextMilestone is null && currentSeason.Levels[i].IsMajorReward)
            {
                nextMilestone = currentSeason.Levels[i].Rewards[0].AsItem();
                milestoneLevel = i;
            }
        }

        if (currentLevel == currentSeason.Levels.Length-1)
        {
            var pastMaxXP = ventureXP - currentSeason.Levels[^1].TotalRequiredXP;
            int pastMaxLevel = pastMaxXP / currentSeason.PastLevelXPRequirement;
            currentProgress = pastMaxXP - (pastMaxLevel * currentSeason.PastLevelXPRequirement);
            targetProgress = currentSeason.PastLevelXPRequirement;
            currentLevel += pastMaxLevel;
            totalTarget = currentSeason.Levels[^1].TotalRequiredXP + ((pastMaxLevel + 1) * currentSeason.PastLevelXPRequirement);

            var rewardIndex = pastMaxLevel % currentSeason.PastLevels.Length;
            nextItem = currentSeason.PastLevels[rewardIndex].AsItem();
        }

        currentLevelLabel.Text = $"Lv {currentLevel}";
        levelProgress.Value = (float)currentProgress / targetProgress;
        levelProgress.TooltipText = $"{(targetProgress - currentProgress).Notate()} XP to Lv {currentLevel + 1} ({ventureXP.Notate()}/{totalTarget.Notate()})";
        nextLevelLabel.Text = $"Lv {currentLevel+1}";
        nextReward.SetItem(nextItem);

        majorLevelSection.Visible = nextMilestone is not null;
        if (nextMilestone is not null)
        {
            nextMajorLevelLabel.Text = $"Lv {milestoneLevel}";
            nextMajorReward.SetItem(nextMilestone);
        }
    }

    public record struct VentureSeasonProgressData
    {
        [JsonInclude]
        public VentureLevel[] Levels;
        [JsonInclude]
        public int PastLevelXPRequirement;
        [JsonInclude]
        public VentureReward[] PastLevels;//todo: make one dimensional array
    }

    public record struct VentureLevel
    {
        [JsonInclude]
        public bool IsMajorReward;
        [JsonInclude]
        public VentureReward[] Rewards;
        [JsonInclude]
        public int TotalRequiredXP;
    }

    //todo: this is the structure of a quest reward, once we serialise quests properly this should be replaced
    public record struct VentureReward
    {
        [JsonInclude]
        public string Item;
        [JsonInclude]
        public int Quantity;

        public readonly GameItem AsItem() => GameItemTemplate.Get(Item).CreateInstance(Quantity);
    }
}
