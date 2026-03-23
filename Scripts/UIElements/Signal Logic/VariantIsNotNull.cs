using Godot;
using System;

public partial class VariantIsNotNull : Node
{
    [Signal]
    public delegate void ValueEventHandler(bool value);
    public void Check(Variant value) => EmitSignal(SignalName.Value, value.Obj is not null);
}
