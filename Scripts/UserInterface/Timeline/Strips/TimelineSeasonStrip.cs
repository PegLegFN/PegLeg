using Godot;
using Godot.Collections;
using System;

public partial class TimelineSeasonStrip : Control
{
	[Export]
	GameItemEntry llamaItem;
	[Export]
	Label seasonNameLabel;
	[Export]
	GameItemEntry commonModifierItem;
	[Export]
	GameItemEntry[] venturesModifierItems;
	[Export]
	Dictionary<string, Texture2D> styles;
	[Export]
	ShaderHook styleTarget;
	[Export]
	RefreshTimerHook refreshTimer;

	public TimelineInterface.SeasonMarker current { get; private set; }
	public void SetMarker(TimelineInterface.SeasonMarker marker)
	{
		if (current == marker)
			return;
		current = marker;

		llamaItem.SetItem(marker.LlamaItem);
		seasonNameLabel.Text = marker.displayName;
		commonModifierItem.SetItem(marker.CommonModifierItem);
		if (styleTarget is not null)
		{
			styles.TryGetValue(marker.style, out var tex);
			styleTarget.SetShaderTexture(tex, "background");
			styleTarget.SelfModulate = marker.color;
		}
		if (refreshTimer is not null)
		{
			refreshTimer.Visible = marker.toDate > DateTime.UtcNow && marker.fromDate < DateTime.UtcNow;
			if (refreshTimer.Visible)
				refreshTimer.SetCustomRefreshTime(marker.toDate, marker.fromDate);
		}
		var ventMods = marker.VenturesModifierItems;
		for (int i = 0; i < venturesModifierItems.Length; i++)
		{
			if (ventMods.Length > i)
			{
				venturesModifierItems[i].SetItem(ventMods[i]);
				venturesModifierItems[i].Visible = true;
			}
			else
			{
				venturesModifierItems[i].Visible = false;
			}
		}
	}
}
