using Godot;
using System.Text.Json.Nodes;

public partial class ConfigFoldoutHook : FoldableContainer
{
	[Export]
	string section;

	[Export]
	string key;

	[Export]
	bool defaultValue = false;

	[Export]
	bool logChanges = false;

	bool valueIsChanging;

	public void UpdateTargetSetting(string section, string key)
	{
		this.section = section ?? this.section;
		this.key = key ?? this.key;

		valueIsChanging = true;
		Emit(AppConfig.Get(this.section, this.key, defaultValue));
		valueIsChanging = false;
	}

	public override void _Ready()
	{
		FoldingChanged += SetInvertedValue;
		AppConfig.OnConfigChanged += UpdateValue;
		valueIsChanging = true;
		Emit(AppConfig.Get(section, key, defaultValue));
		valueIsChanging = false;
	}

	private void UpdateValue(string section, string key, JsonNode val)
	{
		if (section != this.section || key != this.key)
			return;
		valueIsChanging = true;
		Emit(val.GetValue<bool>());
		valueIsChanging = false;
	}

	private void Emit(bool newVal)
	{
		Folded = !newVal;
		if (logChanges)
			GD.Print($"{section}:{key} = {newVal}");
	}

	void SetInvertedValue(bool newFoldedValue) => SetValue(!newFoldedValue);
	public void SetValue(bool newValue)
	{
		if (!valueIsChanging)
			AppConfig.Set(section, key, newValue);
	}

	public override void _ExitTree()
	{
		AppConfig.OnConfigChanged -= UpdateValue;
	}
}
