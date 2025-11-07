using Godot;
using System;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

public partial class AppConfig
{
    public string inspectedAccount;
}

public partial class InventoryInterface : Control, IRecyclableElementProvider<GameItem>
{
    [Export]
    RecycleListContainer itemList;
    [Export]
    LineEdit searchBox;
    [Export]
    LineEdit targetUser;
    [Export]
    Control devAllButton;
    [Export]
    VirtualTabBar tabBar;
    [Export]
    string targetProfile;
    [Export(PropertyHint.ArrayType)]
    string[] typeFilters;
    [Export]
    bool sortByName = false;
    [Export]
    bool allowDevMode = true;
    [Export]
    Control creatorImageParent;
    Control[] creatorImages;
    [Export]
    Control inMissionIndicator;
    [Export]
    Control heavySearchWarning;

    public override void _Ready()
    {
        creatorImages = creatorImageParent.GetChildren().Select(c => (Control)c).ToArray();
        foreach (var item in creatorImages)
        {
            item.Visible = false;
        }
        if (heavySearchWarning is not null)
            heavySearchWarning.Visible = false;
        GameAccount.ActiveAccountChanged += UpdateAccount;
        itemList.SetProvider(this);
        searchBox.TextChanged += _ => LightweithtApplyFilters();
        searchBox.TextSubmitted += _ => ApplyFilters();
        var dev = AppConfig.Get("advanced", "developer", false) && allowDevMode;
        if (targetUser is not null)
        {
            targetUser.TextSubmitted += t =>
            {
                AppConfig.Set("inventory", "customUser", t);
                UpdateAccount();
            };
            targetUser.Visible = dev;
            targetUser.Text = dev ? AppConfig.Get("inventory", "customUser", "") : "";
        }
        if (devAllButton is not null)
            devAllButton.Visible = dev;
        currentTypeFilter = typeFilters[0];
        tabBar.CurrentTab = 0;
        tabBar.TabChanged += SetTypeFilter;
        AppConfig.OnConfigChanged += OnConfigChanged;
        VisibilityChanged += TryFilter;
        UpdateAccount();
    }

    public override void _ShortcutInput(InputEvent @event)
    {
        if
        (
            IsVisibleInTree() &&
            @event is InputEventKey keyEvent && 
            keyEvent.Keycode == Key.I && 
            keyEvent.Pressed &&
            keyEvent.ShiftPressed &&
            !keyEvent.CtrlPressed &&
            keyEvent.AltPressed && 
            currentProfile is not null
        )
        {
            DevTextOverlay.ShowText(currentProfile.statAttributes.ToString());
        }
    }

    private void OnConfigChanged(string section, string key, JsonValue val)
    {
        if (!(section == "advanced" && key == "developer") && !(section == "inventory" && key == "customUser"))
            return;

        bool dev = AppConfig.Get("advanced", "developer", false) && allowDevMode;
        if (devAllButton is not null)
            devAllButton.Visible = dev;
        if (targetUser is not null)
        {
            targetUser.Visible = dev;
            targetUser.Text = dev ? AppConfig.Get("inventory", "customUser", "") : "";
            if (!dev && string.IsNullOrEmpty(currentTypeFilter))
            {
                currentTypeFilter = typeFilters[0];
                tabBar.CurrentTab = 0;
            }
            UpdateAccount();
        }
    }

    public override void _ExitTree()
    {
        GameAccount.ActiveAccountChanged -= UpdateAccount;
        if (currentProfile is not null)
            currentProfile.OnProfileChanged -= ApplyFilters;
    }

    bool filterNew;
    public void SetNewFilter(bool value)
    {
        filterNew = value;
        ApplyFilters();
    }

    bool filterFavorite;
    public void SetFavoriteFilter(bool value)
    {
        filterFavorite = value;
        ApplyFilters();
    }

    void SetTypeFilter(int index)
    {
        if (index < 0 || index >= typeFilters.Length)
            return;
        currentTypeFilter = typeFilters[index];
        ApplyFilters();
    }

    public void ToggleSortMode() => SetSortMode(!sortByName);
    public void SetSortMode(bool sortByName)
    {
        if (sortByName == this.sortByName)
            return;
        this.sortByName = sortByName;
        ApplySorting();
    }

    GameItem[] filteredItems;
    GameItem[] currentItems;
    string currentTypeFilter = "";
    public int GetRecycleElementCount() => currentItems?.Length ?? 0;
    public GameItem GetRecycleElement(int index) => currentItems?[index];
    GameProfile currentProfile;

    async void UpdateAccount()
    {
        accountDirty = true;
        if (!IsVisibleInTree())
            return;
        accountDirty = false;

        filteredItems = [];
        ApplySorting();
        var account = GameAccount.activeAccount;
        if (!string.IsNullOrEmpty(targetUser?.Text) && allowDevMode)
        {
            if (targetUser.Text.Length==32)
                account = GameAccount.GetOrCreateAccount(targetUser.Text);
            else
                account = (await GameAccount.SearchForAccount(targetUser?.Text)) ?? account;
        }
        if (allowDevMode)
            GD.Print("Inventory target: " + account?.accountId);
        if (targetProfile != FnProfileTypes.AccountItems && !await account.Authenticate())
            return;

        foreach (var image in creatorImages)
        {
            image.Visible = account.accountId == image.Name;
        }

        if(currentProfile is not null)
        {
            currentProfile.OnProfileChanged -= ApplyFilters;
        }
        currentProfile = await account.GetProfile(targetProfile).Query();

        inMissionIndicator.Visible = !account.isOwned && currentProfile.statAttributes["quest_manager"]?["objectiveDeferral"] is not null;

        currentProfile.OnProfileChanged += ApplyFilters;
        ApplyFilters();
    }

    public async void BulkRecycle()
    {
        if (targetProfile != FnProfileTypes.AccountItems || currentProfile?.hasProfile != true || !await currentProfile.account.Authenticate())
            return;

        if (filteredItems.Length == 0)
            return;

        //foreach (var item in filteredItems)
        //{
        //    item.GetSearchTags();
        //    item.GenerateRawData();
        //}
        var loadoutHeroes = currentProfile
            .GetItems("CampaignHeroLoadout")
            .SelectMany(loadout =>
                loadout.attributes["crew_members"]
                .AsObject()
                .Select(kvp => kvp.Value.ToString())
            )
            .Distinct()
            .ToList();
        GameItemSelector.Instance.SetRecycleDefaults();
        GameItemSelector.Instance.selectablePredicate = item =>
        {
            if (!GameItemSelector.RecyclablePredicate(item))
                return false;
            if (loadoutHeroes.Contains(item.uuid))
                return false;
            return true;
        };
        var toRecycle = await GameItemSelector.Instance.OpenSelector(filteredItems, null);
        if ((toRecycle?.Length ?? 0) > 0 && await currentProfile.account.Authenticate())
        {
            JsonObject content = new()
            {
                ["targetItemIds"] = new JsonArray(toRecycle.Select(item => (JsonNode)item.uuid).ToArray())
            };
            using var _ = LoadingOverlay.CreateToken();
            await currentProfile.PerformOperation("RecycleItemBatch", content);
            ApplyFilters();
        }
    }

    public void BulkMarkSeen()
    {
        if (targetProfile != FnProfileTypes.AccountItems || currentProfile?.hasProfile != true)
            return;
        if (filteredItems.Length == 0)
            return;
        var unseenItems = filteredItems.Where(i => !i.IsSeen).ToArray();
        currentProfile.MarkItemsSeen(unseenItems);
    }

    //public async void BulkDismantle()
    //{
    //    //implement item amount selection in recycling
    //    return;

    //    if (targetProfile != FnProfileTypes.Backpack || displayedAccount is null || !await displayedAccount.Authenticate())
    //        return;

    //    if (filteredItems.Any())
    //    {
    //        //foreach (var item in filteredItems)
    //        //{
    //        //    item.GetSearchTags();
    //        //    item.GenerateRawData();
    //        //}
    //        GameItemSelector.Instance.SetDismantleDefaults();
    //        var toRecycle = await GameItemSelector.Instance.OpenSelector(filteredItems, null);
    //        if ((toRecycle?.Length ?? 0) > 0 && await displayedAccount.Authenticate())
    //        {
    //            JsonObject content = new()
    //            {
    //                ["targetItemIds"] = new JsonArray(toRecycle.Select(item => (JsonNode)item.uuid).ToArray())
    //            };
    //            using var _ = LoadingOverlay.CreateToken();
    //            await displayedAccount.GetProfile(FnProfileTypes.AccountItems).PerformOperation("RecycleItemBatch", content);
    //        }
    //    }
    //}


    bool accountDirty = false;
    bool itemsDirty = false;

    async void TryFilter()
    {
        await Helpers.WaitForFrame();
        if (accountDirty)
        {
            itemsDirty = false;
            totalItemCount = null;
            UpdateAccount();
        }
        else if (itemsDirty)
        {
            ApplyFilters();
        }
    }


    int? totalItemCount;
    void LightweithtApplyFilters()
    {
        totalItemCount ??= currentProfile?.GetItems().Length;
        if ((totalItemCount ?? 0) < 3500)
            ApplyFilters();
        else if (heavySearchWarning is not null)
            heavySearchWarning.Visible = true;


    }

    bool isFiltering = false;
    async void ApplyFilters()
    {
        itemsDirty = true;
        if (isFiltering || currentProfile?.hasProfile != true || !IsVisibleInTree())
            return;
        totalItemCount = null;
        itemsDirty = false;

        if (heavySearchWarning is not null)
            heavySearchWarning.Visible = false;

        var possibleTypes = 
            currentTypeFilter
            .Split(',')
            .Select(s => s.Trim())
            .Where(s=>!string.IsNullOrEmpty(s));
        if (!possibleTypes.Any())
            possibleTypes = null;

        var instructions = PLSearch.GenerateSearchInstructions(searchBox.Text);
        var allItems = currentProfile.GetItems();
        totalItemCount = allItems.Length;
        currentItems = [];
        itemList.UpdateList(true);
        GameItem[] resultItems = [];
        GameItem[] FilterFunc() => 
            [.. allItems
                .Where(item =>
                    (item.template is not null || AppConfig.Get("advanced", "developer", false)) &&
                    (!filterNew || !item.IsSeen) &&
                    (!filterFavorite || item.IsFavourited) &&
                    (possibleTypes?.Contains(item.template?.Type) ?? true) &&
                    PLSearch.EvaluateInstructions(instructions, item.RawData)
                )
            ];
        isFiltering = true;
        if ((totalItemCount ?? 0) < 3500)
            resultItems = FilterFunc();
        else
            await Task.Run(() => resultItems = FilterFunc());
        isFiltering = false;
        filteredItems = resultItems;
        ApplySorting();
    }

    public void ApplySorting()
    {
        var resultItems = filteredItems
            .OrderBy(i => i.template is null)
            .ThenBy(i => !(i.attributes?["favorite"]?.GetValue<bool>() ?? false))
            .ThenBy(i => !i.template?.CanBeLeveled)
            .ThenBy(i => i.template?.Type);

        if (sortByName)
            resultItems = resultItems.ThenBy(i => i.template?.SortingDisplayName);

        resultItems = resultItems
            //.ThenBy(i => i.template.Category)
            .ThenBy(i => -i.Rating)
            .ThenBy(i => -i.template?.RarityLevel)
            .ThenBy(i => i.template?.Type == "Ingredient" ? -i.TotalQuantity : 1)
            .ThenBy(i => -i.quantity);

        if (!sortByName)
            resultItems = resultItems.ThenBy(i => i.template?.SortingDisplayName);


        currentItems = [.. resultItems];
        itemList.UpdateList(true);
    }

    public void OnElementSelected(int index, string context)
    {
        GameItemViewer.Instance.ShowItem(currentItems[index]);
    }
}
