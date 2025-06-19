using Godot;

public partial class CopyTemplateIdCtx : BaseContextComponent
{
    public override string Id => "CopyTemplateId";
    GameItem currentItem;
    public override void Update(ContextMenuHook hook)
    {
        currentItem = hook?.itemSource?.currentItem;
        SetDisabled(currentItem is null);
    }

    public void Copy()
    {
        if (currentItem is null)
            return;
        DisplayServer.ClipboardSet(Input.IsKeyPressed(Key.Shift) ? currentItem.template?.ToString() : currentItem.templateId);
        menu.CloseMenu();
    }
}
