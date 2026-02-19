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
    Control multiselectButtons;
    [Export]
    Control confirmButton;
    [Export]
    Control skipButton;
    [Export]
    LineEdit searchInput;
    [Export]
    Control survivorFilters;

    public override void _Ready()
	{
		base._Ready();
        container.SetProvider(this);
        smallContainer.SetProvider(this);
        container.Visible = false;
        smallContainer.Visible = false;
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
        selectableFilter = RecyclableFilter,

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
        autoselectFilter = CreateAutorecycleFilter(),
    };

    private Config _DismantleConfig;
    public static Config DismantleConfig => instance is null ? new() : instance._DismantleConfig ??= instance._DefaultConfig with
    {
        selectableFilter = item=>!item.template.Undismantlable,

        multiselectMode = true,

        smallItems = false,

        titleText = "Dismantle",
        confirmButtonText = "Confirm Dismantle",
        autoselectButtonTex = instance.recycleIcon,

        selectedTintColor = Colors.Red,

        selectedMarkerTex = instance.recycleIcon,
    };

    public string OverriddeSurvivorSquad => CurrentConfig.overrideSurvivorSquad;

    public static async Task<GameItem[]> OpenSelector(IEnumerable<GameItem> itemOptions, Config config = null) =>
        await instance.OpenSelectorInternal(itemOptions, config);

    protected override void InitialiseSelector(IEnumerable<GameItem> itemOptions)
    {
        EmitSignalTitleChanged(CurrentConfig.titleText);
        EmitSignalConfirmButtonChanged(CurrentConfig.confirmButtonText);
        EmitSignalSkipButtonChanged(CurrentConfig.skipButtonText);
        EmitSignalAutoselectChanged(CurrentConfig.autoselectButtonTex);

        container.Visible = !CurrentConfig.smallItems;
        smallContainer.Visible = CurrentConfig.smallItems;
        activeContainer = CurrentConfig.smallItems ? smallContainer : container;

        multiselectButtons.Visible = CurrentConfig.multiselectMode;
        autoSelectButton.Visible = CurrentConfig.autoselectFilter is not null;

        base.InitialiseSelector(itemOptions);
    }

    protected override void SetDefaultSortingAndFilter() => SetSort(0);

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
