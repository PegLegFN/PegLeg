using Godot;
using System;

public partial class MissionRewardEntry : Control, IRecyclableEntry
{
    [Signal]
    public delegate void MissionCompleteIfAlertEventHandler(bool complete);
    [Export]
    MissionEntry missionEntry;
    [Export]
    GameItemEntry itemEntry;
    public Control node => this;

    public override void _Ready()
    {
        missionEntry.MissionComplete += TryEmitComplete;
    }

    bool knownCompleteState = false;

    private void TryEmitComplete(bool complete)
    {
        knownCompleteState = complete;
        EmitSignalMissionCompleteIfAlert(complete && itemEntry.currentItem?.template.Name.StartsWith("zcp_", StringComparison.OrdinalIgnoreCase) == false);
    }

    IRecyclableElementProvider<MissionRewardPair> provider;
    public void SetRecyclableElementProvider(IRecyclableElementProvider provider)
    {
        if(provider is IRecyclableElementProvider<MissionRewardPair> rewardProvider)
            this.provider = rewardProvider;
    }

    public void SetRecycleIndex(int index)
    {
        if (provider is null)
            return;
        var pair = provider.GetRecycleElement(index);
        missionEntry.SetMission(pair.mission);
        pair.item.SetRewardNotification();
        itemEntry.SetItem(pair.item);
        TryEmitComplete(knownCompleteState);
    }
}
