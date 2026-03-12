using Amazon;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

public partial class HeroLoadoutSlotSelector : Control, IRecyclableElementProvider<GameItem>, ISelectableElementProvider<GameItem>
{
    [Export]
    bool useActiveAccount = true;
    [Export]
    HeroLoadoutEntry linkedPanel;
    [Export]
    HeroLoadoutEntry blueprintPreview;
    [Export]
    RecycleListContainer loadoutList;
    [Export]
    LineEdit searchBar;
    [Export]
    Button setAsActiveButton;
    [Export]
    Button createBlueprintButton;
    [Export]
    Button loadBlueprintButton;
    [Export]
    Button deleteBlueprintButton;
    [Export]
    Button cancelLoadBlueprintButton;
    [Export]
    VirtualTabBar listMode;

    public override void _Ready()
    {
        loadoutList.SetProvider(this);

        setAsActiveButton.Visible = false;
        createBlueprintButton.Visible = false;
        loadBlueprintButton.Visible = false;
        deleteBlueprintButton.Visible = false;
        cancelLoadBlueprintButton.Visible = false;

        searchBar.TextChanged += UpdateFilter;
        listMode.LatestTabChanged += UpdateMode;

        if (!useActiveAccount)
            return;

        Helpers.Defer(() =>
        {
            GameAccount.ActiveAccountChanged += SetActiveAccount;
            SetAccount(GameAccount.ActiveAccount);
        });
    }

    public override void _ExitTree()
    {
        GameAccount.ActiveAccountChanged -= SetActiveAccount;
        if (currentAccount is not null)
            currentAccount.OnAccountUpdated -= UpdateAccount;
    }

    void SetActiveAccount() => SetAccount(GameAccount.ActiveAccount);

    public enum SelectionMode
    {
        None,
        Copy,
        Swap,
        Move,
        LoadBlueprint,
    }
    SelectionMode selectionMode = SelectionMode.None;
    GameItem selectionTarget;

    GameAccount currentAccount;
    GameItem[] loadouts = [];
    PLSearch.Instruction[] searchFilter = [];
    List<GameItem> filteredLoadouts = [];

    public void SetAccount(GameAccount account)
    {
        if(currentAccount is not null)
        {
            currentAccount.OnAccountUpdated -= UpdateAccount;
        }
        currentAccount = account;
        if (currentAccount is not null)
        {
            currentAccount.OnAccountUpdated += UpdateAccount;
        }
        UpdateAccount();
    }
    //
    void UpdateAccount()
    {
        if (selectionMode == SelectionMode.LoadBlueprint)
            CompleteSelectedBlueprintLoading();
        else
            UpdateMode();
    }

    public async void SetSelectedAsActive()
    {
        if (listMode.LatestTab != 0 || linkedPanel.currentItem is not GameItem visibleLoadout)
            return;
        var currentSelected = loadouts.FirstOrDefault(l => l.uuid == visibleLoadout.profile?.statAttributes?["selected_hero_loadout"]?.ToString());
        //using var _ = LoadingOverlay.CreateToken();
        setAsActiveButton.Visible = false;
        await visibleLoadout.profile.PerformOperation("SetActiveHeroLoadout", $$"""
            {
                "selectedLoadout": "{{visibleLoadout.uuid}}"
            }
            """);
        currentSelected?.NotifyChanged();
        visibleLoadout?.NotifyChanged();
    }

    public async void CreateBlueprintFromSelected()
    {
        if (listMode.LatestTab != 0 || linkedPanel.currentItem is not GameItem visibleLoadout)
            return;
        var confirm = await GenericConfirmationWindow.ShowConfirmation("Create new Blueprint from Slot?");
        if (confirm != true)
            return;
        visibleLoadout.profile.account.CreateHeroLoadoutBlueprint(visibleLoadout);
    }

    public async void DeleteSelectedBlueprint()
    {
        if (listMode.LatestTab != 1 || linkedPanel.currentItem is not GameItem visibleLoadout)
            return;
        var confirm = await GenericConfirmationWindow.ShowConfirmation("Delete Blueprint?");
        if (confirm != true)
            return;
        visibleLoadout.profile.account.RemoveHeroLoadoutBlueprint(visibleLoadout);
        UpdateMode();
    }

    public void LoadSelectedBlueprint()
    {
        if (listMode.LatestTab != 1)
            return;

        //resolve selected blueprint to loadout
        var profile = GameAccount.ActiveAccount.GetProfile(FnProfileTypes.AccountItems);
        JsonObject newAttributes = [];
        JsonObject crewMembers = new()
        {
            ["commanderslot"] = selectionTarget.attributes["crew_members"]?["commanderslot"].Deserialize<GameAccount.LoadoutBlueprintHero>(Helpers.JsonOptions.Fields).ResolveHeroUUID(profile)
        };
        for (int i = 0; i < 5; i++)
        {
            crewMembers[$"followerslot{i + 1}"] = selectionTarget.attributes["crew_members"]?[$"followerslot{i + 1}"]?
                .Deserialize<GameAccount.LoadoutBlueprintHero>(Helpers.JsonOptions.Fields).ResolveHeroUUID(profile) ?? "";
        }
        newAttributes["crew_members"] = crewMembers;
        if (profile.GetFirstTemplateItem(selectionTarget.attributes["team_perk"]?.ToString()) is GameItem tPerk)
            newAttributes["team_perk"] = tPerk.uuid;
        var blueprintGadgets = selectionTarget.attributes["gadgets"]?.Deserialize<string[]>() ?? [];
        JsonArray gadgets = [];
        if (blueprintGadgets.Length > 0)
            gadgets.Add(new JsonObject() { ["gadget"] = blueprintGadgets[0] });
        if (blueprintGadgets.Length > 1)
            gadgets.Add(new JsonObject() { ["gadget"] = blueprintGadgets[1] });
        newAttributes["gadgets"] = gadgets;
        var customName = selectionTarget.attributes["displayName"]?.ToString();
        selectionTarget = new(profile, null, new JsonObject() //this is scuffed
        {
            ["templateId"] = "CampaignHeroLoadout:defaultloadout",
            ["attributes"] = newAttributes,
        });
        if (customName is not null)
            selectionTarget.customData["displayName"] = customName;

        linkedPanel?.SetItem(selectionTarget);
        if (blueprintPreview is not null)
        {
            blueprintPreview.Visible = true;
            blueprintPreview.SetItem(selectionTarget);
            blueprintPreview.ForceSelectionVisuals(true, Colors.Green);
        }

        selectionMode = SelectionMode.LoadBlueprint;
        listMode.SetTabPressed(0);
        listMode.Visible = false;

        UpdateFilter();
        loadBlueprintButton.Visible = false;
        deleteBlueprintButton.Visible = false;
        cancelLoadBlueprintButton.Visible = true;
    }

    public void CompleteSelectedBlueprintLoading()
    {
        listMode.Visible = true;
        if (blueprintPreview is not null)
            blueprintPreview.Visible = false;
        selectionMode = SelectionMode.None;
        UpdateMode();
        cancelLoadBlueprintButton.Visible = false;
    }

    public GameItem GetRecycleElement(int index) => filteredLoadouts[index];
    public int GetRecycleElementCount() =>
        filteredLoadouts.Count;

    void SelectDefault()
    {
        var profile = currentAccount?.GetProfile(FnProfileTypes.AccountItems);
        if (profile is null)
        {
            ClearSelection();
            return;
        }
        var defaultLoadoutUUID = profile?.statAttributes?["selected_hero_loadout"]?.ToString();
        SetSelection(SelectionMode.None, Array.IndexOf(loadouts, profile?.GetItem(defaultLoadoutUUID)));
    }

    void ClearSelection() => SetSelection(SelectionMode.None, -1);

    void SetSelection(SelectionMode mode, int index)
    {
        var newTarget = index < 0 ? null : GetRecycleElement(index);
        if (selectionMode == mode && selectionTarget == newTarget)
            return;

        var prevSelected = selectionTarget;
        selectionTarget = newTarget;
        prevSelected?.NotifyChanged();
        selectionTarget?.NotifyChanged();

        selectionMode = mode;
        searchBar.Editable = selectionMode == SelectionMode.None;
        if (mode != SelectionMode.None)
            UpdateFilter();

        if (selectionMode == SelectionMode.None)
        {
            linkedPanel?.SetItem(selectionTarget);
            var selectedUUID = selectionTarget?.profile?.statAttributes?["selected_hero_loadout"]?.ToString();
            setAsActiveButton.Visible = selectionTarget?.profile is not null && (selectedUUID != selectionTarget?.uuid || selectedUUID is null);
            createBlueprintButton.Visible = selectionTarget is not null && selectionTarget?.templateId != GameAccount.HeroLoadoutBlueprintTID;
            loadBlueprintButton.Visible = selectionTarget?.templateId == GameAccount.HeroLoadoutBlueprintTID;
            deleteBlueprintButton.Visible = loadBlueprintButton.Visible;
        }
    }

    private void UpdateFilter(string _) => UpdateFilter();
    void UpdateFilter()
    {
        searchFilter = searchBar.Editable ? PLSearch.GenerateSearchInstructions(searchBar.Text) : [];
        UpdateList();
    }

    void UpdateMode(int tab) => UpdateMode();
    void UpdateMode()
    {
        if (selectionMode != SelectionMode.LoadBlueprint)
            ClearSelection();
        //switch between loadout slots and loadout blueprints
        if (listMode.LatestTab == 0 || selectionMode == SelectionMode.LoadBlueprint)
        {
            var profile = currentAccount?.GetProfile(FnProfileTypes.AccountItems);
            if (profile is null)
            {
                loadouts = [];
                UpdateList();
                return;
            }

            loadouts = [.. profile?.GetItems("CampaignHeroLoadout").OrderBy(i => i.attributes?["loadout_index"]?.GetValue<int>() ?? 0).ToArray() ?? []];

            if (selectionMode != SelectionMode.LoadBlueprint)
            {
                UpdateList();
                SelectDefault();
            }
        }
        else
        {
            loadouts = currentAccount?.HeroLoadoutBlueprints ?? [];
            UpdateList();
            SetSelection(SelectionMode.None, loadouts.Length > 0 ? 0 : -1);
        }
    }

    void UpdateList()
    {
        filteredLoadouts.Clear();
        if (searchFilter.Length == 0)
        {
            filteredLoadouts.AddRange(loadouts);
            loadoutList.UpdateList(true);
            return;
        }

        filteredLoadouts.AddRange(loadouts.Where(loadout =>
        {
            string name = null;

            if (loadout.profile?.account.GetCustomNameForLoadoutSlot(loadout) is string slotName)
                name = slotName;
            else if (loadout.attributes?["displayName"]?.ToString() is string blueprintName)
                name = blueprintName;

            if (name is null)
                return false;
            return PLSearch.EvaluateInstructions(searchFilter, [name]);
        }));

        filteredLoadouts.AddRange(loadouts.Except(filteredLoadouts).Where(loadout =>
        {
            GameItem commanderItem = null;

            if (loadout.profile?.GetItem(loadout.attributes?["crew_members"]?["commanderslot"]?.ToString()) is GameItem slotCommander)
                commanderItem = slotCommander;
            else if (GameItemTemplate.Get(loadout.attributes?["crew_members"]?["commanderslot"]?.ToString()) is GameItemTemplate bpCommander)
                commanderItem = bpCommander.CreateInstance();

            if (commanderItem is null)
                return false;
            var template = commanderItem.template;
            return PLSearch.EvaluateInstructions(searchFilter, commanderItem.CustomSearchObject([template.DisplayName, ..template.GetHeroAbilities().Select(a => a.DisplayName)]));
        }));

        filteredLoadouts.AddRange(loadouts.Except(filteredLoadouts).Where(loadout =>
        {
            GameItem teamPerkItem = null;

            if (loadout.profile?.GetItem(loadout.attributes?["team_perk"]?.ToString()) is GameItem slotTeamPerk)
                teamPerkItem = slotTeamPerk;
            else if (GameItemTemplate.Get(loadout.attributes?["team_perk"]?.ToString()) is GameItemTemplate bpTeamPerk)
                teamPerkItem = bpTeamPerk.CreateInstance();

            if (teamPerkItem is null)
                return false;
            var template = teamPerkItem.template;
            return PLSearch.EvaluateInstructions(searchFilter, teamPerkItem.CustomSearchObject([template.DisplayName]));
        }));

        filteredLoadouts.AddRange(loadouts.Except(filteredLoadouts).Where(loadout =>
        {
            for (int i = 0; i < 5; i++)
            {
                GameItem supportItem = null;

                if (loadout.profile?.GetItem(loadout.attributes?["crew_members"]?[$"followerslot{i + 1}"]?.ToString()) is GameItem slotSupport)
                    supportItem = slotSupport;
                else if (GameItemTemplate.Get(loadout.attributes?["crew_members"]?[$"followerslot{i + 1}"]?.ToString()) is GameItemTemplate bpSupport)
                    supportItem = bpSupport.CreateInstance();

                if (supportItem is null)
                    continue;
                var template = supportItem.template;
                if (PLSearch.EvaluateInstructions(searchFilter, supportItem.CustomSearchObject([template.DisplayName, template.GetHeroAbilities()[0].DisplayName])))
                    return true;
            }
            return false;
        }));

        loadoutList.UpdateList(true);
    }

    public async void OnElementSelected(int index, string context)
    {
        if(selectionMode != SelectionMode.LoadBlueprint)
        {
            if (context == "move")
            {
                SetSelection(SelectionMode.Move, index);
                return;
            }

            if (context == "swap")
            {
                SetSelection(SelectionMode.Swap, index);
                return;
            }

            if (context == "copy")
            {
                SetSelection(SelectionMode.Copy, index);
                return;
            }
        }

        if (context != "")
            return;

        if ((selectionMode == SelectionMode.Swap || selectionMode == SelectionMode.Move) && selectionTarget?.profile?.account.isOwned == true)
        {
            var destinationTarget = GetRecycleElement(index);

            if (selectionTarget == destinationTarget)
            {
                SelectDefault();
                return;
            }

            var account = selectionTarget.profile.account;
            var sourceName = account.GetCustomNameForLoadoutSlot(selectionTarget);
            var destinationName = account.GetCustomNameForLoadoutSlot(destinationTarget);

            var profile = selectionTarget.profile;
            var sourceUuid = selectionTarget.uuid;
            var source = selectionTarget.Clone();
            var destinationUuid = destinationTarget.uuid;
            var destination = destinationTarget.Clone();

            using var _ = LoadingOverlay.CreateToken();

            if (selectionMode == SelectionMode.Swap)
            {
                //GD.Print($"applying swap with : " + (index + 1));
                //basic swap
                await profile.CopyLoadoutToItem(destination, sourceUuid);
                account.SetCustomNameForLoadoutSlot(destinationTarget, sourceName);
                await profile.CopyLoadoutToItem(source, destinationUuid);
                account.SetCustomNameForLoadoutSlot(selectionTarget, destinationName);

                SelectDefault();
                return;
            }

            //GD.Print($"applying move with : " + (index + 1));
            int srcIdx = source.attributes["loadout_index"]?.GetValue<int>() ?? 0;
            int targetIdx = destination.attributes["loadout_index"]?.GetValue<int>() ?? 0;

            if (srcIdx < targetIdx)
            {
                //shift slots up
                for (int i = srcIdx; i < targetIdx; i++)
                {
                    var item = GetRecycleElement(i);
                    var nextItem = GetRecycleElement(i + 1);
                    await profile.CopyLoadoutToItem(nextItem, item?.uuid);
                    account.SetCustomNameForLoadoutSlot(item, account.GetCustomNameForLoadoutSlot(nextItem));
                }
            }
            else
            {
                //shift slots down
                for (int i = srcIdx; i > targetIdx; i--)
                {
                    var item = GetRecycleElement(i);
                    var prevItem = GetRecycleElement(i - 1);
                    await profile.CopyLoadoutToItem(prevItem, item?.uuid);
                    account.SetCustomNameForLoadoutSlot(item, account.GetCustomNameForLoadoutSlot(prevItem));
                }
            }
            await profile.CopyLoadoutToItem(source, destinationUuid);
            account.SetCustomNameForLoadoutSlot(destinationTarget, sourceName);

            SelectDefault();
            return;
        }

        if (selectionMode == SelectionMode.Copy && selectionTarget is not null)
        {
            var target = GetRecycleElement(index);
            if (selectionTarget != target)
            {
                using var _ = LoadingOverlay.CreateToken();
                await selectionTarget.profile.CopyLoadoutToItem(selectionTarget, target.uuid);
            }

            SelectDefault();
            return;
        }

        if (selectionMode == SelectionMode.LoadBlueprint && selectionTarget is not null)
        {
            var confirm = await GenericConfirmationWindow.ShowConfirmation("Load Blueprint into Slot?", warningText:"This will overwrite the existing loadout.");
            if (confirm != true)
                return;
            var target = GetRecycleElement(index);
            if (selectionTarget != target)
            {
                using var _ = LoadingOverlay.CreateToken();
                await selectionTarget.profile.CopyLoadoutToItem(selectionTarget, target.uuid);
                selectionTarget.profile.account.SetCustomNameForLoadoutSlot(target, selectionTarget.attributes?["displayName"]?.ToString());
            }
            CompleteSelectedBlueprintLoading();
            return;
        }

        SetSelection(SelectionMode.None, index);
    }

    public bool IsSelected(GameItem loadout) => selectionTarget is not null && loadout == selectionTarget;
    public static readonly Color transparantWhite = Colors.White.Lerp(Colors.Transparent, 0.5f);
    public Color GetSelectableColor(GameItem _) => selectionMode switch
    {
        SelectionMode.Copy => Colors.Green,
        SelectionMode.Swap => Colors.Blue,
        SelectionMode.Move => Colors.Blue,
        SelectionMode.None => transparantWhite,
        _ => Colors.Transparent
    };
}
