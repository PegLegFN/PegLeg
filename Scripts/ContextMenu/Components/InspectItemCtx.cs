using Godot;
using System;

public partial class InspectItemCtx : BaseContextComponent
{
    public override string Id => "InspectItem";
    GameItem currentItem;
    public override void Update(ContextMenuHook hook)
    {
        currentItem = hook?.itemSource?.currentItem;
        SetDisabled(currentItem is null);
    }

    public void Inspect()
    {
        if (currentItem is null)
            return;
        GameItemViewer.Instance.ShowItem(currentItem);
        menu.CloseMenu();
    }
}
