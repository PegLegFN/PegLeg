using Godot;
using Godot.Collections;
using System;

public partial class TimelineEventStrip : Control
{
	[Signal]
	public delegate void EventColorChangedEventHandler(Color color);
	[Export]
	Label displayName;
	[Export]
	GameItemEntry[] displayItems;
	[Export]
	Control freeMarker;
	[Export]
	Dictionary<string, Texture2D> styles;
	[Export]
	ShaderHook styleTarget;
	[Export]
	RefreshTimerHook refreshTimer;

	public TimelineInterface.BaseEventMarker current { get; private set; }
	public void SetMarker(TimelineInterface.BaseEventMarker marker)
	{
		if (current == marker)
			return;
		current = marker;

		displayName.Visible = !string.IsNullOrWhiteSpace(marker.DisplayName);
		displayName.Text = marker.DisplayName;
		if (freeMarker is not null)
			freeMarker.Visible = marker.Free;
		if (styleTarget is not null)
		{
			styles.TryGetValue(marker.style, out var tex);
			styleTarget.SetShaderTexture(tex, "background");
			styleTarget.SelfModulate = marker.color;
		}
		EmitSignalEventColorChanged(marker.color);
		if (refreshTimer is not null)
		{
			refreshTimer.Visible = marker.toDate > DateTime.UtcNow && marker.fromDate < DateTime.UtcNow;
			if (refreshTimer.Visible)
				refreshTimer.SetCustomRefreshTime(marker.toDate, marker.fromDate);
		}
		var items = marker.KeyGameItems;
		for (int i = 0; i < displayItems.Length; i++)
		{
			if (items.Length > i)
			{
				displayItems[i].SetItem(items[i]);
				displayItems[i].Visible = true;
			}
			else
			{
				displayItems[i].Visible = false;
			}
		}
	}

	public void Press()
	{
		TimelineEventViewer.ShowEvent(current);
	}
}
