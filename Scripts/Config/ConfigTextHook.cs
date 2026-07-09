using Godot;
using System.Text.Json.Nodes;

public partial class ConfigTextHook : Control
{
	[Signal]
	public delegate void ConfigValueChangedEventHandler(string newValue);

	[Export]
	string section;

	[Export]
	string key;

	[Export]
	string defaultValue = "";

	[Export]
	bool tryBind = true;

	[Export]
	bool accountMode = false;

	[Export]
	double cooldown = 0.5;


	public void UpdateTargetSetting(string section, string key)
	{
		this.section = section ?? this.section;
		this.key = key ?? this.key;
		EmitSignal(SignalName.ConfigValueChanged, GetCurrentValue());
	}

	public override void _Ready()
	{
		if (tryBind)
		{
			if ((string)Get("text") is not null)
			{
				ConfigValueChanged += SetText;
				SetText(GetCurrentValue());
			}
			if (HasSignal("text_changed"))
			{
				if (Get("highlight_all_occurrences").VariantType == Variant.Type.Bool)
					Connect("text_changed", Callable.From(() => TrySetValue((string)Get("text"))));
				else
					Connect("text_changed", Callable.From<string>(TrySetValue));
			}
		}

		base._Ready();
		if (accountMode)
			GameAccount.LocalDataChanged += UpdateAccountValue;
		else
			AppConfig.OnConfigChanged += UpdateConfigValue;
		EmitSignal(SignalName.ConfigValueChanged, GetCurrentValue());
	}

	public override void _ExitTree()
	{
		if (accountMode)
			GameAccount.LocalDataChanged -= UpdateAccountValue;
		else
			AppConfig.OnConfigChanged -= UpdateConfigValue;
	}

	private void SetText(string newVal)
	{
		//if (!valueIsChanging)
		Set("text", newVal);
	}

	private void UpdateConfigValue(string section, string key, JsonNode val)
	{
		if (section != this.section || key != this.key || editingValue)
			return;
		//valueIsChanging = true;
		EmitSignal(SignalName.ConfigValueChanged, val.GetValue<string>());
		//valueIsChanging = false;
	}

	private void UpdateAccountValue(string key)
	{
		if (key != this.key || editingValue)
			return;
		EmitSignal(SignalName.ConfigValueChanged, GetCurrentValue());
	}

	string nextValue;
	double currentCooldown = 0;
	bool editingValue = false;
	public void TrySetValue(string newValue)
	{
		currentCooldown = cooldown;
		if (currentCooldown <= 0)
			SetValue(newValue);
		else
			nextValue = newValue;
	}

	string GetCurrentValue()
	{
		if (accountMode)
			return GameAccount.ActiveAccount.GetLocalData(key)?.ToString() ?? defaultValue;
		return AppConfig.Get(section, key, defaultValue);
	}

	void SetValue(string newValue)
	{
		editingValue = true;
		if (accountMode)
		{
			GD.Print($"Set Account Data ({key}={newValue})");
			GameAccount.ActiveAccount.SetLocalData(key, newValue);
		}
		else
			AppConfig.Set(section, key, newValue);
		editingValue = false;
	}

	public override void _Process(double delta)
	{
		if (currentCooldown <= 0)
			return;
		currentCooldown -= delta;
		if (currentCooldown <= 0)
			SetValue(nextValue);
	}
}
