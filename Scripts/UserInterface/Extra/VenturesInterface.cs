using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

public partial class VenturesInterface : Control
{
	[Export]
	bool barOnly;

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
	[Export]
	LineEdit customXP;
	[Export]
	Control customXPSection;
	[Export]
	Control autoXPSection;
	[Export]
	Label fullXPLabel;

	[ExportGroup("Modifiers")]
	[Export]
	GameItemEntry[] modifierEntries;

	[ExportGroup("Rewards")]
	[Export]
	Button hideCompleted;
	[Export]
	Button importantLevels;
	[Export]
	VenturesLevelEntry[] mainItemEntries;
	[Export]
	VenturesLevelEntry[] extraItemEntries;

	bool hasSeason = false;
	GameProfile currentProfile;
	VentureSeasonProgressData currentSeason;
	VentureLevel[] currentLevels;
	GameItem[] currentItems;
	GameItem[] currentExtraItems;
	Dictionary<string, VentureSeasonProgressData> ventureSeasons;
	public override async void _Ready()
	{
		await Helpers.WaitForFrames(3);
		ventureSeasons = PegLegResourceManager.VenturesSeasons.Deserialize<Dictionary<string, VentureSeasonProgressData>>();
		RefreshTimerController.OnDayChanged += CheckSeason;
		GameAccount.ActiveAccountChanged += UpdateXP;
		importantLevels?.Toggled += _ => UpdateXP();
		hideCompleted?.Toggled += _ => UpdateXP();
		customXP?.TextSubmitted += SetCustomXP;

		if (!barOnly)
		{
			foreach (var item in mainItemEntries)
			{
				item.SetInterface(this);
			}
			foreach (var item in extraItemEntries)
			{
				item.SetInterface(this);
			}
		}

		CheckSeason();
	}

	void SetCustomXP(string newText)
	{
		if(int.TryParse(newText, out var newXP) && newXP >= 0)
			customXPValue = newXP;
		customXP.Text = customXPValue.ToString();
		UpdateXP();
	}

	int customXPValue;
	public event Action OnDisplayXPChanged;
	public int DisplayXP
	{
		get => field;
		set
		{
			field = value;
			UpdateTopBar();
			OnDisplayXPChanged?.Invoke();
		}
	}

	public override void _ExitTree()
	{
		GameAccount.ActiveAccountChanged -= UpdateXP;
		RefreshTimerController.OnDayChanged -= CheckSeason;
	}

	async void Refresh()
	{
		if (currentProfile is null)
			return;
		autoXPSection.Visible = false;
		await currentProfile.Query();
		UpdateXP();
	}

	void CheckSeason()
	{
		//await CalenderRequests.CheckCalender();
		string currentSeasonFlag = RefreshTimerController.GetSeasonIndex() switch
		{
			0 => "EventFlag.Phoenix.NewBeginnings",
			1 => "EventFlag.Phoenix.Adventure",
			2 => "EventFlag.Phoenix.RoadTrip",
			3 => "EventFlag.Phoenix.Fortnitemares",
			4 => "EventFlag.Phoenix.Winterfest",
			_ => null
		};
		hasSeason = ventureSeasons.TryGetValue(currentSeasonFlag, out currentSeason);

		currentSeason.Levels ??= [];
		currentSeason.PastLevels ??= [];

		currentLevels = [.. currentSeason.Levels.OrderBy(l => l.TotalRequiredXP)];
		currentItems = [.. currentLevels.Select(l => l.Rewards.FirstOrDefault().AsItem())];
		currentExtraItems = [.. currentSeason.PastLevels.Select(l => l.AsItem())];


		if(barOnly)
		{
			UpdateXP();
			return;
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

		//TODO: set up questlines

		UpdateXP();
	}

	//todo: make this more automated based on PL calculations (perhaps precalculate during export)
	static int LevelToMissionUnlock(int level) => level switch
	{
		7 => 23,
		11 => 34,
		16 => 46,
		20 => 58,
		23 => 70,
		28 => 82,
		31 => 94,
		36 => 108,
		39 => 124,
		43 => 140,
		_ => 0
	};

	void UpdateXP()
	{
		if (!hasSeason)
			return;

		if (currentProfile?.account != GameAccount.ActiveAccount && (currentProfile is not null || GameAccount.ActiveAccount.isOwned))
		{
			currentProfile?.OnProfileChanged -= UpdateXP;
			currentProfile = GameAccount.ActiveAccount.isOwned ? GameAccount.ActiveAccount.GetProfile(FnProfileTypes.AccountItems) : null;
			currentProfile?.OnProfileChanged += UpdateXP;
		}

		autoXPSection.Visible = currentProfile is not null;
		customXPSection.Visible = !autoXPSection.Visible;
		var xp = autoXPSection.Visible ? (currentProfile.GetFirstTemplateItem("AccountResource:phoenixxp")?.quantity ?? 0) : customXPValue;

		if (IsVisibleInTree())
		{
			var tween = CreateTween().SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Quad);
			tween.TweenProperty(this, "DisplayXP", xp, 0.5);
		}
		else
		{
			DisplayXP = xp;
		}

		if (barOnly)
			return;

		var visibleLevelIndexes = Enumerable.Range(0, currentLevels.Length).Where(i =>
		{
			if(hideCompleted.ButtonPressed && currentLevels[i].TotalRequiredXP <= xp)
				return i < currentLevels.Length && currentLevels[i + 1].TotalRequiredXP > xp; //only true when next level is not complete
			if (LevelToMissionUnlock(i + 1) > 0)
				return true;
			if (importantLevels.ButtonPressed && currentItems[i]?.template?.RarityLevel < 5)
				return false;
			return true;
		}).ToArray();

		for (int j = 0; j < visibleLevelIndexes.Length; j++)
		{
			var i = visibleLevelIndexes[j];
			var prevXP = j == 0 ? -1 : currentLevels[visibleLevelIndexes[j - 1]].TotalRequiredXP;
			var nextXP = j == visibleLevelIndexes.Length - 1 ? -1 : currentLevels[visibleLevelIndexes[j + 1]].TotalRequiredXP;

			mainItemEntries[j].Visible = true;
			mainItemEntries[j].SetInfo(currentItems[i], prevXP, currentLevels[i].TotalRequiredXP, nextXP, i + 1, LevelToMissionUnlock(i+1));
		}
		for (int j = visibleLevelIndexes.Length; j < mainItemEntries.Length; j++)
		{
			mainItemEntries[j].Visible = false;
		}

		int extraBaseXP = currentLevels[^1].TotalRequiredXP;
		var extraXPIncrement = currentSeason.PastLevelXPRequirement;
		int startingExtraLevel = Mathf.Max((xp - extraBaseXP) / extraXPIncrement, 2) - 2;
		for (int i = 0; i < extraItemEntries.Length; i++)
		{
			int extraLevel = startingExtraLevel + i;
			int levelRequirement = extraBaseXP + ((extraLevel + 1) * extraXPIncrement);
			extraItemEntries[i].SetInfo(currentExtraItems[extraLevel % currentExtraItems.Length], levelRequirement - extraXPIncrement, levelRequirement, levelRequirement + extraXPIncrement, 51+extraLevel, 0);
		}
	}

	void UpdateTopBar()
	{
		if (!hasSeason)
			return;

		var xp = DisplayXP;
		var extraXPThreshold = currentLevels[^1].TotalRequiredXP;
		float progress = 0;

		var nextLvData = currentLevels.FirstOrDefault(l => l.TotalRequiredXP > xp);
		var currentLevel = Array.IndexOf(currentLevels, nextLvData);
		var nextItem = nextLvData.TotalRequiredXP > 0 ? currentItems[currentLevel] : null;

		var nextMilestoneLvData = currentLevels.FirstOrDefault(l => l.IsMajorReward && l.TotalRequiredXP > nextLvData.TotalRequiredXP);
		var milestoneIdx = Array.IndexOf(currentLevels, nextMilestoneLvData);
		var nextMilestoneItem = nextMilestoneLvData.TotalRequiredXP > 0 ? currentItems[milestoneIdx] : null;

		if (nextItem is null)
		{
			var extraXP = xp - extraXPThreshold;
			var extraLevel = extraXP / currentSeason.PastLevelXPRequirement;
			currentLevel = 50 + extraLevel;
			nextItem = currentExtraItems[extraLevel % currentExtraItems.Length];
			progress = (float)(extraXP - (extraLevel * currentSeason.PastLevelXPRequirement)) / currentSeason.PastLevelXPRequirement;
		}
		else
		{
			var curLvData = currentLevels[currentLevel - 1];
			var relativeXP = xp - curLvData.TotalRequiredXP;
			var relativeTargetXP = nextLvData.TotalRequiredXP - curLvData.TotalRequiredXP;
			progress = (float)relativeXP / relativeTargetXP;
		}

		currentLevelLabel.Text = $"Level {currentLevel}";
		fullXPLabel.Text = xp.Notate();
		levelProgress.Value = progress * levelProgress.MaxValue;
		nextLevelLabel.Text = (currentLevel + 1).ToString();
		nextReward.SetItem(nextItem);

		majorLevelSection.Visible = nextMilestoneItem is not null;
		if (nextMilestoneItem is not null)
		{
			nextMajorLevelLabel.Text = (milestoneIdx + 1).ToString();
			nextMajorReward.SetItem(nextMilestoneItem);
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

		public readonly GameItem AsItem() => GameItemTemplate.Get(Item)?.CreateInstance(Quantity);
	}
}
