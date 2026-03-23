using Godot;

public partial class ScrollEventPassthrough : Control
{
	[Export]
	ScrollBar target;
	[Export]
	float scale = 1;
	[Export]
	float dragScale = 1;

	bool vertical = false;
	public override void _Ready()
	{
		vertical = target is VScrollBar;
	}

	public override void _GuiInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton scroll)
		{
			if (scroll.ButtonIndex == MouseButton.WheelUp)
				target.Value += scale;
			if (scroll.ButtonIndex == MouseButton.WheelDown)
				target.Value -= scale;
		}
		if (@event is InputEventScreenDrag drag)
		{
			float amount = vertical ? drag.Relative.Y : drag.Relative.X;
			target.Value += amount * dragScale;
		}
	}
}
