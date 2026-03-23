using Godot;
using System;

public partial class BooleanPair : Node
{
    [Signal]
    public delegate void ValueEventHandler(bool value);
    [Signal]
    public delegate void InvertedValueEventHandler(bool value);
    [Signal]
    public delegate void OnTrueEventHandler();
    [Signal]
    public delegate void OnFalseEventHandler();
    public void EmitValues(bool value)
    {
        if(value)
            EmitSignal(SignalName.OnTrue);
        else
            EmitSignal(SignalName.OnFalse);
        EmitSignal(SignalName.Value, value);
        EmitSignal(SignalName.InvertedValue, !value);
    }
}
