using Godot;
using System;

public partial class UniformScale : Node
{
    [Export]
    Control target;
    public void SetScaleUniform(float value) => target.Scale = new(value, value);
}
