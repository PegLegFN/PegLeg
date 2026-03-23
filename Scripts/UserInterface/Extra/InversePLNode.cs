using Godot;
using System;

public partial class InversePLNode : GraphElement
{
    [Export]
    Label lab;
    [Export]
    Control line;

    bool hasSetLabel = false;
    bool hasSetLine = false;
    public override void _Ready()
    {
        base._Ready();
        if (!hasSetLabel)
            lab.Text = "";
        if (!hasSetLine)
            line.Size = Vector2.Zero;
    }

    public void SetLabel(string text)
    {
        hasSetLabel = true;
        lab.Text = text;
    }

    public void SetPrevPos(Vector2 prevPos)
    {
        hasSetLine = true;
        Vector2 prevRelativePos = prevPos - PositionOffset;
        line.Rotation = Vector2.Right.AngleTo(prevRelativePos);
        line.Size = new(Mathf.Sqrt((prevRelativePos.X * prevRelativePos.X) + (prevRelativePos.Y * prevRelativePos.Y)), 1);
        //GD.Print(line.Size);
    }
}
