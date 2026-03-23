using Godot;
using System;

public partial class TestRecyclableElement : Control, IRecyclableEntry
{

    [Signal]
    public delegate void ValueChangedEventHandler(string value);
    public Control node => this;

    public void SetRecyclableElementProvider(IRecyclableElementProvider provider)
    {
    }

    public void SetRecycleIndex(int index)
    {
        EmitSignal(SignalName.ValueChanged, index.ToString());
    }
}
