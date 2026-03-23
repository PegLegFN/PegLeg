using Godot;
using System;
using System.Linq;

[Tool]
public partial class SwitchController : Control
{
    [Signal]
    public delegate void SwitchIndexChangedEventHandler(int index);

    [Export]
    bool requirePressed;

    [Export]
    Button[] switchButtons;

    public int CurrentIndex { get; private set; }
    protected ButtonGroup SwitchGroup { get; private set; }

    public override void _Ready()
    {
        var firstPressed = switchButtons.FirstOrDefault(b => b.ButtonPressed);
        if (firstPressed is not null)
            Initialise(Array.IndexOf(switchButtons, firstPressed));
        else
            Initialise(requirePressed ? 0 : -1);
    }

    protected virtual void Initialise(int defaultIndex)
    {
        SwitchGroup = new();
        for (int i = 0; i < switchButtons.Length; i++)
        {
            int curIndex = i;
            switchButtons[i].Toggled += val => UpdateIndex(val, curIndex);
            switchButtons[i].ButtonGroup = SwitchGroup;
        }
        SetIndex(defaultIndex);
    }

    protected virtual void SetIndex(int pressedIndex)
    {
        for (int i = 0; i < switchButtons.Length; i++)
        {
            switchButtons[i].ButtonPressed = i == pressedIndex;
        }
        var firstPressed = switchButtons.FirstOrDefault(b => b.ButtonPressed);
        if (firstPressed == null && requirePressed)
        {
            switchButtons[0].ButtonPressed = true;
        }
    }

    protected virtual void UpdateIndex(bool val, int index)
    {
        if (!val)
            return;
        CurrentIndex = index;
        EmitSignal(SignalName.SwitchIndexChanged, index);
    }
}
