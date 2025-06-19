using Godot;

public partial class ItemTitleCtx : BaseTitleCtx
{
    public override string Id => "ItemTitle";

    protected override string GetTitle(ContextMenuHook hook) => 
        hook?.itemSource?.currentItem?.template?.DisplayName;

    protected override Color GetColor(ContextMenuHook hook) =>
        hook?.itemSource?.currentItem?.template?.RarityColor ?? Colors.Transparent;
}
