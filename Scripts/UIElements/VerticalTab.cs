using Godot;
using System;

public partial class VerticalTab : Button
{
	[Export]
	int margin = 10;
	MarginContainer parent;
	public override void _Ready()
	{
		if (GetParent() is not MarginContainer mgP)
            return;
		parent = mgP;
        parent.AddThemeConstantOverride("margin_left", ButtonPressed ? 0 : margin);
        parent.AddThemeConstantOverride("margin_right", ButtonPressed ? margin : 0);
        Toggled += state =>
		{
			parent.AddThemeConstantOverride("margin_left", state ? 0 : margin);
            parent.AddThemeConstantOverride("margin_right", state ? margin : 0);
        };
	}
}
