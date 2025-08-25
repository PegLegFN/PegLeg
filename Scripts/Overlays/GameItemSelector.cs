using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

public partial class GameItemSelector : ModalWindow, IRecyclableElementProvider<GameItem>
{
    public static GameItemSelector Instance { get; private set; }

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
        RestoreDefaults();
        container.SetProvider(this);
        Instance = this;
    }

    bool isSelecting;
    bool isCancelling;
    List<GameItem> items;
    List<GameItem> filteredItems;
    List<GameItem> selectedItems = [];

    public bool multiselectMode;
    public bool allowEmptySelection;
    public bool allowCancel;
    public string overrideSurvivorSquad;
    public Predicate<GameItem> selectablePredicate;
    public Predicate<GameItem> autoselectPredicate;

    public string titleText;
    public string confirmButtonText;
    public string skipButtonText;
    public Texture2D autoselectButtonTex;
    public Color unselectableTintColor;
    public Color selectedTintColor;
    public Color collectionTintColor;
    public Texture2D unselectableMarkerTex;
    public Texture2D selectedMarkerTex;
    public Texture2D collectionMarkerTex;


    public override void SetWindowOpen(bool openState)
    {
        if (isSelecting && !openState)
        {
            CancelSelection();
        }
    }

    public void RestoreDefaults()
    {
        multiselectMode = false;
        allowEmptySelection = false;
        allowCancel = true;
        overrideSurvivorSquad = null;
        selectablePredicate = null;
        autoselectPredicate = null;

        titleText = "Select an Item";
        confirmButtonText = "Confirm";
        skipButtonText = "Continue";
        autoselectButtonTex = null;
        unselectableTintColor = Color.FromHtml("#303030");
        selectedTintColor = Colors.Orange;
        collectionTintColor = Colors.Green;
        unselectableMarkerTex = unselectableIcon;
        selectedMarkerTex = defaultSelectionMarker;
        collectionMarkerTex = defaultSelectionMarker;
    }

    public static bool RecyclablePredicate(GameItem item) => 
        item.template?.Unrecyclable == false && 
        item.attributes?["favorite"]?.GetValue<bool>() != true &&
        item.attributes?["squad_id"] is null;

    public static Predicate<GameItem> GenerateAutorecyclePredicate()
    {
        var recycleFilter = GameAccount.activeAccount.GetLocalData("RecycleFilter")?.ToString() ?? "Common | Uncommon | Rare";
        var autoselectInstructions = PLSearch.GenerateSearchInstructions(recycleFilter);
        return item => PLSearch.EvaluateInstructions(autoselectInstructions, item.RawData);
    }

    public void SetRecycleDefaults()
    {
        RestoreDefaults();
        titleText = "Recycle";
        confirmButtonText = "Confirm Recycle";
        multiselectMode = true;
        selectedMarkerTex = recycleIcon;
        selectedTintColor = Colors.Red;
        collectionMarkerTex = collectionIcon;
        autoselectButtonTex = recycleIcon;
        selectablePredicate = RecyclablePredicate;
        autoselectPredicate = GenerateAutorecyclePredicate();
        //autoselectPredicate = item => item.template.RarityLevel <= 3;
    }

    public void SetDismantleDefaults()
    {
        RestoreDefaults();
        titleText = "Dismantle";
        confirmButtonText = "Confirm Dismantle";
        multiselectMode = true;
        selectedMarkerTex = recycleIcon;
        selectedTintColor = Colors.Red;
        collectionMarkerTex = recycleIcon;
        autoselectButtonTex = recycleIcon;
        selectablePredicate = item => !item.template.Undismantlable;
        autoselectPredicate = null;
    }

    GameItem emptyItem = new(null, 1, customData: new() { ["empty"] = true });
    public async Task<GameItem[]> OpenSelector(IEnumerable<GameItem> profileItems, IEnumerable<GameItem> preSelectedItems = null)
    {
        EmitSignal(SignalName.TitleChanged, titleText);
        EmitSignal(SignalName.ConfirmButtonChanged, confirmButtonText);
        EmitSignal(SignalName.SkipButtonChanged, skipButtonText);
        EmitSignal(SignalName.AutoselectChanged, autoselectButtonTex);

        multiselectButtons.Visible = multiselectMode;
        autoSelectButton.Visible = autoselectPredicate is not null;
        selectablePredicate ??= item => true;

        items = profileItems.ToList();
        if(allowEmptySelection && !multiselectMode)
            items.Insert(0, emptyItem);

        if (multiselectMode)
        {
            selectedItems = 
                preSelectedItems?
                    .Where(item => selectablePredicate.Try(item))
                    .ToList() ??
                (autoselectPredicate is not null ?
                    items
                        .Where(item => selectablePredicate.Try(item) && autoselectPredicate(item))
                        .ToList() :
                    []);
        }
        else
            selectedItems = [];

        confirmButton.Visible = selectedItems?.Any() ?? false;
        skipButton.Visible = !confirmButton.Visible && allowEmptySelection;

        SetSort(0);
        SortItems();
        container.UpdateList(true);
        isSelecting = true;
        isCancelling = false;
        base.SetWindowOpen(true);
        await Helpers.WaitForFrame();
        container.UpdateList(true);

        //GD.Print($"opening selector with {itemHandles.Count} items");
        while (isSelecting)
            await Helpers.WaitForFrame();

        base.SetWindowOpen(false);

        selectedItems.Remove(emptyItem);
        var toReturn = selectedItems.ToArray();
        selectedItems.Clear();
        items.Clear();
        RestoreDefaults();

        return isCancelling ? null : toReturn;
    }

    void AutoMarkSelection()
    {
        if (!multiselectMode || autoselectPredicate is null)
            return;
        selectedItems = items.Where(item => selectablePredicate.Try(item) && autoselectPredicate(item)).Union(selectedItems).ToList();
        SortItems();
        container.UpdateList(true);
    }

    int currentSortingIndex = 0;
    bool sortingDirty = false;
    delegate IOrderedEnumerable<GameItem> SortingFunc(IOrderedEnumerable<GameItem> items);
    SortingFunc[] sortingFunctions;
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
            if (currentSortingIndex == sortingFunctions.Length)
                currentSortingIndex = 0;
            SetSort(currentSortingIndex);
        }
        sortingDirty = false;
        SortItems();
        container.UpdateList(true);
    }

    void SetSort(int newIndex)
    {
        sortingFunctions ??=
        [
            SortByPower,
            SortByPowerAsc,
            SortByName
        ];
        currentSortingIndex = newIndex % Mathf.Max(sortingFunctions.Length, sortingFunctionNames.Length);
        currentSortingFunction = sortingFunctions[currentSortingIndex];
        EmitSignal(SignalName.SortTypeChanged, sortingFunctionNames[currentSortingIndex]);
    }

    IOrderedEnumerable<GameItem> SortByPower(IOrderedEnumerable<GameItem> items) => 
        items.ThenBy(item => -item.CalculateSurvivorRating(overrideSurvivorSquad is not null, overrideSurvivorSquad));
    IOrderedEnumerable<GameItem> SortByPowerAsc(IOrderedEnumerable<GameItem> items) =>
        items.ThenBy(item => item.CalculateSurvivorRating(overrideSurvivorSquad is not null, overrideSurvivorSquad));
    IOrderedEnumerable<GameItem> SortByName(IOrderedEnumerable<GameItem> items) => 
        items.ThenBy(item => item.template.DisplayName);

    SortingFunc currentSortingFunction;

    void SortItems()
    {
        var presortedItemHandles = items.OrderBy(item => item.profile is not null ? 1 : 0).ThenBy(item => selectedItems.Contains(item) ? 0 : 1);
        items = [.. currentSortingFunction(presortedItemHandles)];
    }

    void ConfirmSelection()
    {
        isSelecting = false;
    }

    void CancelSelection()
    {
        if (!allowCancel)
            return;
        selectedItems.Clear();
        isCancelling = true;
        isSelecting = false;
    }

    void ClearSelection()
    {
        selectedItems.Clear();
        SortItems();
        if (multiselectMode)
            container.UpdateList(true);
    }

    public bool ItemIsSelected(GameItem item) => multiselectMode && selectedItems.Contains(item);

    public void OnElementSelected(int index)
    {
        if (isSelecting && selectablePredicate.Try(items[index]))
        {
            if (multiselectMode)
            {
                if(selectedItems.Contains(items[index]))
                    selectedItems.Remove(items[index]);
                else
                    selectedItems.Add(items[index]);
                sortingDirty = true;
                bool empty = selectedItems.Count == 0;
                confirmButton.Visible = !empty;
                skipButton.Visible = empty && allowEmptySelection;
            }
            else
            {
                selectedItems.Clear();
                selectedItems.Add(items[index]);
                isSelecting = false;
            }
        }
    }

    public GameItem GetRecycleElement(int index) => 
        ((items?.Count ?? -1) > 0 && index < items.Count && index >= 0) ? items[index] : null;

    public int GetRecycleElementCount() => items?.Count ?? 0;
}
