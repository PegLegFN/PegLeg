using Godot;
using System;

public partial class MissionMapInput : Control
{
	public event Action<InputEvent> OnInput;
	public override void _GuiInput(InputEvent @event)
	{
		OnInput?.Invoke(@event);
	}
}
