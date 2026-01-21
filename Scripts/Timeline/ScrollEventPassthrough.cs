using Godot;
using System;

public partial class ScrollEventPassthrough : Control
{
    [Export]
    ScrollBar target;
    [Export]
    float scale;
    public override void _GuiInput(InputEvent @event)
    {
        if(@event is InputEventMouseButton scroll)
        {
            if (scroll.ButtonIndex == MouseButton.WheelUp)
                target.Value += scale;
            if (scroll.ButtonIndex == MouseButton.WheelDown)
                target.Value -= scale;
        }
    }
}
