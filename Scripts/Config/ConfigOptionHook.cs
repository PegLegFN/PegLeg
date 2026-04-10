using Godot;
using System.Text.Json.Nodes;

public partial class ConfigOptionHook : OptionButton
{
	[Export]
	string section;

	[Export]
	string key;

	[Export]
	bool serialiseId = true;

	bool valueIsChanging;
	int defaultIndex = -1;

	public override void _Ready()
	{
		defaultIndex = Selected;
		AppConfig.OnConfigChanged += OnConfigChanged;
		ItemSelected += _ => WriteToConfig();
		ReadFromConfig();
	}

	private void OnConfigChanged(string section, string key, JsonValue val)
	{
		if (this.section == section && this.key == key)
			ReadFromConfig();
	}

	void ReadFromConfig()
	{
		valueIsChanging = true;
		if (serialiseId)
			Selected = GetItemIndex(AppConfig.Get(section, key, GetItemId(defaultIndex)));
		else
			Selected = AppConfig.Get(section, key, defaultIndex);
		valueIsChanging = false;
	}

	void WriteToConfig()
	{
		if (valueIsChanging)
			return;
		if (serialiseId)
			AppConfig.Set(section, key, GetItemId(Selected));
		else
			AppConfig.Set(section, key, Selected);
	}
}
