using Godot;
using System;
using System.Linq;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using static System.Runtime.InteropServices.JavaScript.JSType;

public partial class HeroLoadoutSlotSelector : Control, IRecyclableElementProvider<GameItem>, ISelectableElementProvider<GameItem>
{
    [Export]
    bool useActiveAccount = true;
    [Export]
    HeroLoadoutEntry linkedPanel;
    [Export]
    RecycleListContainer loadoutList;

    public override void _Ready()
    {
        loadoutList.SetProvider(this);
        if (useActiveAccount)
        {
            GameAccount.ActiveAccountChanged += UpdateActive;
            SetAccount(GameAccount.activeAccount);
        }
    }

    public override void _ExitTree()
    {
        GameAccount.ActiveAccountChanged -= UpdateActive;
    }

    void UpdateActive()
    {
        SetAccount(GameAccount.activeAccount);
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

    public void SetAccount(GameAccount account)
    {
        ClearSelection();
        var profile = account?.GetProfile(FnProfileTypes.AccountItems);
        if (profile is null)
        {
            loadouts = [];
            loadoutList.UpdateList(true);
            linkedPanel?.ClearItem();
            return;
        }
        var defaultLoadoutUUID = profile?.statAttributes?["selected_hero_loadout"]?.ToString();
        loadouts = [.. profile?.GetItems("CampaignHeroLoadout").OrderBy(i => i.attributes?["loadout_index"]?.GetValue<int>() ?? 0).ToArray() ?? []];
        loadoutList.UpdateList(true);
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

    public GameItem GetRecycleElement(int index)
    {
        if(index < 0 || index >= loadouts.Length)
        {
            //GD.Print($"{index} is OOB of [0..{loadouts.Length - 1}]");
            return null;
        }
        return loadouts[index];
    }

    public int GetRecycleElementCount() => 
        loadouts.Length;

    void ClearSelection() => SetSelection(SelectionMode.None, -1);
    void SetSelection(SelectionMode mode, int index)
    {
        //GD.Print($"mode : {mode}, target: {index + 1}");
        selectionTarget = GetRecycleElement(index);
        selectionMode = mode;
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
