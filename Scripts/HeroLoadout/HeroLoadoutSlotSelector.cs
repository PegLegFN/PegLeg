using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

public partial class HeroLoadoutSlotSelector : Control, IRecyclableElementProvider<GameItem>, ISelectableElementProvider<GameItem>
{
    [Export]
    bool useActiveAccount = true;
    [Export]
    HeroLoadoutEntry linkedPanel;
    [Export]
    RecycleListContainer loadoutList;
    [Export]
    LineEdit searchBar;

    public override async void _Ready()
    {
        loadoutList.SetProvider(this);
        await Helpers.WaitForFrame();
        if (useActiveAccount)
        {
            GameAccount.ActiveAccountChanged += UpdateActive;
            SetAccount(GameAccount.ActiveAccount);
        }
        searchBar.TextChanged += UpdateFilter;
    }

    public override void _ExitTree()
    {
        GameAccount.ActiveAccountChanged -= UpdateActive;
    }

    void UpdateActive()
    {
        SetAccount(GameAccount.ActiveAccount);
    }

    public enum SelectionMode
    {
        None,
        Copy,
        Swap,
        Move
    }
    SelectionMode selectionMode = SelectionMode.None;
    GameItem selectionTarget;

    GameItem[] loadouts = [];
    PLSearch.Instruction[] searchFilter;
    List<GameItem> filteredLoadouts = [];

    public void SetAccount(GameAccount account)
    {
        ClearSelection();
        var profile = account?.GetProfile(FnProfileTypes.AccountItems);
        if (profile is null)
        {
            loadouts = [];
            UpdateList();
            linkedPanel?.ClearItem();
            return;
        }
        var defaultLoadoutUUID = profile?.statAttributes?["selected_hero_loadout"]?.ToString();
        loadouts = [.. profile?.GetItems("CampaignHeroLoadout").OrderBy(i => i.attributes?["loadout_index"]?.GetValue<int>() ?? 0).ToArray() ?? []];
        UpdateList();
        linkedPanel?.SetItem(profile?.GetItem(defaultLoadoutUUID));
    }

    public async void SetSelectedAsActive()
    {
        if (linkedPanel.currentItem is GameItem visibleLoadout)
        {
            var currentSelected = loadouts.FirstOrDefault(l => l.uuid == visibleLoadout.profile?.statAttributes?["selected_hero_loadout"]?.ToString());
            using var _ = LoadingOverlay.CreateToken();
            await visibleLoadout.profile.PerformOperation("SetActiveHeroLoadout", $$"""
            {
                "selectedLoadout": "{{visibleLoadout.uuid}}"
            }
            """);
            currentSelected?.NotifyChanged();
            visibleLoadout?.NotifyChanged();
        }
    }

    public GameItem GetRecycleElement(int index) => filteredLoadouts[index];
    public int GetRecycleElementCount() =>
        filteredLoadouts.Count;

    void ClearSelection() => SetSelection(SelectionMode.None, -1);
    void SetSelection(SelectionMode mode, int index)
    {
        //GD.Print($"mode : {mode}, target: {index + 1}");
        selectionTarget = index < 0 ? null : GetRecycleElement(index);
        selectionMode = mode;
        UpdateFilter();
    }

    private void UpdateFilter(string _) => UpdateFilter();
    void UpdateFilter()
    {
        searchBar.Editable = selectionMode == SelectionMode.None;
        searchFilter = searchBar.Editable ? PLSearch.GenerateSearchInstructions(searchBar.Text) : [];
        UpdateList();
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

        //if a loadout slot has a custom name, search that first

        filteredLoadouts.AddRange(loadouts.Where(loadout =>
        {
            var commanderGuid = loadout.attributes?["crew_members"]?["commanderslot"]?.ToString();
            if(commanderGuid is null || loadout.profile.GetItem(commanderGuid) is not GameItem c)
                return false;
            return PLSearch.EvaluateInstructions(searchFilter, c.CustomSearchObject(() => [c.template.DisplayName, .. c.template.GetHeroAbilities().Select(a => a.DisplayName)]));
        }));

        filteredLoadouts.AddRange(loadouts.Except(filteredLoadouts).Where(loadout =>
        {
            var teamPerkGuid = loadout.attributes?["team_perk"]?.ToString();
            if (teamPerkGuid is null || loadout.profile.GetItem(teamPerkGuid) is not GameItem tp)
                return false;
            return PLSearch.EvaluateInstructions(searchFilter, tp.CustomSearchObject(() => [tp.template.DisplayName]));
        }));

        filteredLoadouts.AddRange(loadouts.Except(filteredLoadouts).Where(loadout =>
        {
            for (int i = 0; i < 5; i++)
            {
                var supportGuid = loadout.attributes["crew_members"][$"followerslot{i + 1}"]?.ToString();
                if (supportGuid is null || loadout.profile.GetItem(supportGuid) is not GameItem supp)
                    continue;
                if (PLSearch.EvaluateInstructions(searchFilter, supp.CustomSearchObject(() => [supp.template.DisplayName, supp.template.GetHeroAbilities()[0].DisplayName])))
                    return true;
            }
            return false;
        }));

        loadoutList.UpdateList(true);
    }

    public async void OnElementSelected(int index, string context)
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

        if ((selectionMode == SelectionMode.Swap || selectionMode == SelectionMode.Move) && selectionTarget is not null)
        {
            var target = GetRecycleElement(index);

            if (selectionTarget == target)
            {
                ClearSelection();
                return;
            }

            var profile = selectionTarget.profile;
            var sourceUuid = selectionTarget.uuid;
            var source = selectionTarget.Clone();
            var targetUuid = target.uuid;
            target = target.Clone();

            using var _ = LoadingOverlay.CreateToken();

            if (selectionMode == SelectionMode.Swap)
            {
                //GD.Print($"applying swap with : " + (index + 1));
                //basic swap
                await profile.CopyLoadoutToItem(target, sourceUuid);
                await profile.CopyLoadoutToItem(source, targetUuid);

                ClearSelection();
                return;
            }

            //GD.Print($"applying move with : " + (index + 1));
            int srcIdx = source.attributes["loadout_index"]?.GetValue<int>() ?? 0;
            int targetIdx = target.attributes["loadout_index"]?.GetValue<int>() ?? 0;

            if (srcIdx < targetIdx)
            {
                //shift slots up
                for (int i = srcIdx; i < targetIdx; i++)
                {
                    await profile.CopyLoadoutToItem(GetRecycleElement(i + 1), GetRecycleElement(i)?.uuid);
                }
            }
            else
            {
                //shift slots down
                for (int i = srcIdx; i > targetIdx; i--)
                {
                    await profile.CopyLoadoutToItem(GetRecycleElement(i - 1), GetRecycleElement(i)?.uuid);
                }
            }
            await profile.CopyLoadoutToItem(source, targetUuid);

            ClearSelection();
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
            ClearSelection();
            return;
        }

        linkedPanel.SetItem(GetRecycleElement(index));
    }

    public bool IsSelected(GameItem loadout) => selectionTarget is not null && loadout == selectionTarget;
    public Color GetSelectableColor(GameItem _) => selectionMode switch
    {
        SelectionMode.Copy => Colors.Green,
        SelectionMode.Swap => Colors.Blue,
        SelectionMode.Move => Colors.Blue,
        _ => Colors.Transparent
    };
}
