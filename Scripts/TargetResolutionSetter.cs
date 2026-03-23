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
	double resizeIncrement = 0.05f;
	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		var window = GetTree().Root;
		window.MinSize = MinResolution;
		SetSize(window, (float)AppConfig.Get("ui", "scale", 1.0));
		if (rootNode is not null)
		{
			rootNode.ResetOffsets();
			rootNode.ResetAnchors();
		}
		if (OS.HasFeature("mobile"))
			DisplayServer.ScreenSetOrientation(forceVertical ? DisplayServer.ScreenOrientation.SensorPortrait : DisplayServer.ScreenOrientation.Sensor);
		AppConfig.OnConfigChanged += OnConfigChanged;
	}

	public override void _Input(InputEvent @event)
	{
		if (allowResize && @event is InputEventMouseButton mouseInput)
		{
			if (!mouseInput.CtrlPressed)
				return;
			double currentZoom = AppConfig.Get("ui", "scale", 1.0);
			if (mouseInput.ButtonIndex == MouseButton.WheelUp)
			{
				AppConfig.Set("ui", "scale", Math.Clamp(currentZoom + resizeIncrement, 0.5, 1));
				GetViewport().SetInputAsHandled();
			}
			else if (mouseInput.ButtonIndex == MouseButton.WheelDown)
			{
				AppConfig.Set("ui", "scale", Math.Clamp(currentZoom - resizeIncrement, 0.5, 1));
				GetViewport().SetInputAsHandled();
			}
		}
	}

	public override void _ExitTree()
	{
		AppConfig.OnConfigChanged -= OnConfigChanged;
	}

	private void OnConfigChanged(string section, string key, JsonValue property)
	{
		if (section != "ui" || key != "scale")
			return;

		var window = GetTree().Root;
		SetSize(window, (float)property.GetValue<double>());
	}

	void SetSize(Window window, float value)
	{
		float finalScale = Mathf.Clamp(value, 0.5f, 1.0f);
		window.ContentScaleSize = (Vector2I)((Vector2)TargetResolution / finalScale);
	}
}
