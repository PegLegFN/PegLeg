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
            @event is not InputEventKey keyEvent ||
            !keyEvent.Pressed ||
            !keyEvent.ShiftPressed ||
            keyEvent.CtrlPressed ||
            !keyEvent.AltPressed ||
            missionEntry.currentMission is null
            )
            return;
        var m = missionEntry.currentMission;
        var text = keyEvent.Keycode switch
        {
            Key.M => m.missionData.ToString(),
            Key.G => m.missionGenerator.rawData.ToString(),
            Key.Z => m.zoneTheme.rawData.ToString(),
            Key.D => m.difficultyInfo.ToString(),
            Key.T => m.tile.ToString(),
            Key.A => m.alertData.ToString(),
            Key.S => m.searchTags.ToString(),
            Key.E => string.Join(",\n", m.regions.Select(r => r.ToString())),
            _ => null
        };

        if(text is not null)
            DevTextOverlay.ShowText(text);
    }
}
