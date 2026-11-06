using Godot;
using System;

public partial class ScrollFixer : Node
{
	public override void _Input(InputEvent @event)
	{

		if (@event is InputEventMouseButton mouseBtn && mouseBtn.Pressed)
		{
			//scroll wheel is usually tied to zooming or scrolling, which causes UI issues unless we tell the UI the mouses new relative position explicitly
			if (mouseBtn.ButtonIndex == MouseButton.WheelUp || mouseBtn.ButtonIndex == MouseButton.WheelDown)
			{
				var vp = GetViewport();
				vp.WarpMouse(vp.GetMousePosition());
			}
		}
	}
}
