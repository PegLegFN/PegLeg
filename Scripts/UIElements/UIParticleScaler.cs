using Godot;
using System;

[Tool]
public partial class UIParticleScaler : Control
{
	[Export]
	Vector2 basisResolution = Vector2.Zero;
	Node2D firstChild;

    public override void _Ready()
    {
		firstChild = GetChildOrNull<Node2D>(0);
    }

    public override void _Process(double delta)
	{
		if (firstChild is null)
        {
            if (!Engine.IsEditorHint())
                return;
            firstChild = GetChildOrNull<Node2D>(0);

            if (firstChild is null)
                return;
        }

        firstChild.Position = Size * 0.5f;
		firstChild.Scale = (Size/basisResolution);
	}
}
