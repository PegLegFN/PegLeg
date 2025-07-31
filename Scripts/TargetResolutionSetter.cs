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
	Vector2I TargetResolution = new(1280, 720);
    [Export]
    Vector2I MinResolution = new(360, 360);
    // Called when the node enters the scene tree for the first time.
    public override void _Ready()
	{
        var window = GetTree().Root;
        SetSize(window, (float)AppConfig.Get("ui", "scale", 1.0));
        window.MinSize = MinResolution;
        rootNode?.ResetOffsets();
        AppConfig.OnConfigChanged += OnConfigChanged;
    }

    public override void _ExitTree()
    {
        AppConfig.OnConfigChanged -= OnConfigChanged;
    }

    private void OnConfigChanged(string section, string key, JsonValue property)
    {
        if (section != "ui")
            return;

        if (key == "scale")
        {
            var window = GetTree().Root;
            SetSize(window, (float)property.GetValue<double>());
        }
    }

    void SetSize(Window window, float value)
    {
        float finalScale = Mathf.Clamp(value, 0.5f, 1.0f);
        window.ContentScaleSize = (Vector2I)((Vector2)TargetResolution / finalScale);
    }
}
