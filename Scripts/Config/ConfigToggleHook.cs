using Godot;
using System.Text.Json.Nodes;

public partial class ConfigToggleHook : Control
{
	[Signal]
	public delegate void ConfigValueChangedEventHandler(bool newValue);
	[Signal]
	public delegate void ConfigValueChangedInverseEventHandler(bool newValue);
	[Signal]
	public delegate void OnTrueEventHandler();
	[Signal]
	public delegate void OnFalseEventHandler();

	[Export]
	string section;

	[Export]
	string key;

	[Export]
	bool defaultValue = false;

	[Export]
	bool tryBind = true;
	[Export]
	bool accountMode = false;
	[Export]
	bool logChanges = false;
	[Export]
	bool disabled = false;

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
		if (disabled)
			return;

		if (tryBind)
		{
			if (HasSignal("toggled"))
			{
				Connect("toggled", Callable.From<bool>(SetValue));
			}
			if ((bool?)Get("button_pressed") is bool)
			{
				ConfigValueChanged += newVal => Set("button_pressed", newVal);
			}
		}

		if (accountMode)
		{
			GameAccount.LocalDataChanged += UpdateAccountValue;
			GameAccount.ActiveAccountChanged += UpdateAccount;
		}
		else
			AppConfig.OnConfigChanged += UpdateValue;
		valueIsChanging = true;
		Emit(GetCurrentValue());
		valueIsChanging = false;
	}

	public override void _ExitTree()
	{
		if (accountMode)
		{
			GameAccount.LocalDataChanged -= UpdateAccountValue;
			GameAccount.ActiveAccountChanged -= UpdateAccount;
		}
		else
			AppConfig.OnConfigChanged -= UpdateValue;
	}

	private void UpdateValue(string section, string key, JsonNode val)
	{
		if (section != this.section || key != this.key)
			return;
		valueIsChanging = true;
		Emit(val.GetValue<bool>());
		valueIsChanging = false;
	}

	private void UpdateAccountValue(string key)
	{
		if (key != this.key)
			return;
		Emit(GetCurrentValue());
	}

	private void UpdateAccount()
	{
		Emit(GetCurrentValue());
	}

	bool GetCurrentValue()
	{
		if (accountMode)
			return GameAccount.ActiveAccount.GetLocalData(key)?.GetValue<bool>() ?? defaultValue;
		return AppConfig.Get(section, key, defaultValue);
	}

	private void Emit(bool newVal)
	{
		if (newVal)
			EmitSignalOnTrue();
		else
			EmitSignalOnFalse();
		EmitSignalConfigValueChanged(newVal);
		EmitSignalConfigValueChangedInverse(!newVal);
		if (logChanges)
			GD.Print($"{section}:{key} = {newVal}");
	}

	public void SetValue(bool newValue)
	{
		if (valueIsChanging)
			return;
		if (accountMode)
		{
			GD.Print($"Set Account Data for {GameAccount.ActiveAccount.DisplayName} ({key}={newValue})");
			GameAccount.ActiveAccount.SetLocalData(key, newValue);
		}
		else
			AppConfig.Set(section, key, newValue);
	}
}
