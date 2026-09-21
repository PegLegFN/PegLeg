using Godot;
using System;
using System.Xml.Linq;

public partial class TriggerButton : Button
{
	[Export]
	bool Disable;
	[Export]
	string name;
	[Export]
	string[] context;

	public override async void _Ready()
	{
		UpdateTrigger(name);
		TriggerInstance.OnTriggersChanged += UpdateTrigger;
		Pressed += () => TriggerInstance.Trigger(name, context);
	}

	public override void _ExitTree()
	{
		TriggerInstance.OnTriggersChanged -= UpdateTrigger;
	}

	void UpdateTrigger(string updated)
	{
		if (updated == name)
			Visible = TriggerInstance.HasTrigger(name) && !Disable;
	}
}
