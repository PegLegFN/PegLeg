using Godot;
using System;

public partial class InversePL : Node
{
    [Export]
    GraphEdit editor;
    [Export]
    PackedScene graphNode;
    [Export]
    float initialEdgePad;
    [Export]
    Vector2 graphUnit;
    [Signal]
    public delegate void OnSampleEventHandler(float value);
    [Signal]
    public delegate void OnInverseEventHandler(float value);

    public override void _Ready()
    {
        RatingData max = new(90720/4, 0, 0, 0, 144, 144);
        max.Print("Max");
        if (editor is null)
            return;
        var keys = RatingData.HomebaseRatingCurve.times.ToArray();
        var values = RatingData.HomebaseRatingCurve.values.ToArray();

        Vector2 lastPos = new();
        for (int i = 0; i < keys.Length; i++)
        {
            //if (i > 5)
            //    break;
            var node = graphNode.Instantiate<InversePLNode>();
            editor.AddChild(node);
            //GD.Print($"({values[i]}, {keys[i]})");
            node.PositionOffset = new((float)values[i]*graphUnit.X, (float)keys[i] * -graphUnit.Y);
            node.SetLabel($"{values[i]}\n{keys[i]}");
            if (i != 0)
            {
                node.SetPrevPos(lastPos);
            }
            lastPos = node.PositionOffset;
        }
        ResetGraphView();
    }

    void ResetGraphView()
    {
        editor.ScrollOffset = new(initialEdgePad, editor.Size.Y - initialEdgePad);
    }

    public void SetValue(float inputValue)
    {
        EmitSignalOnInverse((float?)RatingData.HomebaseRatingCurve?.SampleInverse(inputValue) ?? 0);
    }

    public void SampleValue(float inputValue)
    {
        var sample = (float?)RatingData.HomebaseRatingCurve?.Sample(inputValue) ?? 0;
        GD.Print(sample);
        EmitSignalOnInverse(sample);
    }
}
