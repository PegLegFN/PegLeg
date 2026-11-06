using Godot;
using System;
using System.Collections.Generic;

public partial class MissionInterfaceNew : Control
{
	[Export]
	ScrollContainer scrollBox;
	[Export]
	MissionMapInput mapInputSource;
	[Export]
	Control dragBlocker;
	[Export]
	Control theaterScaler;
	[Export]
	Control theaterContainer;
	[Export]
	VirtualTabBar zoneTabs;
	[Export]
	Control venturesBar;
	[Export]
	MissionEntry previewMission;
	[Export]
	MissionTheaterTiles[] theaters;
	[Export]
	float minZoom = 0.5f;
	[Export]
	float maxZoom = 2.0f;
	[Export]
	float theaterPadding = 2500.0f;

	public override async void _Ready()
	{
		zoneTabs.TabsChanged += UpdateTabs;
		mapInputSource.OnInput += MapInput;
		mapInputSource.MouseEntered += () => mapHovered = true;
		mapInputSource.MouseExited += () => mapHovered = false;
		theaterContainer.GrowHorizontal = GrowDirection.End;
		theaterContainer.GrowVertical = GrowDirection.End;

		//TestPrint();
		ProcessPriority = 1;
		UpdateMapSize();
		foreach (var theater in theaters)
		{
			theater.ItemRectChanged += () => mapUpdateQueued = true;
			theater.MissionSelected += SelectMission;
			theater.MissionHovered += HoverMission;
		}
		VisibilityChanged += TryLoadMissions;
		GameMission.OnMissionsInvalidated += InvalidateMissions;
		GameMission.OnMissionsUpdated += MarkMissionsForUpdate;
		TryLoadMissions();
	}

	public override void _ExitTree()
	{
		GameMission.OnMissionsInvalidated -= InvalidateMissions;
		GameMission.OnMissionsUpdated -= MarkMissionsForUpdate;
	}


	private async void UpdateTabs()
	{
		venturesBar.Visible = zoneTabs.LatestTab == 5 && GameAccount.ActiveAccount.isOwned;
		UpdateMap();
		await Helpers.WaitForFrame();
		scrollBox.ScrollHorizontal = (int)(mapInputSource.Size.X - scrollBox.Size.X) / 2;
		scrollBox.ScrollVertical = (int)(mapInputSource.Size.Y - scrollBox.Size.Y) / 2;
	}

	bool mapHovered;
	bool mapDragging;
	MouseButton dragSource;
	int dragProgress;
	Vector2I mapDragStart;
	Vector2 mapDragMouseStart;
	public void MapInput(InputEvent @event)
	{
		if (mapHovered && !mapDragging)
		{
			if (@event is InputEventMouseButton mouseBtn && mouseBtn.Pressed)
			{
				if (mouseBtn.ButtonIndex == MouseButton.Middle)
				{
					dragSource = mouseBtn.ButtonIndex;
					mapDragging = true;
					mapDragStart = new(scrollBox.ScrollHorizontal, scrollBox.ScrollVertical);
					mapDragMouseStart = mouseBtn.GlobalPosition;
					dragBlocker.Visible = true;
					dragBlocker.GrabFocus();
					GetViewport().SetInputAsHandled();
				}
				else if (mouseBtn.ButtonIndex == MouseButton.WheelUp && !mouseBtn.AltPressed)
				{
					Zoom(0.2f);
				}
				else if(mouseBtn.ButtonIndex == MouseButton.WheelDown && !mouseBtn.AltPressed)
				{
					Zoom(-0.2f);
				}

				void Zoom(float amount)
				{
					Vector2I startingScroll = new(scrollBox.ScrollHorizontal, scrollBox.ScrollVertical);
					var mousePos = mouseBtn.Position;

					var startScale = MapZoom;
					MapZoom *= 1 + amount;

					var scaleDiff = MapZoom / startScale;
					var newScrollDiff = (mousePos * scaleDiff) - mousePos;
					var newScroll = startingScroll + (Vector2I)newScrollDiff;

					scrollBox.ScrollHorizontal = newScroll.X;
					scrollBox.ScrollVertical = newScroll.Y;

					Helpers.Defer(() =>
					{
						scrollBox.ScrollHorizontal = newScroll.X;
						scrollBox.ScrollVertical = newScroll.Y;
					});
					GetViewport().SetInputAsHandled();
				}
			}
		}
		if (mapDragging)
		{
			if (@event is InputEventMouseButton mouseBtn)
			{
				if (mouseBtn.ButtonIndex == MouseButton.Middle && !mouseBtn.Pressed)
				{
					mapDragging = false;
					dragBlocker.Visible = false;
					dragProgress = 0;
					GetViewport().SetInputAsHandled();
				}
			}
			if (@event is InputEventMouseMotion mouseDrag)
			{
				var diff = mapDragMouseStart - mouseDrag.GlobalPosition;
				var target = mapDragStart + (Vector2I)diff;

				scrollBox.ScrollHorizontal = target.X;
				scrollBox.ScrollVertical = target.Y;
				GetViewport().SetInputAsHandled();
			}
		}
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event is InputEventKey key)
		{
			if (key.Keycode == Key.R && key.Pressed)
			{
				GD.Print("Reload");
				TryLoadMissions();
				GetViewport().SetInputAsHandled();
			}
		}
	}

	private void InvalidateMissions()
	{
		foreach (var theater in theaters)
		{
			theater.Visible = false;
		}
		MapSize = Vector2.Zero;
		ClearVisibleMission();
	}

	void MarkMissionsForUpdate()
	{
		missionsNeedUpdate = true;
		TryLoadMissions();
	}

	bool missionsNeedUpdate = true;
	async void TryLoadMissions()
	{
		if (!IsVisibleInTree())
			return;
		if (!missionsNeedUpdate)
			return;
		missionsNeedUpdate = false;
		ClearVisibleMission();
		foreach (var theater in theaters)
		{
			theater.LoadTiles();
		}
		UpdateMap();
	}

	bool mapUpdateQueued;

	public override void _Process(double delta)
	{
		if (mapUpdateQueued)
		{
			mapUpdateQueued = false;
			UpdateMap();
		}
	}

	void UpdateMap()
	{
		if (theaters.Length < 5)
			return;

		var origin = Vector2.One * theaterPadding;

		if (zoneTabs.LatestTab == 0)
		{
			foreach (var theater in theaters)
			{
				theater.Visible = true;
			}

			float firstRowHeight = Mathf.Max(theaters[0].Size.Y, theaters[1].Size.Y);
			float firstColumnWidth = Mathf.Max(theaters[0].Size.X, theaters[2].Size.X);

			theaters[0].Position = origin;
			theaters[1].Position = origin + Vector2.Right * firstColumnWidth;

			theaters[2].Position = origin + Vector2.Down * firstRowHeight;
			theaters[3].Position = origin + new Vector2(firstColumnWidth, firstRowHeight);

			float secondRowHeight = firstRowHeight + Mathf.Max(theaters[2].Size.Y, theaters[3].Size.Y);
			theaters[4].Position = origin + new Vector2(firstColumnWidth - theaters[4].Size.X * 0.5f, secondRowHeight);

			MapSize = new(firstColumnWidth + Mathf.Max(theaters[1].Size.X, theaters[3].Size.X) + theaterPadding*2, secondRowHeight + theaters[4].Size.Y + theaterPadding * 2);
		}
		else
		{
			var tIdx = zoneTabs.LatestTab - 1;
			for (int i = 0; i < theaters.Length; i++)
			{
				theaters[i].Visible = i == tIdx;
			}
			theaters[tIdx].Position = origin;
			MapSize = theaters[tIdx].Size + origin * 2;
		}
	}

	void HoverMission(string guid)
	{
		SetVisibleMission(GameMission.MissionDict.GetValueOrDefault(guid));
	}

	void SelectMission(string guid)
	{
		selectedMission = GameMission.MissionDict.GetValueOrDefault(guid);
		SetVisibleMission();
	}

	GameMission lastHoveredMission;
	GameMission selectedMission;
	void SetVisibleMission(GameMission mission = null)
	{
		if (mission is not null)
			lastHoveredMission = mission;
		mission ??= selectedMission ?? lastHoveredMission;
		previewMission?.Visible = mission is not null;
		previewMission?.SetMission(mission);
	}

	void ClearVisibleMission()
	{
		selectedMission = null;
		lastHoveredMission = null;
		previewMission?.Visible = false;
		previewMission?.ClearMission();
	}

	Vector2 MapSize
	{
		get => field;
		set
		{
			field = value;
			UpdateMapSize();
		}
	}

	float MapZoom
	{
		get => field;
		set
		{
			field = Mathf.Clamp(value, minZoom, maxZoom);
			UpdateMapSize();
		}
	} = 1;

	void UpdateMapSize()
	{
		theaterScaler?.OffsetTransformScale = Vector2.One * MapZoom;
		Vector2 trueMinSize = scrollBox?.GetCombinedMinimumSize() ?? Vector2.Zero;
		theaterScaler?.CustomMinimumSize = trueMinSize.Max(MapSize * MapZoom);
		theaterContainer?.CustomMinimumSize = MapSize.Max(trueMinSize / MapZoom);
	}
}
