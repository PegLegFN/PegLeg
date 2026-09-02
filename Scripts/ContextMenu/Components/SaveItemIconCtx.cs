
using Godot;

public partial class SaveItemIconCtx : AbstractContextComponent
{
	[Export]
	FileDialog filePicker;
	public override string Id => "SaveItemIcon";
	Image currentImage;

	public override void _Ready()
	{
		filePicker?.FileSelected += OnSaveLocation;
	}

	public override void Update(ContextMenuHook hook)
	{
		currentImage = null;
		var currentItem = hook?.itemSource?.currentItem;
		var tex = currentItem?.GetTexture(null, true);
		currentImage = tex?.GetImage();
		currentImage?.SetMeta("filename", currentItem.template?.DisplayName ?? "item");
		SetDisabled(currentImage is null);
	}

	public async void Copy()
	{
		if (currentImage is null)
			return;
		filePicker.CurrentFile = currentImage.GetMeta("filename").As<string>();
		filePicker.SetMeta("targetImage", currentImage);
		filePicker.Popup();
		menu.CloseMenu();
	}

	private void OnSaveLocation(string path)
	{
		if (filePicker.GetMeta("targetImage").As<Image>() is not Image outImage)
			return;
		outImage.SavePng(path);
	}
}
