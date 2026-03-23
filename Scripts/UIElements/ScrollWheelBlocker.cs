using Godot;
using System;
using System.Linq;

public partial class ScrollWheelBlocker : Control
{
    [Export]
    bool allowOnShift = true;
    Control linkedParent;

    public ScrollWheelBlocker() { }

    public ScrollWheelBlocker(bool allowOnShift)
    {
        this.allowOnShift = allowOnShift;
    }

    public override void _Ready()
    {
        base._Ready();
        linkedParent = GetParent<Control>();
        MouseFilter = MouseFilterEnum.Pass;
        AnchorTop = 0;
        AnchorLeft = 0;
        AnchorBottom = 1;
        AnchorRight = 1;
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mb)
        {
            if (mb.IsPressed() && (mb.ButtonIndex == MouseButton.WheelUp || mb.ButtonIndex == MouseButton.WheelDown) && !(allowOnShift && mb.ShiftPressed))
            {
                var prevMouseFilter = linkedParent.MouseFilter;
                linkedParent.MouseFilter = MouseFilterEnum.Ignore;
                ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame).OnCompleted(()=> {
                    linkedParent.MouseFilter = prevMouseFilter;
                });
                return;
            }
        }
    }
}
