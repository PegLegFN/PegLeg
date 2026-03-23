using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class BooleanAny : BooleanAll
{
    protected override void UpdateState()
	{
		bool newState = trackedStates.Count > 0 ? trackedStates.Any(s => s.State) : valueWhenEmpty;
		if (newState != resolvedState)
        {
			resolvedState = newState;
            EmitSignal(SignalName.StateChanged, invert != newState);
        }
	}
}
