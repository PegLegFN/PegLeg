using Godot;
using Microsoft.VisualBasic;
using System;

public partial class TimelineMonthStrip : Control
{
    [Export]
    Gradient colorGradient;
    [Export]
    Control colorTarget;
    [Export]
    Label label;

    public void SetMonth(DateTime date)
    {
        label.Text = date.Year == DateTime.UtcNow.Year ? date.ToString("MMMM") : date.ToString("MMMM yyyy");
        colorTarget.SelfModulate = colorGradient.Sample((date.Month-1)/12f);
    }
}
