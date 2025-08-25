using Godot;
using System;

public partial class XpLimitDisplay : Control
{
    [Export]
    Label progressText;
    [Export]
    ProgressBar progressBar;
    [Export]
    Label progressPercent;
    [Export]
    RefreshTimerHook refreshTime;

    public void SetXpProgress(int current, int max, DateTime? nextRefresh)
    {
        progressText.Text = $"{current.Notate()}/{max.Notate()}";
        float scale = (float)current / max;
        progressBar.Value = scale;
        progressPercent.Text = $"{Mathf.RoundToInt(scale * 100)}%";
        refreshTime.Visible = nextRefresh is not null;
        if (refreshTime.Visible)
            refreshTime.SetCustomRefreshTime(nextRefresh ?? default, (nextRefresh ?? default).AddDays(-7));
    }
}
