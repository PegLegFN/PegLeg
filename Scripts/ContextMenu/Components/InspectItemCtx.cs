using Godot;
using System;

public partial class InspectItemCtx : AbstractContextComponent
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
        GameItemViewer.Instance.ShowItem(currentItem, preserveUnseen: Input.IsKeyPressed(Key.Shift), rawInspect: Input.IsKeyPressed(Key.Ctrl));
        menu.CloseMenu();
    }
}
