using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class BooleanAll : Node, BooleanStateProvider
{
    [Signal]
    public delegate void StateChangedEventHandler(bool value);
	[Export]
    protected bool valueWhenEmpty = false;
    [Export]
    protected bool invert = false;

    public override void _Ready()
	{
		ChildEnteredTree += NodeAdded;
		ChildEnteredTree += NodeRemoved;
		trackedStates = GetChildren().Select(n => n is BooleanStateProvider state ? state : null).Where(s => s is not null).ToList();
		UpdateState();
    }

	void NodeAdded(Node added)
	{
		if(added is BooleanStateProvider state)
		{
			trackedStates.Add(state);
			UpdateState();
		}
	}

    void NodeRemoved(Node removed)
    {
        if (removed is BooleanStateProvider state)
        {
            trackedStates.Add(state);
            UpdateState();
        }
    }

    protected List<BooleanStateProvider> trackedStates = [];
    protected bool? resolvedState = null;
	public bool State => invert != (resolvedState ?? valueWhenEmpty);
	protected virtual void UpdateState()
	{
		bool newState = trackedStates.Count > 0 ? !trackedStates.Any(s => !s.State) : valueWhenEmpty;
		if (newState != resolvedState)
        {
			resolvedState = newState;
            EmitSignal(SignalName.StateChanged, invert != newState);
        }
	}
}
