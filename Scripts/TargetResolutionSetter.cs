using Godot;
using System;
using System.Text.Json.Nodes;

public partial class AppConfig
{
	public float uiScale;
}

public partial class TargetResolutionSetter : Node
{
	[Export]
	Control rootNode;
	[Export]
	Vector2I TargetResolution = new(950, 720);
	[Export]
	Vector2I MinResolution = new(360, 360);
	[Export]
	bool forceVertical;
	[Export]
	bool allowResize;
	[Export]
	bool onlyExpandWhenSmaller;
	[Export]
	double resizeIncrement = 0.05f;
	[Export]
	float resizeMin = 0.5f;
	[Export]
	float resizeMax = 1.0f;
	Window curWindow;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		curWindow = GetTree().Root;
		curWindow.SizeChanged += WindowSizeChanged;
		curWindow.MinSize = MinResolution;
		SetSize((float)AppConfig.Get("ui", "scale", 1.0));
		if (rootNode is not null)
		{
			rootNode.ResetOffsets();
			rootNode.ResetAnchors();
		}
		if (OS.HasFeature("mobile"))
			DisplayServer.ScreenSetOrientation(forceVertical ? DisplayServer.ScreenOrientation.SensorPortrait : DisplayServer.ScreenOrientation.Sensor);
		AppConfig.OnConfigChanged += OnConfigChanged;
	}

	private void WindowSizeChanged()
	{
		SetSize((float)AppConfig.Get("ui", "scale", 1.0));
	}

	public override void _Input(InputEvent @event)
	{
		if (!allowResize)
			return;
		if (@event is InputEventMouseButton mouseInput)
		{
			if (!mouseInput.CtrlPressed || !mouseInput.Pressed || (mouseInput.ButtonIndex != MouseButton.WheelUp && mouseInput.ButtonIndex != MouseButton.WheelDown))
				return;
			TryScroll(mouseInput.ButtonIndex == MouseButton.WheelUp);
			GetViewport().SetInputAsHandled();
		}
		else if(@event is InputEventKey keyInput)
		{
			if (!keyInput.CtrlPressed || !keyInput.Pressed || (keyInput.Keycode != Key.Equal && keyInput.Keycode != Key.Minus))
				return;
			TryScroll(keyInput.Keycode == Key.Equal);
			GetViewport().SetInputAsHandled();
		}
	}

	void TryScroll(bool up)
	{
		double currentZoom = AppConfig.Get("ui", "scale", 1.0);
		double currentRealZoom = CalcRealZoom(currentZoom);
		double newZoom = currentRealZoom + (up ? resizeIncrement : -resizeIncrement);
		newZoom = Math.Round(Math.Clamp(newZoom, resizeMin, resizeMax), 2);
		if (newZoom != currentZoom)
			AppConfig.Set("ui", "scale", newZoom);
	}

	double CalcRealZoom(double value)
	{
		Vector2 targetRes = TargetResolution;
		if (onlyExpandWhenSmaller)
			targetRes = new(Mathf.Max(TargetResolution.X, curWindow.Size.X), Mathf.Max(TargetResolution.Y, curWindow.Size.Y));
		float scaleFactor = (float)Mathf.Clamp(value, resizeMin, resizeMax);
		var prescaled = targetRes;
		targetRes /= scaleFactor;
		targetRes = new(Mathf.Max(targetRes.X, TargetResolution.X), Mathf.Max(targetRes.Y, TargetResolution.Y));
		double realZoom = Mathf.Min(prescaled.X / targetRes.X, prescaled.Y / targetRes.Y);
		realZoom = Math.Round(Math.Round(realZoom / resizeIncrement) * resizeIncrement, 2);
		return realZoom;
	}

	public override void _ExitTree()
	{
		AppConfig.OnConfigChanged -= OnConfigChanged;
		curWindow.SizeChanged -= WindowSizeChanged;
	}

	private void OnConfigChanged(string section, string key, JsonValue property)
	{
		if (section != "ui" || key != "scale")
			return;

		SetSize((float)property.GetValue<double>());
	}

	void SetSize(float value)
	{
		Vector2 targetRes = TargetResolution;
		if (onlyExpandWhenSmaller)
			targetRes = new(Mathf.Max(TargetResolution.X, curWindow.Size.X), Mathf.Max(TargetResolution.Y, curWindow.Size.Y));
		float scaleFactor = Mathf.Clamp(value, resizeMin, resizeMax);
		targetRes /= scaleFactor;
		targetRes = new(Mathf.Max(targetRes.X, TargetResolution.X), Mathf.Max(targetRes.Y, TargetResolution.Y));

		curWindow.ContentScaleSize = (Vector2I)targetRes;
	}
}
