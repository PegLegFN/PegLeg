using Godot;
using System.Linq;

public partial class InterfaceSettings : Control
{
	[Export]
	OptionButton preferredScreenOptions;

	public override void _Ready()
	{
		if (preferredScreenOptions is null)
			return;
		int count = DisplayServer.GetScreenCount();
		string[] screenNames = ["Auto", .. Enumerable.Range(1, count).Select(i => $"Display {i}")];
		int currentIdx = AppConfig.Get("ui", "preferred_screen", -1) + 1;
		preferredScreenOptions.Clear();
		for ( int i = 0; i < screenNames.Length; i++)
		{
			preferredScreenOptions.AddItem(screenNames[i]);
		}
		preferredScreenOptions.Selected = currentIdx;
		preferredScreenOptions.ItemSelected += SelectPreferredScreen;
	}

	private void SelectPreferredScreen(long index)
	{
		AppConfig.Set("ui", "preferred_screen", (int)(index - 1));
	}
}
