using Godot;
using System;

public partial class TempCosmeticFallback : Node
{
	[Export]
	TabContainer tabs;
	[Export]
	int standardIndex;
	[Export]
	int fallbackIndex;
	[Export]
	string requiredMagicBool;
	public override void _Ready()
	{
		bool fallback = PegLegResourceManager.MagicNumbers[requiredMagicBool]?.GetValue<bool>() ?? false;
		tabs.SetTabHidden(standardIndex, fallback);
		tabs.SetTabHidden(fallbackIndex, !fallback);
	}
}
