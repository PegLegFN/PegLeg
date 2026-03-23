using Godot;

public partial class MissionCollectionsController : Control
{
	public async void Reload()
	{
		await GameMission.UpdateMissions();
	}

	//serialise colections
}
