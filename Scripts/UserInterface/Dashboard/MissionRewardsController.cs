using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

public partial class MissionRewardsController : Control, IRecyclableElementProvider<MissionRewardPair>, IListProvider<MissionRewardPair>
{
	[Signal]
	public delegate void HasVBucksEventHandler(bool value);
	[Export]
	bool notableMode;
	[Export]
	bool excludeTodo;
	[Export]
	RecycleListContainer missionList;
	[Export]
	Node newMissionListNode;
	[Export]
	Control loadingIcon;
	[Export]
	LineEdit searchBar;
	[Export]
	LineEdit itemSearchBar;
	[Export]
	OptionButton sortMode;
	[Export]
	Button filterNotable;
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
	Button multiFilterToggle;

	[Export]
	CheckButton[] rarityFilters;
	[Export]
	CheckButton[] zoneFilters;
	[Export]
	CheckButton[] typeFilters;
	[Export]
	SpecificMissionRewardController[] excludeRewards;

	[Export]
	CheckButton[] repeatabilityFilters;

	CheckButton[] allFilters;

	List<MissionRewardPair> rewards = [];

	IList<MissionRewardPair> IListProvider<MissionRewardPair>.List => rewards;


	public MissionRewardPair GetRecycleElement(int index) => index >= 0 && index < rewards.Count ? rewards[index] : default;

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

	IListHandler newMissionList;
	public override void _Ready()
	{
		excludeRewards ??= [];
		missionList?.SetProvider(this);
		if(newMissionListNode is IListHandler newListHandler)
		{
			newMissionList = newListHandler;
			newMissionList.LinkListProvider(this);
		}
		allFilters = rarityFilters.Union(zoneFilters).Union(typeFilters).Where(f => f is not null).ToArray();
		SetupFilters(rarityFilters);
		SetupFilters(zoneFilters);
		SetupFilters(typeFilters);
		if (sortMode is not null)
			sortMode.ItemSelected += _ => FilterMissions();
		if (filterPower is not null)
			filterPower.Toggled += _ => FilterMissions();
		if (filterNotable is not null)
			filterNotable.Toggled += _ => FilterMissions();
		if (filterPowerContainer is not null)
			filterPowerContainer.Visible = GameAccount.ActiveAccount.isOwned;
		if (filterStory is not null)
			filterStory.Toggled += _ => FilterMissions();
		if (searchBar is not null)
		{
			searchBar.TextChanged += _ => UpdateSearch();
		}
		if (itemSearchBar is not null)
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
		MissionToDoListController.OnToDoListChanged += FilterMissions;
		AppConfig.OnConfigChanged += OnConfigChanged;
		VisibilityChanged += TryRefresh;
		FilterMissions();
	}

	public override void _ExitTree()
	{
		MissionToDoListController.OnToDoListChanged -= FilterMissions;
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
		if (notableMode && 
			(
				key == "lite_notable_filter" ||
				key == "notable_count" ||
				key == "groupVBucks" ||
				key == "groupLegSurvivors"
			)
		)
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
                Mythic |
                (V-Bucks | X-Ray) |
                (Legendary Survivor !Lead) |
                REMINDER
                """;
		}
		var notableSearchInstructions = PLSearch.GenerateSearchInstructions(notableFilterText);
		//templateId="Schematic:sid_edged_axe_scavenger_sr_ore_t01"
		return item => PLSearch.EvaluateInstructions(notableSearchInstructions, item.RawData) || item.customData.ContainsKey("fools");
	}

	public static IOrderedEnumerable<GameItem> OrderByNotable(IEnumerable<GameItem> items) => OrderByNotableGen(items, StandardItemSelector);
	public static IOrderedEnumerable<MissionRewardPair> OrderByNotable(IEnumerable<MissionRewardPair> pairs) => OrderByNotableGen(pairs, RewardPairSelector);
	public static IOrderedEnumerable<MissionRewardPair> OrderByPower(IEnumerable<MissionRewardPair> pairs)
	{
		var initialOrder = pairs.OrderBy(r => -r.mission.PowerLevel);
		return ThenByRewardPriority(initialOrder, RewardPairSelector);
	}
	public static IOrderedEnumerable<MissionRewardPair> OrderByDB(IEnumerable<MissionRewardPair> pairs)
	{
		return pairs
			//.Reverse()
			.OrderBy(r => r.item.sortingTemplate?.Type == "AccountResource" && !r.item.sortingTemplate.VBucksOrXRayTickets)
			.ThenBy(r => -r.mission.TheaterIdx)
			.ThenBy(r => !r.item.sortingTemplate.VBucksOrXRayTickets)
			.ThenBy(r => -r.item.sortingTemplate.RarityLevel)
			.ThenBy(r => -r.mission.PowerLevel)
			.ThenBy(r => r.mission.missionData.missionGenerator)
			//.ThenBy(r => r.mission.Guid)
			//.Reverse().OrderBy(_=>true)
			;
	}

	public static IOrderedEnumerable<MissionRewardPair> OrderByDBPower(IEnumerable<MissionRewardPair> pairs)
	{
		return pairs
			//.Reverse()
			.OrderBy(r => r.item.sortingTemplate?.Type == "AccountResource" && !r.item.sortingTemplate.VBucksOrXRayTickets)
			.ThenBy(r => -r.mission.PowerLevel)
			.ThenBy(r => -r.mission.TheaterIdx)
			.ThenBy(r => -r.item.sortingTemplate.RarityLevel)
			.ThenBy(r => OrderByMissionNameDB(r.mission.missionGenerator), StringComparer.InvariantCultureIgnoreCase)
			//.ThenBy(r => r.mission.Guid)
			//.Reverse().OrderBy(_=>true)
			;
	}

	static GameItem StandardItemSelector(GameItem item) => item;
	static GameItem RewardPairSelector(MissionRewardPair pair) => pair.item;

	static IOrderedEnumerable<T> OrderByNotableGen<T>(IEnumerable<T> rewards, Func<T, GameItem> itemSelector)
	{
		var initialOrder = rewards.OrderBy(r => NotablePriority(itemSelector(r).sortingTemplate));
		return ThenByRewardPriority(initialOrder, itemSelector);
	}

	static int NotablePriority(GameItemTemplate template)
	{
		if (GameAccount.ActiveAccount.HasReminder(template))
			return -25; // reminder items
		if (template.RarityLevel == 6)
			return -20; // mythics
		if (template.VBucksOrXRayTickets)
			return -19; // v-bucks
						//if (template.TemplateId == "AccountResource:voucher_cardpack_bronze")
						//    return -18; // upgrade llamas
		if (template.RarityLevel == 5 && template.Type == "Worker" && template.SubType is null)
			return -17; // legendary survivor (excl. leads)
		return 0;
	}

	static IOrderedEnumerable<T> ThenByRewardPriority<T>(IOrderedEnumerable<T> items, Func<T, GameItem> itemSelector)
	{
		return items
			.ThenBy(r => !GameAccount.ActiveAccount.HasReminder(itemSelector(r).templateId))
			.ThenBy(r => !(
				itemSelector(r).template.VBucksOrXRayTickets //||
															 //r.item.sortingTemplate.HasLevel ||
															 //r.item.template.DisplayName.Contains("Llama", StringComparison.InvariantCultureIgnoreCase)
			))
			.ThenBy(r => itemSelector(r).sortingTemplate?.Type == "AccountResource")
			.ThenBy(r => -itemSelector(r).sortingTemplate.RarityLevel)
			.ThenBy(r => OrderByItemType(itemSelector(r).sortingTemplate), StringComparer.InvariantCultureIgnoreCase)
			.ThenBy(r => -itemSelector(r).DesiredLevel)
			.ThenBy(r => itemSelector(r).sortingTemplate.DisplayName.EndsWith(" XP", StringComparison.InvariantCultureIgnoreCase))
			.ThenBy(r => itemSelector(r).sortingTemplate.DisplayName)
			.ThenBy(r => itemSelector(r).sortingTemplate != itemSelector(r).template)
			.ThenBy(r => -itemSelector(r).quantity);
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
		int ventPL = (int)GameAccount.ActiveAccount.VentureRatingData.PowerLevel;

		Func<GameMission, bool> missionPredicate = null;
		Func<GameItem, bool> itemPredicate = null;
		if (notableMode)
		{
			missionPredicate = m =>
			{
				if (filterPower?.ButtonPressed == true && !m.PlayableBy(GameAccount.ActiveAccount))
					return false;
				return true;
			};
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
				.SelectMany(c =>
					c.ButtonPressed &&
					c.GetMeta("rarity", "").ToString() is string result &&
					!string.IsNullOrWhiteSpace(result) ?
					result.Split(",") : []
				)
			];
			List<string> requiredTypes = [.. typeFilters
				.SelectMany(c =>
					c.ButtonPressed &&
					c.GetMeta("type", "").ToString() is string result &&
					!string.IsNullOrWhiteSpace(result) ? result.Split(",") : []
				)
			];
			Func<GameItem, bool> notableItemFilter = null;
			bool TypeFilter(GameItem i)
			{
				if (requiredTypes.Any(t => i.sortingTemplate.TemplateId.StartsWith(t)))
					return true;
				if (requiredTypes.Contains("Hero") && i.sortingTemplate.Type == "CardPack" && i.sortingTemplate.RarityLevel != 6)
					return true;
				if (requiredTypes.Contains("Worker:manager") && i.sortingTemplate.Type == "CardPack" && i.sortingTemplate.RarityLevel == 6)
					return true;
				if (requiredTypes.Contains($"{i.sortingTemplate.Type}>{i.sortingTemplate.Category}"))
					return true;
				if (requiredTypes.Contains($"{i.sortingTemplate.Type}>_") && i.sortingTemplate.Category == null)
					return true;
				if (requiredTypes.Contains($"{i.sortingTemplate.Type}@{i.sortingTemplate.SubType}"))
					return true;
				if (requiredTypes.Contains($"{i.sortingTemplate.Type}@_") && i.sortingTemplate.SubType == null)
					return true;
				if (requiredTypes.Contains($"{i.sortingTemplate.Type}@_") && i.sortingTemplate.SubType == null)
					return true;
				return false;
			}
			bool StandardFilter(GameItem i)
			{
				if (repeatabilityFilters[0].ButtonPressed && i.zcpEquivelent is not null)
					return false;
				else if (repeatabilityFilters[1].ButtonPressed && i.zcpEquivelent is null)
					return false;
				else if (repeatabilityFilters[2].ButtonPressed && (i.zcpEquivelent is null || i.quantity < 4))
					return false;

				if (requiredRarities.Count > 0 && !requiredRarities.Contains(i.sortingTemplate.Rarity ?? "Uncommon"))
					return false;

				if (requiredTypes.Count > 0 && !TypeFilter(i))
					return false;

				if (filterNotable?.ButtonPressed == true)
				{
					notableItemFilter ??= CreateNotableFilter();
					if (!notableItemFilter(i))
						return false;
				}

				if (!PLSearch.EvaluateInstructions(itemSearchInstructions, i.RawData))
					return false;

				return true;
			}
			itemPredicate = StandardFilter;
		}

		List<MissionRewardPair> filteredRewards = [];
		string[] ignorePrefixes = [.. excludeRewards.Select(e => e.TargetTemplatePrefix).Where(p => p is not null)];

		foreach (var mission in missions)
		{
			if (missionPredicate is not null && !missionPredicate(mission))
				continue;
			foreach (var item in mission.alertRewardItems ?? [])
			{
				if (item.template.DisplayName == "Venture XP")
					continue;
				if (ignorePrefixes.Any(p => item.templateId.StartsWith(p)))
					continue;
				if (itemPredicate?.Invoke(item) == false)
					continue;
				if (excludeTodo && MissionToDoListController.IsOnToDoList(item))
					continue;
				filteredRewards.Add(new(mission, item));
			}
			foreach (var item in mission.rewardItems ?? [])
			{
				if (
					item.template.DisplayName == "Gold" ||
					item.template.DisplayName == "Venture XP" ||
					item.template.DisplayName == "People XP" ||
					item.template.DisplayName == "Schematic XP"
					)
					continue;
				if (ignorePrefixes.Any(p => item.templateId.StartsWith(p)))
					continue;
				if (itemPredicate?.Invoke(item) == false)
					continue;
				if (excludeTodo && MissionToDoListController.IsOnToDoList(item))
					continue;
				filteredRewards.Add(new(mission, item));
			}
		}

		if (notableMode)
			EmitSignalHasVBucks(filteredRewards.Any(r => r.item.template.VBucksOrXRayTickets));

		IOrderedEnumerable<MissionRewardPair> sortedRewards;

		sortedRewards = sortMode?.GetSelectedId() switch
		{
			160 => OrderByPower(filteredRewards),
			465 => OrderByDB(filteredRewards),
			333 => OrderByDBPower(filteredRewards),
			_ => OrderByNotable(filteredRewards),
		};

		rewards = [.. sortedRewards];
		int limit = AppConfig.Get("missions", "notable_count", 20);
		if (notableMode && rewards.Count > limit)
			rewards = rewards[..limit];

		if (emptyContent is not null)
			emptyContent.Visible = rewards.Count == 0;

		newMissionList?.UpdateList();
		if (missionList is null)
			return;
		missionList.UpdateList(true);
		missionList.Visible = true;
	}

	//todo: user configurable sorting rules
	static string OrderByItemType(GameItemTemplate template) => template?.Type switch
	{
		"Hero" => "000000",
		"Worker" when template.SubType is null => "00000Z",
		"Worker" => "0000ZZ",
		"AccountResource" when template.DisplayName.Contains("Perk", StringComparison.OrdinalIgnoreCase) => "000AZZ",
		"AccountResource" when template.Name.Contains("reagent_alteration", StringComparison.OrdinalIgnoreCase) => "000ZZZ",
		"AccountResource" => "00ZZZZ",
		_ => template?.Type,
	};

	static string OrderByMissionNameDB(GameItemTemplate template) => template?.DisplayName switch
	{
		"Fight Category 4 Storm" => "000000",
		"Fight Category 3 Storm" => "000001",
		"Fight Category 2 Storm" => "000002",
		"Ride The Lightning" => "000010",
		"Repair the Shelter" => "000020",
		"Resupply" => "000020",
		"Retrieve the Data" => "000025",
		"Refuel the Homebase" => "000030",
		"Fight the Storm" => "000033",
		"Elimenate and Collect" => "000040",
		"Destroy the Encampments" => "000050",
		_ => template?.DisplayName,
	};

	void ClearMissions()
	{
		loadingIcon.Visible = true;
		rewards.Clear();
		newMissionList?.UpdateList();
		if (missionList is not null)
			missionList.Visible = false;
		if (emptyContent is not null)
			emptyContent.Visible = false;
	}
}

public record struct MissionRewardPair(GameMission mission, GameItem item);