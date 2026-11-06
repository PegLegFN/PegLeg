using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using StringDict = Godot.Collections.Dictionary<string, string>;
using ColorDict = Godot.Collections.Dictionary<string, Godot.Color>;
using RuleDict = Godot.Collections.Dictionary<string, string[]>;

public partial class MissionTheaterTiles : Control
{
	[Signal]
	public delegate void MissionSelectedEventHandler(string guid);
	[Signal]
	public delegate void MissionHoveredEventHandler(string guid);
	[Export]
	string testTargetCat;
	[ExportGroup("Environment")]
	[Export]
	float envPaddingFactor;
	[Export]
	int envDownsampling = 4;
	[Export]
	Texture2D envBlitTexture;
	[Export]
	Vector2 envCellSize;
	[Export]
	Vector2 envOffset;
	[ExportSubgroup("Clouds")]
	[Export]
	Material cloudBlitMat;
	[Export]
	ShaderHook cloudTarget;
	[ExportSubgroup("Biomes")]
	[Export]
	StringDict defaultBiomeMap;
	[Export]
	RuleDict biomeRules;
	[Export]
	ColorDict biomeColours;
	[Export]
	Material biomeBlitMat;
	[Export]
	ShaderHook biomeTarget;
	[ExportGroup("Mission Nodes")]
	[Export]
	int prespawnNodes = 40;
	[Export]
	PackedScene missionNodeScene;
	[Export]
	Control missionNodeParent;
	[Export]
	Vector2 nodePadding;

	DrawableTexture2D environmentTexture;

	List<BiomeRule> biomeRuleList;
	readonly record struct BiomeRule(string id, string[] rules, Color colour);

	List<MissionEntry> missionNodeList = [];

	public override void _Ready()
	{
		Setup();
	}

	bool isSetup;
	void Setup()
	{
		if (isSetup)
			return;
		isSetup = true;
		var biomeKeys = biomeRules.Keys.Intersect(biomeColours.Keys).ToArray();
		biomeRuleList = [.. biomeKeys.Select(k => new BiomeRule(k, biomeRules[k], biomeColours[k]))];

		environmentTexture = new();

		cloudTarget?.Texture = environmentTexture;
		biomeTarget?.Texture = environmentTexture;

		ItemRectChanged += OnItemRectChanged;

		//for (int i = 0; i < prespawnNodes; i++)
		//{
		//	CreateMissionEntry();
		//}
	}


	string hoveredGUID = null;
	void CreateMissionEntry()
	{
		var newMission = missionNodeScene.Instantiate<MissionEntry>();
		newMission.MissionSelected += EmitSignalMissionSelected;
		newMission.MissionHovered += guid =>
		{
			EmitSignalMissionHovered(guid);
			hoveredGUID = guid;
		};
		newMission.MissionUnhovered += guid =>
		{
			if (hoveredGUID == guid)
			{
				EmitSignalMissionHovered(null);
				hoveredGUID = null;
			}
		};
		missionNodeParent.AddChild(newMission);
		missionNodeList.Add(newMission);
	}

	private void OnItemRectChanged()
	{
		cloudTarget?.SetShaderVector(-Position, "offset");
	}

	public async void LoadTiles()
	{
		Setup();
		var theater = GameMission.TheaterList.FirstOrDefault(t => t.category == testTargetCat);
		if (theater is null)
		{
			GD.Print("Theater Tiles: No Theater");
			return;
		}

		var missions = theater.missions;//filter playable by account
		var tiles = theater.tiles;

		var tileCoords = tiles.Select(t => t.Coordinates).ToArray();
		var xCoords = tileCoords.Select(c => c.X).ToArray();
		var yCoords = tileCoords.Select(c => c.Y).ToArray();

		Vector2I boundsStart = new(xCoords.Min(), yCoords.Min());
		Vector2I boundsSize = new Vector2I(xCoords.Max() + 1, yCoords.Max() + 1) - boundsStart;
		//increase bounds in stonewood/canny/twine to compensate for Storm Shield/Storm King landmarks

		Size = envOffset * 2 + (boundsSize * envCellSize * new Vector2(0.75f, 1));

		//todo: use a blit to just reset the alpha, without resetting the biomes
		var defaultBiomeColour = biomeColours.GetValueOrDefault(defaultBiomeMap.GetValueOrDefault(theater.uniqueId, "Water"), Colors.White);
		environmentTexture.Setup((int)Size.X / envDownsampling, (int)Size.Y / envDownsampling, DrawableTexture2D.DrawableFormat.Rgba8, defaultBiomeColour);

		List<Rect2I> cloudRects = [];
		Rect2I? portalRect = null;
		foreach (var tile in tiles)
		{
			var colour = defaultBiomeColour;
			string zoneTheme = tile.GetZoneTheme()?.ItemName ?? tile.zoneTheme?.Split('.')[^1] ?? "";
			foreach (var biome in biomeRuleList)
			{
				if (biome.rules.Any(r => Regex.IsMatch(zoneTheme, r)))
				{
					colour = biome.colour;
					break;
				}
			}
			var cell = tile.Coordinates - boundsStart;
			cell.Y = boundsSize.Y - 1 - cell.Y;
			var rect = GetStampRect(cell);
			var cloudRect = rect;
			if (zoneTheme == "The Portal")
			{
				var oldSize = rect.Size;
				rect.Size *= 2;
				rect.Position -= (rect.Size-oldSize) / 2;
				portalRect = rect;
			}
			StampEnv(rect, biomeBlitMat, colour);
			cloudRects.Add(cloudRect);
		}
		if (portalRect is not null)
			StampEnv(portalRect.Value, biomeBlitMat, biomeRuleList.FirstOrDefault(b => b.id == "Spooky").colour is Color bCol && bCol != default ? bCol : defaultBiomeColour);
		foreach (var rect in cloudRects)
		{
			StampEnv(rect, cloudBlitMat);
		}

		for (int i = 0; i < missionNodeList.Count; i++)
		{
			missionNodeList[i].ClearMission();
			missionNodeList[i].Visible = false;
		}

		Vector2 nodeSize = Vector2.Zero;
		if (missions.Length > missionNodeList.Count)
			GD.Print($"Theater {testTargetCat}: About to spawn {missions.Length - missionNodeList.Count} mission nodes");

		for (int i = 0; i < missions.Length; i++)
		{
			if (i >= missionNodeList.Count)
			{
				CreateMissionEntry();
			}
			var missionEntry = missionNodeList[i];
			if (i == 0)
				nodeSize = missionEntry.GetCombinedMinimumSize();
			missionEntry.SetMission(missions[i]);

			var tileCell = missions[i].tile.Coordinates - boundsStart;
			tileCell.Y = boundsSize.Y - 1 - tileCell.Y;
			var extraOffset = (tileCell.X % 2 == 1) ? nodeSize.Y * -0.5f : 0;
			missionEntry.Position = (Vector2.Down * extraOffset) + (tileCell * (nodeSize * new Vector2(0.75f, 1) + nodePadding));

			missionEntry.Visible = true;
		}

		//for (int i = missions.Length; i < missionNodeList.Count; i++)
		//{
		//	missionNodeList[i].ClearMission();
		//	missionNodeList[i].Visible = false;
		//}
	}

	Rect2I GetStampRect(Vector2I cell)
	{
		var paddingAmount = envCellSize.X * envPaddingFactor;
		var extraOffset = (cell.X % 2 == 1) ? envCellSize.Y * -0.5f : 0;
		var blitPos = envOffset + (Vector2.Down * extraOffset) + (cell * envCellSize * new Vector2(0.75f, 1)) - (Vector2.One * paddingAmount);
		var blitSize = (int)(envCellSize.X + paddingAmount * 2);
		return new((Vector2I)blitPos / envDownsampling, Vector2I.One * blitSize / envDownsampling);
	}

	void StampEnv(Rect2I rect, Material blitMat) => StampEnv(rect, blitMat, Colors.White);
	void StampEnv(Rect2I rect, Material blitMat, Color modulate) => 
		environmentTexture.BlitRect(rect, envBlitTexture, material: blitMat, modulate: modulate);
}
