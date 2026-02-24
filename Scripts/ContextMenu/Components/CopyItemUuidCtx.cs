using Godot;
using System.Text.Json;

public partial class CopyItemUuidCtx : AbstractContextComponent
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
        DisplayServer.ClipboardSet(Input.IsKeyPressed(Key.Shift) ? JsonSerializer.Serialize(currentItem.GameItemData) : currentItem.uuid);
        menu.CloseMenu();
    }
}
