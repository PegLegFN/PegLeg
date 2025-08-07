using Godot;
using System;
using System.Threading.Tasks;

public partial class SubViewportScreenshotter : SubViewport
{
	[Export]
	Control matchSize;
	public async void CopyScreenshot()
	{
		var img = await CaptureScreenshot();
		Win64Helpers.ClipboardSetImage(img);
	}

	public async Task<Image> CaptureScreenshot()
	{
		if(matchSize is not null)
		{
			var targetSize = matchSize.Size * matchSize.Scale;
			Size = (Vector2I)targetSize;
		}
		RenderTargetUpdateMode = UpdateMode.Once;
		await Helpers.WaitForFrame();
        await Helpers.WaitForFrame();
        return GetTexture().GetImage();
	}
}
