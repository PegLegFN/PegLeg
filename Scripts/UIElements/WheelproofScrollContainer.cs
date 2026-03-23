using Godot;
using System;

public partial class WheelproofScrollContainer : ScrollContainer
{
    [Export]
    bool allowOnShift = true;

    ScrollMode defaultHMode;
    ScrollMode defaultVMode;
    public override void _Ready()
    {
        base._Ready();
        defaultHMode = HorizontalScrollMode;
        defaultVMode = VerticalScrollMode;
        GetHScrollBar().AddChild(new ScrollWheelBlocker(allowOnShift));
        GetVScrollBar().AddChild(new ScrollWheelBlocker(allowOnShift));
    }

    //I dont like that you cant REPLACE native functionality in Godot, this will run ScrollContainers _GuiInput before mine... bit rude innit
    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventKey kb)
        {
            if (kb.Keycode != Key.Shift)
                return;
            if (kb.Pressed)
            {
                HorizontalScrollMode = defaultHMode;
            }
        }
    }
}
