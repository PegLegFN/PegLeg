
public partial class CopyItemIconCtx : BaseContextComponent
{
    public override string Id => "CopyItemIcon";
    GameItem currentItem;
    public override void Update(ContextMenuHook hook)
    {
        currentItem = hook?.itemSource?.currentItem;
        SetDisabled(currentItem?.GetTexture(null)?.GetImage() is null || !Win64Helpers.isWindows);
    }

    public void Copy()
    {
        var img = currentItem?.GetTexture(null)?.GetImage();
        if (img is null)
            return;
        Win64Helpers.ClipboardSetImage(img);
        menu.CloseMenu();
    }
}
