using Godot;
using System;

public partial class MissionCollectionsController : Control
{
	public async void Reload()
	{
        await GameMission.UpdateMissions();
    }

    //serialise colections
}
