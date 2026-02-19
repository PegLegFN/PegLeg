using Godot;
using System.Linq;

public partial class RecycleItemCtx : BaseContextComponent
{
    public override string Id => "RecycleItem";
    GameItem currentItem;
    public override void Update(ContextMenuHook hook)
    {
        currentItem = hook?.itemSource?.currentItem;
        if (currentItem?.inspectorOverride is not null) //ensures we're targeting the actual profile item
            currentItem = currentItem.inspectorOverride;
        SetDisabled(true);

        if (currentItem?.profile is null)
            return;

        if (!SimpleItemSelector.RecyclableFilter(currentItem))
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
        var item = currentItem;
        if (item?.profile is null)
            return;
        menu.CloseMenu();
        var toRecycle = await SimpleItemSelector.OpenSelector([item], SimpleItemSelector.RecycleConfig);
        if (toRecycle.Length == 0)
            return;
        var json = $@"{{""targetItemIds"":[{item.uuid}]}}";
        await item?.profile.PerformOperation("RecycleItemBatch", json);
    }
}
