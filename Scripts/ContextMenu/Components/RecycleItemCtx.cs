using Godot;
using System.Linq;

public partial class RecycleItemCtx : BaseContextComponent
{
    public override string Id => "RecycleItem";
    GameItem currentItem;
    public override void Update(ContextMenuHook hook)
    {
        currentItem = hook?.itemSource?.currentItem;
        SetDisabled(true);

        if (currentItem?.profile is null)
            return;

        if (!GameItemSelector.RecyclablePredicate(currentItem))
            return;

        bool isInHeroLoadout = currentItem.profile
            .GetItems("CampaignHeroLoadout")
            .SelectMany(loadout =>
                loadout.attributes["crew_members"]
                .AsObject()
                .Select(kvp => kvp.Value.ToString())
            )
            .Distinct()
            .Contains(currentItem.uuid);
        if (isInHeroLoadout)
            return;

        SetDisabled(false);
    }

    public async void Recycle()
    {
        if (currentItem?.profile is null)
            return;
        menu.CloseMenu();
        GameItemSelector.Instance.SetRecycleDefaults();
        var toRecycle = await GameItemSelector.Instance.OpenSelector([currentItem], [currentItem]);
        if (toRecycle.Length == 0)
            return;
        await currentItem?.profile.PerformOperation("RecycleItemBatch", $@"""targetItemIds"":[{currentItem.uuid}]");
    }
}
