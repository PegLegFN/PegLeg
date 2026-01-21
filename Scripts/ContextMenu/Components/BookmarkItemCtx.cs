using Godot;

public partial class BookmarkItemCtx : BaseContextComponent
{
    public override string Id => "BookmarkItem";
    GameItem currentItem;
    [Export]
    ToggleIconAnim toggleIcon;

    public override void Update(ContextMenuHook hook)
    {
        currentItem = hook?.itemSource?.currentItem;
        SetDisabled(currentItem?.template.CanBeFavourited != true || !GameAccount.ActiveAccount.isOwned);
        toggleIcon.SetState(GameAccount.ActiveAccount.HasReminder(currentItem?.template));
    }

    public void ToggleBookmark()
    {
        if (currentItem?.template.CanBeFavourited != true)
            return;
        GameAccount.ActiveAccount.ToggleReminder(currentItem.template);
        toggleIcon.Animate(GameAccount.ActiveAccount.HasReminder(currentItem.template));
    }
}
