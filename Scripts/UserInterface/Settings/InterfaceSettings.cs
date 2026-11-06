using Godot;
using System.Linq;

public partial class InterfaceSettings : Control
{
	[Export]
	OptionButton preferredScreenOptions;
	[Export]
	OptionButton preferredNotifScreenOptions;

	public override void _Ready()
	{
#if GODOT_PC
		int count = DisplayServer.GetScreenCount();
		string[] screenNames = ["Auto", .. Enumerable.Range(1, count).Select(i => $"Display {i}")];

		SetupOptions(preferredScreenOptions, screenNames, "preferred_screen");
		SetupOptions(preferredNotifScreenOptions, screenNames, "preferred_notif_screen");
#else
		preferredScreenOptions?.Disabled = true;
		preferredNotifScreenOptions?.Disabled = true;
#endif
	}

	void SetupOptions(OptionButton button, string[] names, string config)
	{
		if (button is null)
			return;
		button.Clear();
		for (int i = 0; i < names.Length; i++)
		{
			button.AddItem(names[i]);
		}
		button.Selected = AppConfig.Get("ui", config, -1) + 1;
		button.ItemSelected += index => AppConfig.Set("ui", config, (int)(index - 1));
	}
}
