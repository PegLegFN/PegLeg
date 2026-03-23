using Amazon.Runtime.Internal.Transform;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

public abstract partial class GameItemSelectorBase<T> : ModalWindow, 
    IRecyclableElementProvider<GameItem>, 
    ISelectableElementProvider<GameItem> 
    where T: GameItemSelectorBase<T>.BaseConfig, new()
{
    public record BaseConfig
    {
        public bool allowCancel = true;
        public bool allowEmptySelection;
        public bool quantitySelection;
        public Func<GameItem, bool> selectableFilter;
        public Func<KeyValuePair<GameItem, int>[], Task<bool>> confirmationTaskProvider;
    }

    public record MultiselectableConfig : BaseConfig
    {
        public bool multiselectMode;
        public Func<GameItem, bool> autoselectFilter;
    }

    protected bool isSelecting;
    protected bool isCancelling;
    protected List<GameItem> items = [];
    protected List<GameItem> filteredItems = [];
    protected Dictionary<GameItem, int> selectedItems = [];
    protected RecycleListContainer activeContainer;

    public static bool RecyclableFilter(GameItem item) =>
        item.template?.Unrecyclable == false &&
        item.attributes?["favorite"]?.GetValue<bool>() != true &&
        item.attributes?["squad_id"] is null &&
        item.profile?.HeroesInLoadouts.Contains(item) != true;

    public static Func<GameItem, bool> CreateAutorecycleFilter()
    {
        var recycleFilter = GameAccount.ActiveAccount.GetLocalData("RecycleFilter")?.ToString() ?? "Common | Uncommon | Rare";
        var autoselectInstructions = PLSearch.GenerateSearchInstructions(recycleFilter);
        return item => PLSearch.EvaluateInstructions(autoselectInstructions, item.RawData);
    }

    public override void _Ready()
    {
        base._Ready();
        CurrentConfig = DefaultConfigInternal;
    }

    public override void SetWindowOpen(bool openState)
    {
        if (isSelecting && !openState)
        {
            CancelSelection();
        }
    }

    protected virtual T DefaultConfigInternal => new();
    protected T CurrentConfig { get; private set; }
    private bool SupportsMultiselect(out MultiselectableConfig multiselectableConfig)
    {
        multiselectableConfig = null;
        if (CurrentConfig is MultiselectableConfig msConfig)
        {
            multiselectableConfig = msConfig;
            return true;
        }
        return false;
    }

    private bool multiselectMode;

    protected async Task<KeyValuePair<GameItem, int>[]> OpenSelectorInternal(IEnumerable<GameItem> itemOptions, T config = null)
    {
        try
        {
            ConfigureSelector(config);

            isSelecting = true;
            isCancelling = false;

            InitialiseSelector(itemOptions);

            SetDefaultSortingAndFilter();
            SortItems();

            base.SetWindowOpen(true);
            await WaitForSelection();
            base.SetWindowOpen(false);

            return GetResultItemsAndCleanup();
        }
        catch(Exception e)
        {
            GD.PushError(e.ToString().FixLogLines());
            isSelecting = false;
            return null;
        }
    }

    static bool AllFilter(GameItem _) => true;

    protected virtual void ConfigureSelector(T config)
    {
        CurrentConfig = config ?? DefaultConfigInternal;
        CurrentConfig.selectableFilter ??= item => true;
        multiselectMode = SupportsMultiselect(out var msConfig) && msConfig.multiselectMode;
    }

    protected virtual void InitialiseSelector(IEnumerable<GameItem> itemOptions)
    {
        var supportsMultiselect = SupportsMultiselect(out var msConfig);
        items = [.. itemOptions];
        if (CurrentConfig.allowEmptySelection && !multiselectMode)
            items.Insert(0, GameItem.Empty);

        if (supportsMultiselect && msConfig.multiselectMode)
        {
            var filter = msConfig.autoselectFilter ?? CurrentConfig.selectableFilter;
            selectedItems = items.Where(filter).ToDictionary(i => i, i => i.quantity);
        }
        else
            selectedItems = [];
        SelectionChanged();
    }
    
    protected virtual async Task WaitForSelection()
    {
        while (isSelecting)
        {
            await Helpers.WaitForFrame();
            if (isSelecting || isCancelling)
                continue;
            if (CurrentConfig.confirmationTaskProvider is null)
                continue;
            var result = await CurrentConfig.confirmationTaskProvider([..selectedItems]);
            if (!result)
            {
                isSelecting = true;
                if (!multiselectMode)
                    selectedItems = [];
            }
        }
    }

    protected virtual KeyValuePair<GameItem, int>[] GetResultItemsAndCleanup()
    {
        selectedItems.Remove(GameItem.Empty);
        var toReturn = selectedItems.ToArray();
        selectedItems.Clear();
        filteredItems.Clear();
        items.Clear();

        return isCancelling ? null : toReturn;
    }

    protected virtual void SetDefaultSortingAndFilter() { }
    protected virtual Func<IOrderedEnumerable<GameItem>, IOrderedEnumerable<GameItem>> SortingFunction => null;
    protected virtual Func<GameItem, bool> FilterFunction => null;

    protected void SortItems()
    {
        var presortedItems = items.OrderBy(item => item == GameItem.Empty ? 0 : 1).ThenBy(item => selectedItems.ContainsKey(item) ? 0 : 1);
        items = SortingFunction is null ? [..presortedItems] : [..SortingFunction(presortedItems)];
        FilterItems();
    }

    protected void FilterItems()
    {
        filteredItems = FilterFunction is null ? items : [.. items.Where(item => item.profile is null || IsSelected(item) || FilterFunction(item))];
        activeContainer?.UpdateList(true);
    }

    protected void AutoselectItems(bool filteredOnly)
    {
        if (!multiselectMode || !SupportsMultiselect(out var msConfig) || msConfig.autoselectFilter is null)
            return;
        var fromItems = filteredOnly ? filteredItems : items;

        KeyValuePair<GameItem, int>[] selectedKVPs = [
            ..fromItems
            .Where(CurrentConfig.selectableFilter)
            .Where(msConfig.autoselectFilter)
            .ToDictionary(i=>i, i=>i.quantity),
            ..selectedItems
        ];
        selectedItems = selectedKVPs.ToDictionary();
        SelectionChanged();
        SortItems();
    }

    protected void ConfirmSelection() => isSelecting = false;

    protected void CancelSelection()
    {
        if (!CurrentConfig.allowCancel)
            return;
        selectedItems.Clear();
        isCancelling = true;
        isSelecting = false;
    }

    protected void ClearSelection()
    {
        if (!multiselectMode)
            return;
        selectedItems.Clear();
        SortItems();
    }

    public bool IsSelectable(GameItem item) => CurrentConfig.selectableFilter.Try(item);
    public bool IsSelected(GameItem item) => multiselectMode && selectedItems.ContainsKey(item);
    public int GetSelectionQuantity(GameItem item) =>
        multiselectMode &&
        CurrentConfig.quantitySelection &&
        selectedItems.TryGetValue(item, out var count) ?
            count : 0;
    public virtual Color GetSelectableColor(GameItem item) => 
        IsSelectable(item) ? 
            (
                IsSelected(item) ? 
                    Colors.Cyan : 
                    Colors.Transparent
            ) : 
            Colors.Gray;
    public virtual Texture2D GetSelectableIcon(GameItem item) => null;

    public async void OnElementSelected(int index, string context)
    {
        if (!isSelecting || !CurrentConfig.selectableFilter.Try(filteredItems[index]))
            return;

        var quantity = filteredItems[index].quantity;
        if (CurrentConfig.quantitySelection)
        {
            //show quantity selector menu
        }
        if (multiselectMode)
        {
            if (CurrentConfig.quantitySelection)
            {
                if (quantity == 0)
                    selectedItems.Remove(filteredItems[index]);
                else if (!selectedItems.TryAdd(filteredItems[index], quantity))
                    selectedItems[filteredItems[index]] = quantity;
            }
            else
            {
                if (!selectedItems.TryAdd(filteredItems[index], quantity))
                    selectedItems.Remove(filteredItems[index]);
            }
            SelectionChanged();
        }
        else
        {
            selectedItems.Clear();
            selectedItems.Add(filteredItems[index], quantity);
            isSelecting = false;
        }
    }

    protected virtual void SelectionChanged() { }

    public GameItem GetRecycleElement(int index) =>
        ((filteredItems?.Count ?? -1) > 0 && index < filteredItems.Count && index >= 0) ? filteredItems[index] : null;

    public int GetRecycleElementCount() => filteredItems?.Count ?? 0;
}
