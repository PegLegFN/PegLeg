using Godot;

public partial class InvertBoolean : Node
{
	[Signal]
	public delegate void InvertedValueEventHandler(bool value);
	public void EmitInverted(bool value) => EmitSignal(SignalName.InvertedValue, !value);
}
