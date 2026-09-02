
using Godot;
using System.Threading.Tasks;

public partial class SaveItemIconCtx : AbstractContextComponent
{
	[Export]
	FileDialog filePicker;
	public override string Id => "SaveItemIcon";
	Image currentImage;

	public override void _Ready()
	{
		filePicker?.FileSelected += OnSaveLocation;
		filePicker.Canceled += OnCancel;
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

	bool isPicking=false;

	public async void Copy()
	{
		if (currentImage is null)
			return;
		filePicker.CurrentFile = currentImage.GetMeta("filename").As<string>();
		filePicker.SetMeta("targetImage", currentImage);
		filePicker.Popup();
		menu.CloseMenu();
		using var _ = LoadingOverlay.CreateToken();
		isPicking = true;
		while (isPicking)
		{
			await Helpers.WaitForFrame();
		}
	}

	private void OnCancel() => isPicking = false;
	private void OnSaveLocation(string path)
	{
		isPicking = false;
		if (filePicker.GetMeta("targetImage").As<Image>() is not Image outImage)
			return;
		outImage.SavePng(path);
	}
}
