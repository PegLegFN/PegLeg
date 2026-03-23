using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using static TimelineInterface;

public partial class TimelineStripPlacer : Node
{
	[Export]
	PackedScene SeasonStripScene;
	[Export]
	PackedScene ShopStripScene;
	[Export]
	PackedScene EventStripScene;
	[Export]
	PackedScene SmallEventStripScene;
	[Export]
	PackedScene MonthStripScene;
	[Export]
	Control stripParent;
	[Export]
	ScrollContainer timelineScrollArea;
	[Export]
	Control weekParent;
	[Export]
	float weekSize = 350;
	[Export]
	float stripSpacing = 1;
	[Export]
	float seasonStartHeight;
	[Export]
	float shopStartHeight;
	[Export]
	float questlineStartHeight;
	[Export]
	float eventStartHeight;

	public List<TimelineContentContainer> contentContainers = [];
	Label[] weekLabels;

	Markers markers;

	public override void _Ready()
	{
		weekLabels = new Label[weekParent.GetChildCount()];
		for (int i = 0; i < weekLabels.Length; i++)
		{
			weekLabels[i] = weekParent.GetChild(i).GetNode<Label>("Label");
		}
		timelineScrollArea.ItemRectChanged += UpdateStrips;
		timelineScrollArea.GetChild<Control>(0).ItemRectChanged += UpdateStrips;
	}

	public void SetMarkers(Markers markerCollection)
	{
		markers = markerCollection;
		UpdateStrips(true);
	}

	List<TimelineSeasonStrip> SeasonStrips = [];

	List<TimelineShopStrip> ShopStrips = [];

	List<TimelineEventStrip> EventStrips = [];

	List<TimelineEventStrip> SmallEventStrips = [];

	List<TimelineMonthStrip> MonthStrips = [];
	int prevMinWeek = -1;
	int prevMaxWeek = -1;

	public void UpdateStrips() => UpdateStrips(false);
	public void UpdateStrips(bool force)
	{
		if (markers is null)
			return;

		var minWeek = Mathf.Clamp(Mathf.FloorToInt(timelineScrollArea.ScrollHorizontal / weekSize), -99, 99);
		var maxWeek = Mathf.Clamp(Mathf.CeilToInt((timelineScrollArea.ScrollHorizontal + timelineScrollArea.Size.X) / weekSize), -99, 99);

		if (prevMinWeek == minWeek && prevMaxWeek == maxWeek && !force)
			return;
		prevMinWeek = minWeek;
		prevMaxWeek = maxWeek;

		var minDate = markers.startDate.AddDays(minWeek * 7);
		var maxDate = markers.startDate.AddDays(maxWeek * 7);

		//GD.Print($"TL {minWeek}, {maxWeek}");

		bool ValidateRange(BaseMarker m) => (m.fromDate < maxDate && m.toDate > minDate);

		for (int i = 0; i < SeasonStrips.Count; i++)
		{
			SeasonStrips[i].Visible = false;
		}

		for (int i = 0; i < ShopStrips.Count; i++)
		{
			ShopStrips[i].Visible = false;
		}

		for (int i = 0; i < EventStrips.Count; i++)
		{
			EventStrips[i].Visible = false;
		}

		for (int i = 0; i < SmallEventStrips.Count; i++)
		{
			SmallEventStrips[i].Visible = false;
		}

		for (int i = 0; i < MonthStrips.Count; i++)
		{
			MonthStrips[i].Visible = false;
		}

		for (int i = 0; i < weekLabels.Length; i++)
		{
			weekLabels[i].Text = markers.weekIndexes.Length > i ? $"Week {markers.weekIndexes[i] + 1}" : "";
		}

		var curMonth = minDate.AddDays(-minDate.Day).AddDays(1);
		var curMonthIdx = 0;
		while (curMonth < maxDate)
		{
			if (MonthStrips.Count <= curMonthIdx)
			{
				var newStrip = MonthStripScene.Instantiate<TimelineMonthStrip>();
				stripParent.AddChild(newStrip);
				MonthStrips.Add(newStrip);
			}
			var strip = MonthStrips[curMonthIdx];
			strip.Visible = true;
			strip.SetMonth(curMonth);
			var dayOffset = (curMonth - markers.startDate).TotalDays;
			strip.ResetOffsets();
			strip.OffsetLeft = (float)(dayOffset * weekSize / 7);
			strip.OffsetRight = strip.OffsetLeft;
			var dayDuration = (curMonth.AddMonths(1) - curMonth).TotalDays;
			strip.CustomMinimumSize = new Vector2((float)(dayDuration * weekSize / 7), strip.CustomMinimumSize.Y);

			curMonthIdx++;
			curMonth = curMonth.AddMonths(1);
		}

		int fullEventStrips = 0;
		int miniEventStrips = 0;

		var eventMarkers = markers.events.Where(ValidateRange).ToArray();
		for (int i = 0; i < eventMarkers.Length; i++)
		{
			if (eventMarkers[i].Duration > 6.5)
			{
				TimelineEventStrip eventStrip;
				if (EventStrips.Count > fullEventStrips)
				{
					eventStrip = EventStrips[fullEventStrips];
				}
				else
				{
					eventStrip = EventStripScene.Instantiate<TimelineEventStrip>();
					stripParent.AddChild(eventStrip);
					EventStrips.Add(eventStrip);
				}
				eventStrip.Visible = true;
				PositionStrip(eventStrip, eventMarkers[i], eventStartHeight, 5, markers.startDate);
				eventStrip.SetMarker(eventMarkers[i]);
				fullEventStrips++;
			}
			else
			{
				TimelineEventStrip eventStrip;
				if (SmallEventStrips.Count > miniEventStrips)
				{
					eventStrip = SmallEventStrips[miniEventStrips];
				}
				else
				{
					eventStrip = SmallEventStripScene.Instantiate<TimelineEventStrip>();
					stripParent.AddChild(eventStrip);
					SmallEventStrips.Add(eventStrip);
				}
				eventStrip.Visible = true;
				PositionStrip(eventStrip, eventMarkers[i], eventStartHeight, 5, markers.startDate);
				eventStrip.SetMarker(eventMarkers[i]);
				miniEventStrips++;
			}
		}

		var questlineMarkers = markers.questlines.Where(ValidateRange).ToArray();
		for (int i = 0; i < questlineMarkers.Length; i++)
		{
			TimelineEventStrip questlineStrip;
			if (EventStrips.Count > fullEventStrips)
			{
				questlineStrip = EventStrips[fullEventStrips];
			}
			else
			{
				questlineStrip = EventStripScene.Instantiate<TimelineEventStrip>();
				stripParent.AddChild(questlineStrip);
				EventStrips.Add(questlineStrip);
			}
			questlineStrip.Visible = true;
			PositionStrip(questlineStrip, questlineMarkers[i], questlineStartHeight, 8, markers.startDate);
			questlineStrip.SetMarker(questlineMarkers[i]);
			fullEventStrips++;
		}

		var shopMarkers = markers.shops.Where(ValidateRange).ToArray();
		for (int i = 0; i < shopMarkers.Length; i++)
		{

			TimelineShopStrip shopStrip;
			if (ShopStrips.Count > i)
			{
				shopStrip = ShopStrips[i];
			}
			else
			{
				shopStrip = ShopStripScene.Instantiate<TimelineShopStrip>();
				stripParent.AddChild(shopStrip);
				ShopStrips.Add(shopStrip);
			}
			shopStrip.Visible = true;
			PositionStrip(shopStrip, shopMarkers[i], shopStartHeight, 9, markers.startDate, true);
			shopStrip.SetMarker(shopMarkers[i]);
		}

		var seasonMarkers = markers.seasons.Where(ValidateRange).ToArray();
		for (int i = 0; i < seasonMarkers.Length; i++)
		{
			TimelineSeasonStrip seasonStrip;
			if (SeasonStrips.Count > i)
			{
				seasonStrip = SeasonStrips[i];
			}
			else
			{
				seasonStrip = SeasonStripScene.Instantiate<TimelineSeasonStrip>();
				stripParent.AddChild(seasonStrip);
				SeasonStrips.Add(seasonStrip);
			}
			seasonStrip.Visible = true;
			PositionStrip(seasonStrip, seasonMarkers[i], seasonStartHeight, 10, markers.startDate);
			seasonStrip.SetMarker(seasonMarkers[i]);
		}

		foreach (var strip in SmallEventStrips.Where(s => s.Visible).OrderBy(s => s.OffsetBottom))
		{
			strip.MoveToFront();
		}
		foreach (var strip in EventStrips.Where(s => s.Visible).OrderBy(s => s.OffsetBottom))
		{
			strip.MoveToFront();
		}
		foreach (var strip in ShopStrips.Where(s => s.Visible))
		{
			strip.MoveToFront();
		}
		foreach (var strip in SeasonStrips.Where(s => s.Visible))
		{
			strip.MoveToFront();
		}

		foreach (var strip in MonthStrips.Where(s => s.Visible))
		{
			strip.MoveToFront();
		}
	}

	void PositionStrip(Control strip, BaseMarker marker, float startHeight, int startOrder, DateTime startDate, bool minWidth = false)
	{
		var dayOffset = (marker.fromDate - startDate).TotalDays;
		strip.ResetOffsets();
		strip.OffsetLeft = (float)(dayOffset * weekSize / 7);
		strip.OffsetRight = strip.OffsetLeft;
		strip.OffsetBottom = startHeight - ((strip.Size.Y + stripSpacing) * marker.lane);
		if (minWidth)
		{
			strip.Size = Vector2.Zero;
			return;
		}
		var dayDuration = (marker.toDate - marker.fromDate).TotalDays;
		strip.CustomMinimumSize = new Vector2((float)(dayDuration * weekSize / 7), strip.CustomMinimumSize.Y);
	}
}
