using Godot;
using System.Collections.Generic;
using System.Threading.Tasks;

public partial class SubViewportScreenshotter : SubViewport
{
	static Dictionary<string, SubViewportScreenshotter> activeNamedScreenshotters = [];

	[Export]
	string uniqueName;

	public override async void _Ready()
	{
		if (GetParent() is SubViewportContainer containerParent)
		{
			if (Bootstrap.UseShareMenu)
				containerParent.VisibilityChanged += QueueRender;
			else
				containerParent.Visible = false;
			if(string.IsNullOrWhiteSpace(uniqueName))
				uniqueName = containerParent.Name;
		}
		if(!string.IsNullOrWhiteSpace(uniqueName))
			activeNamedScreenshotters.TryAdd(uniqueName, this);
		await Helpers.WaitForFrame();
		await Helpers.WaitForFrame();
		QueueRender();
	}

	public static void QueueRenderOfNamed(string name)
	{
		if(activeNamedScreenshotters.TryGetValue(name, out var screenshotter))
			screenshotter.QueueRender();
	}

	public override void _ExitTree()
	{
		activeNamedScreenshotters.Remove(uniqueName);
	}

	void QueueRender()
	{
		if (matchSize is not null)
		{
			var targetSize = matchSize.Size * matchSize.Scale;
			Size = (Vector2I)targetSize;
		}
		RenderTargetUpdateMode = UpdateMode.Once;
	}

	[Export]
	Control matchSize;
	public async void CopyScreenshot()
	{
#if !GODOT_WINDOWS
        GD.PushWarning("Can't share images on non-windows platforms");
        return;
#endif
		var img = await CaptureScreenshot();
		if (Input.IsKeyPressed(Key.Shift))
			Win64Helpers.ClipboardSetImage(img);
		else
			ShareImagePopup.ShowImage(img);
	}

	public async Task<Image> CaptureScreenshot()
	{
		QueueRender();
		await Helpers.WaitForFrame();
		await Helpers.WaitForFrame();
		return GetTexture().GetImage();
	}
}
