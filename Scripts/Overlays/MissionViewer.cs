using Godot;
using System;
using System.Linq;

public partial class MissionViewer : ModalWindow
{
	static MissionViewer instance;
	[Export]
	MissionEntry missionEntry;
    [Export]
    MissionEntry secondMissionEntry;


    public override void _Ready()
	{
		instance = this;
        base._Ready();
	}

	public static void ShowMission(GameMission mission)
	{
		instance.missionEntry.SetMission(mission);
        instance.secondMissionEntry.SetMission(mission);
        instance.SetWindowOpen(true);
	}

    public override void _ShortcutInput(InputEvent @event)
    {
        if (
            !IsVisibleInTree() ||
            !@event.DevTextKeybindPressed() ||
            !IsTopOfStack() ||
            missionEntry.currentMission is null
            )
            return;
        var m = missionEntry.currentMission;
        DevTextOverlay.ShowTabs([
            ["Mission", m.missionData?.ToString()],
            ["Generator", m.missionGenerator?.rawData?.ToString()],
            ["Zone", m.zoneTheme?.rawData?.ToString()],
            ["Difficulty", m.difficultyInfo?.ToString()],
            ["Tile", m.tile?.ToString()],
            ["Alert", m.alertData?.ToString()],
            ["Search Tags", m.searchTags?.ToString()],
            ["Regions", string.Join(",\n", m.regions?.Select(r => r.ToString()) ?? [])],
            ["Fulfillments", string.Join(",\n", m.alertFulfillments ?? [])]
        ]);
    }
}
