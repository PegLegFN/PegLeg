using Godot;
using System;
using System.Collections.Generic;

public partial class TimelineMinimap : Control
{
	[Export]
	PackedScene markerScene;
	[Export]
	Control markerParent;
	[Export]
	ScrollContainer scrollContainer;
	[Export]
	ScrollContainer fullScrollContainer;
	[Export]
	ScrollBar scrollBar;
	[Export]
	Control customPage;
	[Export]
	float sizeOfWeek;
	[Export]
	float sizeOfLane;

	Control scrollChild;
	List<Control> markerPool = [];
	List<Control> markerInstances = [];

	public override void _Ready()
	{
		scrollChild = scrollContainer.GetChild<Control>(0);
		fullScrollContainer.ItemRectChanged += FullScrollSizeChanged;
		scrollContainer.GetHScrollBar().Value = 0;
		scrollBar.MaxValue = 3000;
		FullScrollSizeChanged();
		scrollBar.ValueChanged += OnScrollChanged;
	}

	private async void FullScrollSizeChanged()
	{
		await Helpers.WaitForFrame();
		var fullsb = fullScrollContainer.GetHScrollBar();
		double fullPagePercent = fullsb.Page / fullsb.MaxValue;
		var mmsb = scrollContainer.GetHScrollBar();
		fullPagePercent *= mmsb.MaxValue / mmsb.Page;
		scrollBar.Page = fullPagePercent * 3000;
		OnScrollChanged(scrollBar.Value);
	}

	public void OnScrollChanged(double v)
	{
		var normalised = v / (scrollBar.MaxValue - scrollBar.Page);
		var mmsb = scrollContainer.GetHScrollBar();
		mmsb.Value = normalised * (mmsb.MaxValue - mmsb.Page);
		var fullsb = fullScrollContainer.GetHScrollBar();
		fullsb.Value = normalised * (fullsb.MaxValue - fullsb.Page);
		if (customPage is not null)
		{
			customPage.AnchorLeft = (float)normalised;
			customPage.AnchorRight = customPage.AnchorLeft;
		}
	}

	public void SpawnMarkers(TimelineInterface.Markers markerCollection)
	{
		scrollChild.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
		foreach (var item in markerInstances)
		{
			item.Visible = false;
			markerPool.Add(item);
		}
		markerInstances.Clear();
		foreach (var m in markerCollection.seasons)
		{
			PositionMarker(m, markerCollection.startDate, 0);
		}
		foreach (var m in markerCollection.shops)
		{
			PositionMarker(m, markerCollection.startDate, 1);
		}
		foreach (var m in markerCollection.questlines)
		{
			PositionMarker(m, markerCollection.startDate, 2);
		}
		foreach (var m in markerCollection.events)
		{
			PositionMarker(m, markerCollection.startDate, 2 + markerCollection.maxQuestlineLanes);
		}
		scrollChild.SizeFlagsHorizontal = SizeFlags.ExpandFill;
	}

	void PositionMarker(TimelineInterface.BaseMarker marker, DateTime startDate, int baseLane)
	{
		Control markerInst = null;
		if (markerPool.Count > 0)
		{
			markerInst = markerPool[0];
			markerInst.Visible = true;
		}
		else
		{
			markerInst = markerScene.Instantiate<Control>();
			markerParent.AddChild(markerInst);
			markerInst.AnchorTop = 1;
			markerInst.AnchorBottom = 1;
			markerInst.AnchorLeft = 0;
			markerInst.AnchorRight = 0;
			markerInst.GrowHorizontal = GrowDirection.End;
			markerInst.GrowVertical = GrowDirection.Begin;
		}

		int startDay = (int)(marker.fromDate - startDate).TotalDays;
		int endDay = (int)(marker.toDate - startDate).TotalDays;
		//int duration = (int)(marker.toDate - marker.fromDate).TotalDays;

		var col = marker.color;
		col.A = 1;
		markerInst.Modulate = col;
		markerInst.CustomMinimumSize = new(0, sizeOfLane);
		markerInst.AnchorLeft = (float)(startDay / 7.0);
		markerInst.AnchorRight = (float)(endDay / 7.0);
		//markerInst.CustomMinimumSize = new(duration * (sizeOfWeek / 7), sizeOfLane);
		markerInst.ResetOffsets();
		//markerInst.OffsetLeft = startDay * (sizeOfWeek / 7);
		markerInst.OffsetBottom = (baseLane + marker.lane) * -sizeOfLane;
		if (marker is TimelineInterface.EventMarker e)
		{
			markerInst.TooltipText = e.displayName ?? e.eventQuest;
		}
		else if (marker is TimelineInterface.QuestlineMarker q)
		{
			markerInst.TooltipText = q.eventFlag;
		}
		else
		{
			markerInst.TooltipText = "";
		}
	}
}
