using Godot;

public partial class ItemTitleCtx : BaseTitleCtx
{
	public override string Id => "ItemTitle";

	protected override string GetTitle(ContextMenuHook hook) =>
		hook?.itemSource?.displayItem?.template?.ItemName;

	protected override Color GetColor(ContextMenuHook hook) =>
		hook?.itemSource?.displayItem?.template?.RarityColor ?? Colors.Transparent;

	protected override bool GetRainbow(ContextMenuHook hook)
	{
		if (hook?.itemSource?.displayItem?.template is not { } template)
			return false;
		return template.Type == "ExtractableRelic" && (template["Augments"]?.AsArray().Count ?? 0) > 1;
	}
}
