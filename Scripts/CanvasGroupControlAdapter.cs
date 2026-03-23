using Godot;
using System;

[Tool]
public partial class CanvasGroupControlAdapter : CanvasGroup
{
	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
		var parent = GetParent<Control>();
        for (int i = 0; i < GetChildCount(); i++)
        {
            if(GetChildOrNull<Control>(i) is Control controlChild)
            {
                controlChild.Position = Vector2.Zero;
                controlChild.CustomMinimumSize = parent.Size;
            }
        }
    }
}
