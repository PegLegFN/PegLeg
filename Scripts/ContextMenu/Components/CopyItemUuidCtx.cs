using Godot;

public partial class CopyItemUuidCtx : BaseContextComponent
{
    public override string Id => "CopyItemUuid";
    GameItem currentItem;
    public override void Update(ContextMenuHook hook)
    {
        currentItem = hook?.itemSource?.currentItem;
        SetDisabled(currentItem?.profile is null);
    }

    public void Copy()
    {
        if (currentItem?.profile is null)
            return;
        DisplayServer.ClipboardSet(Input.IsKeyPressed(Key.Shift) ? currentItem.SimpleRawData.ToString() : currentItem.uuid);
        menu.CloseMenu();
    }
}
