
using Godot;

public partial class CopyItemIconCtx : AbstractContextComponent
{
	public override string Id => "CopyItemIcon";
	Image currentImage;
	public override void Update(ContextMenuHook hook)
	{
		currentImage = null;
		var currentItem = hook?.itemSource?.currentItem;
		var tex = currentItem?.GetTexture(null, true);
		currentImage = tex?.GetImage();
		SetDisabled(currentImage is null || !Win64Helpers.isWindows);
	}

	public void Copy()
	{
		if (currentImage is null)
			return;
		Win64Helpers.ClipboardSetImage(currentImage);
		menu.CloseMenu();
	}
}
