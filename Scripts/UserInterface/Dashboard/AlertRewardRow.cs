using Godot;
using System;

public partial class AlertRewardRow : Control
{
	[Export]
	GameItemEntry itemEntry;

	[Export]
	Label[] countLabels;

	[Export]
	Button[] buttons;

	public override void _Ready()
	{
		buttons[0].Pressed += () => SetFilter("");
		buttons[1].Pressed += () => SetFilter("s");
		buttons[2].Pressed += () => SetFilter("p");
		buttons[3].Pressed += () => SetFilter("c");
		buttons[4].Pressed += () => SetFilter("t");
		buttons[5].Pressed += () => SetFilter("spct");
		buttons[6].Pressed += () => SetFilter("v");
	}

	public void SetTotals(GameItemTemplate template, AlertSummaryController.ZoneTotals totals)
	{
		Visible = template is not null;
		if (template != itemEntry.currentItem?.template)
			itemEntry.SetItem(template?.CreateInstance());
		if (template is null)
			return;
		SetCounts(
			totals.S,
			totals.P,
			totals.C,
			totals.T,
			totals.Main,
			totals.V
		);
	}

	void SetCounts(params int[] totals)
	{
		for (int i = 0; i < totals.Length; i++)
		{
			countLabels[i].Text = totals[i].Compactify();
			countLabels[i].TooltipText = totals[i].Notate();
		}
	}

	void SetFilter(string zones)
	{
		MissionRewardsController.PrimaryAllRewards?.SetFilterFromSummary($"\"{itemEntry.currentItem?.template.ItemName}\"", zones);
	}
}
