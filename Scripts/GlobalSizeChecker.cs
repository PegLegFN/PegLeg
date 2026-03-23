using Godot;
using System;

public partial class GlobalSizeChecker : Node
{
    [Export]
    Vector2I WindowSize = new(1350, 720);
    [Export]
    Vector2I TargetResolution = new(950, 720);
    [Export]
    Vector2I MinResolution = new(360, 360);
    public override async void _Ready()
	{
		await Helpers.WaitForFrames(10);
		var window = GetWindow();
		if (window.GetNodeOrNull("Bootstrap") is null)
        {
            window.Size = WindowSize;
            window.Transparent = false;
            window.TransparentBg = true;
            window.Borderless = false;
            window.Unfocusable = false;
            window.MinSize = MinResolution;
            SetSize(window, (float)AppConfig.Get("ui", "scale", 1.0));
        }
    }
    void SetSize(Window window, float value)
    {
        float finalScale = Mathf.Clamp(value, 0.5f, 1.0f);
        window.ContentScaleSize = (Vector2I)((Vector2)TargetResolution / finalScale);
    }
}
