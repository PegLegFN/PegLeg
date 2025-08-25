using Godot;
using System;
using System.Text.Json;

public partial class CopyMissionTileCtx : BaseContextComponent
{
    public override string Id => "CopyMissionTile";
    GameMission currentMission;
    public override void Update(ContextMenuHook hook)
    {
        currentMission = hook?.missionSource?.currentMission;
        SetDisabled(currentMission is null);
    }

    public void Copy()
    {
        if (currentMission is null)
            return;
        DisplayServer.ClipboardSet(Input.IsKeyPressed(Key.Shift) ? currentMission.tile.ToString() : currentMission.TileIdx.ToString());
        menu.CloseMenu();
    }
}
