using Godot;
using System;

public partial class FilterBoolean : Node
{
    [Signal]
    public delegate void FilterPassedEventHandler(bool value);
    [Export]
    bool requirement = true;
    public void Check(bool value) => EmitSignal(SignalName.FilterPassed, value == requirement);
}
