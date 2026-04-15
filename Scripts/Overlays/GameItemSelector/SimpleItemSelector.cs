using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

public partial class SimpleItemSelector : GameItemSelectorBase<SimpleItemSelector.Config>
{
	static SimpleItemSelector instance;

	public record Config : MultiselectableConfig
	{
		public string overrideSurvivorSquad;
		public bool showSurvivorFilters = false;
		public bool smallItems = true;

		public string titleText = "Select an Item";
		public string confirmButtonText = "Confirm";
		public string skipButtonText = "Continue";
		public Texture2D autoselectButtonTex;

		public Color unselectableTintColor = Color.FromHtml("#303030");
		public Color selectedTintColor = Colors.Orange;
		public Color collectionTintColor = Colors.Green;

		public Texture2D unselectableMarkerTex;
		public Texture2D selectedMarkerTex;
		public Texture2D collectionMarkerTex;
	}

	[Signal]
	public delegate void TitleChangedEventHandler(string title);
	[Signal]
	public delegate void ConfirmButtonChangedEventHandler(string buttonText);
	[Signal]
	public delegate void SkipButtonChangedEventHandler(string buttonText);
	[Signal]
	public delegate void AutoselectChangedEventHandler(Texture2D autoselect);
	[Signal]
	public delegate void SortTypeChangedEventHandler(string title);

	[Export]
	Texture2D defaultSelectionMarker;
	[Export]
	Texture2D recycleIcon;
	[Export]
	Texture2D collectionIcon;
	[Export]
	Texture2D unselectableIcon;
	[Export]
	Control autoSelectButton;
	[Export]
	RecycleListContainer container;
	[Export]
	RecycleListContainer smallContainer;
	[Export]
	Control[] multiselectControls;
	[Export]
	Control confirmButton;
	[Export]
	Control skipButton;
	[Export]
	LineEdit searchInput;
	[Export]
	Control survivorFilters;
	[Export]
	VirtualTabBar personalityFilter;
	[Export]
	Button trapDuraFilter;

	public override void _Ready()
	{
		base._Ready();
		container.SetProvider(this);
		smallContainer.SetProvider(this);
		container.Visible = false;
		smallContainer.Visible = false;
		searchInput.TextChanged += UpdateSearchFilters;
		personalityFilter.LatestTabChanged += UpdateFilters;
		trapDuraFilter.Pressed += () => UpdateFilters();
		instance = this;
	}

	private Config _DefaultConfig;
	public static Config DefaultConfig => instance?.DefaultConfigInternal ?? new();
	protected override Config DefaultConfigInternal => _DefaultConfig ??= new()
	{
		unselectableMarkerTex = unselectableIcon,
		selectedMarkerTex = defaultSelectionMarker,
		collectionMarkerTex = defaultSelectionMarker
	};

	private Config _RecycleConfig;
	public static Config RecycleConfig => instance is null ? new() : (instance._RecycleConfig ??= instance._DefaultConfig with
	{
		multiselectMode = true,

		smallItems = false,

		titleText = "Recycle",
		confirmButtonText = "Confirm Recycle",
		autoselectButtonTex = instance.recycleIcon,

		selectedTintColor = Colors.Red,

		selectedMarkerTex = instance.recycleIcon,
		collectionMarkerTex = instance.collectionIcon,
	}) with
	{
		selectableFilter = CreateRecycleFilter(),
		autoselectFilter = CreateAutorecycleFilter(),
	};

	private Config _DismantleConfig;
	public static Config DismantleConfig => instance is null ? new() : instance._DismantleConfig ??= instance._DefaultConfig with
	{
		selectableFilter = item => !item.template.Undismantlable,

		multiselectMode = true,

		smallItems = false,

		titleText = "Dismantle",
		confirmButtonText = "Confirm Dismantle",
		autoselectButtonTex = instance.recycleIcon,

		selectedTintColor = Colors.Red,

		selectedMarkerTex = instance.recycleIcon,
	};

	public string OverriddeSurvivorSquad => CurrentConfig.overrideSurvivorSquad;

	public static async Task<GameItem> OpenSelector(IEnumerable<GameItem> itemOptions, Config config = null) =>
		(await instance.OpenSelectorInternal(itemOptions, config) ?? []).FirstOrDefault().Key;
	public static async Task<GameItem[]> OpenMultiSelector(IEnumerable<GameItem> itemOptions, Config config = null) =>
		[.. (await instance.OpenSelectorInternal(itemOptions, config with { multiselectMode = true }) ?? []).Select(kvp => kvp.Key)];
	public static async Task<KeyValuePair<GameItem, int>> OpenQuantitySelector(IEnumerable<GameItem> itemOptions, Config config = null) =>
		(await instance.OpenSelectorInternal(itemOptions, config with { quantitySelection = true }) ?? []).FirstOrDefault();
	public static async Task<KeyValuePair<GameItem, int>[]> OpenMultiQuantitySelector(IEnumerable<GameItem> itemOptions, Config config = null) =>
		await instance.OpenSelectorInternal(itemOptions, config with { multiselectMode = true, quantitySelection = true });

	protected override void InitialiseSelector(IEnumerable<GameItem> itemOptions)
	{
		EmitSignalTitleChanged(CurrentConfig.titleText);
		EmitSignalConfirmButtonChanged(CurrentConfig.confirmButtonText);
		EmitSignalSkipButtonChanged(CurrentConfig.skipButtonText);
		EmitSignalAutoselectChanged(CurrentConfig.autoselectButtonTex);

		container.Visible = !CurrentConfig.smallItems;
		smallContainer.Visible = CurrentConfig.smallItems;
		activeContainer = CurrentConfig.smallItems ? smallContainer : container;
		survivorFilters.Visible = CurrentConfig.showSurvivorFilters;

		foreach (var ctrl in multiselectControls)
		{
			ctrl.Visible = CurrentConfig.multiselectMode;
		}
		autoSelectButton.Visible = CurrentConfig.autoselectFilter is not null;

		base.InitialiseSelector(itemOptions);
	}

	protected override void SetDefaultSortingAndFilter()
	{
		SetSort(0);
		lockFilters = true;
		personalityFilter.SetTabPressed(0);
		trapDuraFilter.ButtonPressed = false;
		searchInput.Text = "";
		lockFilters = false;
		UpdateFilters(false);
		UpdateSearchFilters(false);
	}

	int currentSortingIndex = 0;
	bool sortingDirty = false;
	Func<IOrderedEnumerable<GameItem>, IOrderedEnumerable<GameItem>>[] sortingFunctions;
	protected override Func<IOrderedEnumerable<GameItem>, IOrderedEnumerable<GameItem>> SortingFunction => sortingFunctions[currentSortingIndex];
	string[] sortingFunctionNames =
	[
		"By Power",
		"By Power (rev)",
		"By Name"
	];

	bool lockFilters = false;
	PLSearch.Instruction[] searchInstructions;
	string personalityRequirement = null;
	string setBonusRequirement = null;

	void UpdateFilters(int _) => UpdateFilters();
	void UpdateFilters(bool filterAfter = true)
	{
		personalityRequirement = personalityFilter.LatestTab switch
		{
			1 => "Adventurous",
			2 => "Analytical",
			3 => "Competitive",
			4 => "Cooperative",
			5 => "Curious",
			6 => "Dependable",
			7 => "Dreamer",
			8 => "Pragmatic",
			_ => null
		};
		setBonusRequirement = trapDuraFilter.ButtonPressed ? "Trap Durability" : null;
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
	private bool ItemFilter(GameItem item)
	{
		if (personalityRequirement is not null && item.Personality?.EndsWith(personalityRequirement) != true)
			return false;
		if (setBonusRequirement is not null && item.SetBonus?.EndsWith(setBonusRequirement) != true)
			return false;
		return PLSearch.EvaluateInstructions(searchInstructions, item.RawData);
	}

	void CycleSort()
	{
		if (!sortingDirty)
		{
			currentSortingIndex++;
			SetSort(currentSortingIndex);
		}
		sortingDirty = false;
		SortItems();
	}

	void SetSort(int newIndex)
	{
		sortingFunctions ??=
		[
			SortByPower,
			SortByPowerAsc,
			SortByName
		];
		currentSortingIndex = newIndex % Mathf.Min(sortingFunctions.Length, sortingFunctionNames.Length);
		EmitSignalSortTypeChanged(sortingFunctionNames[currentSortingIndex]);
	}

	IOrderedEnumerable<GameItem> SortByPower(IOrderedEnumerable<GameItem> items) =>
		items.ThenBy(item => -item.CalculateSurvivorRating(CurrentConfig.overrideSurvivorSquad is not null, CurrentConfig.overrideSurvivorSquad));
	IOrderedEnumerable<GameItem> SortByPowerAsc(IOrderedEnumerable<GameItem> items) =>
		items.ThenBy(item => item.CalculateSurvivorRating(CurrentConfig.overrideSurvivorSquad is not null, CurrentConfig.overrideSurvivorSquad));
	IOrderedEnumerable<GameItem> SortByName(IOrderedEnumerable<GameItem> items) =>
		items.ThenBy(item => item.template.SortingDisplayName);

	public override Color GetSelectableColor(GameItem item)
	{
		if (!CurrentConfig.selectableFilter.Try(item))
			return CurrentConfig.unselectableTintColor;
		if (!IsSelected(item))
			return Colors.Transparent;
		if (item?.isCollectedCache ?? false)
			return CurrentConfig.collectionTintColor;
		return CurrentConfig.selectedTintColor;
	}

	public override Texture2D GetSelectableIcon(GameItem item)
	{
		if (!CurrentConfig.selectableFilter.Try(item))
			return CurrentConfig.unselectableMarkerTex;
		if (!IsSelected(item))
			return null;
		if (item?.isCollectedCache ?? false)
			return CurrentConfig.collectionMarkerTex;
		return CurrentConfig.selectedMarkerTex;
	}

	protected override void SelectionChanged()
	{
		sortingDirty = true;
		confirmButton.Visible = selectedItems.Count > 0;
		skipButton.Visible = !confirmButton.Visible && CurrentConfig.allowEmptySelection;
	}
}
