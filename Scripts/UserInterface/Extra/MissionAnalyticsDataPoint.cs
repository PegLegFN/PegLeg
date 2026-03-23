using Godot;
using System;

public partial class MissionAnalyticsDataPoint : Control
{
    [Export]
    ProgressBar level;
    [Export]
    Label valueLabel;
    [Export]
    Label dateLabel;

    public void SetData(DateTime date, int value, int maxValue)
    {
        Modulate = value >= 0 ? Colors.White : Colors.Red;
        level.Value = value >= 0 ? ((float)value / maxValue) : 0;
        valueLabel.Text = value >= 0 ? value.Compactify() : "N/A";
        dateLabel.Text = date.ToLocalTime().ToString("yyyy-M-d");
    }
}
