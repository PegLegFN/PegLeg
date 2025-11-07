using Godot;
using System;
using System.Text.Json.Nodes;

public partial class ConfigToggleHook : Control
{
    [Signal]
    public delegate void ConfigValueChangedEventHandler(bool newValue);
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

        base._Ready();
        AppConfig.OnConfigChanged += UpdateValue;
        valueIsChanging = true;
        Emit(AppConfig.Get(section, key, defaultValue));
        valueIsChanging = false;
    }

    private void UpdateValue(string section, string key, JsonValue val)
    {
        if (section != this.section || key != this.key)
            return;
        valueIsChanging = true;
        Emit(val.GetValue<bool>());
        valueIsChanging = false;
    }

    private void Emit(bool newVal)
    {
        if (newVal)
            EmitSignalOnTrue();
        else
            EmitSignalOnFalse();
        EmitSignalConfigValueChanged(newVal);
    }

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
