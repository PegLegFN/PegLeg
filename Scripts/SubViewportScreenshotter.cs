using Godot;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

public partial class SubViewportScreenshotter : SubViewport
{
	static Dictionary<string, SubViewportScreenshotter> activeNamedScreenshotters = [];

	[Export]
	int queueFrameDelay = 2;
	[Export]
	int renderFrameDelay = 2;
	[Export]
	string uniqueName;
	[Export]
	Control matchSize;
	[Export]
	TextureRect opaqueBG;

	TriggerInstance copyTrigger;

	public override async void _Ready()
	{
		opaqueBG?.Visible = false;
		if (GetParent() is SubViewportContainer containerParent)
		{
			if (containerParent.IsInGroup("StayVisible"))
				containerParent.VisibilityChanged += QueueRender;
			else
				containerParent.Visible = false;
			if(string.IsNullOrWhiteSpace(uniqueName))
				uniqueName = containerParent.Name;
		}
		if (!string.IsNullOrWhiteSpace(uniqueName))
		{
			activeNamedScreenshotters.TryAdd(uniqueName, this);
			copyTrigger = new(uniqueName + "_copy", true);
			copyTrigger.BindNode(this, CopyScreenshotCtx);
		}

		Node ancestor = GetParent();
		while(ancestor is not null)
		{
			if (ancestor.IsInGroup("ShareTab"))
			{
				ancestor.SetMeta("shareTrigger", uniqueName + "_copy");
				break;
			}
			ancestor = ancestor.GetParent();
		}

		//await Helpers.WaitForFrames(2);
		QueueRender();
	}

	public static SubViewportScreenshotter GetNamed(string name) =>
		activeNamedScreenshotters.TryGetValue(name, out var screenshotter) ? screenshotter : null;

	public override void _ExitTree()
	{
		activeNamedScreenshotters.Remove(uniqueName);
	}

	void QueueRender() => QueueRenderTask().StartTask();
	async Task QueueRenderTask()
	{
		if (matchSize is not null)
		{
			matchSize.Size = Vector2.Zero;
			await Helpers.WaitForFrames(queueFrameDelay);
			var targetSize = matchSize.Size * matchSize.Scale;
			Size = (Vector2I)targetSize;
		}
		RenderTargetUpdateMode = UpdateMode.Once;
		await Helpers.WaitForFrames(renderFrameDelay);
	}

	public void CopyScreenshot() => CopyScreenshotCtx([]);
	public async void CopyScreenshotCtx(string[] ctx)
	{
#if !GODOT_WINDOWS
        GD.PushWarning("Can't share images on non-windows platforms");
        return;
#endif
		var img = await CaptureScreenshot();
		if (Input.IsKeyPressed(Key.Shift) || (ctx is not null && ctx.Length > 0 && ctx[0] == "force"))
			Win64Helpers.ClipboardSetImage(img);
		else
			ShareImagePopup.ShowImage(img);
	}

	public async Task<Image> CaptureScreenshot(bool opaque = false)
	{
		opaqueBG?.Visible = opaque;
		await QueueRenderTask();
		opaqueBG?.Visible = false;
		return GetTexture().GetImage();
	}

	public async Task<Image> CaptureScreenshotOpaque() => await CaptureScreenshot(true);

	public PromisedScreenshot GetPromise(bool opaque = false) => new(this, opaque);

	public record struct PromisedScreenshot(SubViewportScreenshotter source, bool opaque)
	{
		SemaphoreSlim semaphore = new(1);
		Image result;

		public async Task<Image> GetOrCapture()
		{
			using var _ = await semaphore.AwaitToken();
			if (result is not null || source is null)
				return result;
			return result = await source.CaptureScreenshot(opaque);
		}
	}
}
