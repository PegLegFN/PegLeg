using Godot;
using System;
using System.Text.Json.Nodes;

public partial class ConfigRangeHook : Node
{
    [Signal]
    public delegate void ConfigValueChangedEventHandler(double newValue);
    [Signal]
    public delegate void UnappliedLabelChangedEventHandler(string newValue);
    [Signal]
    public delegate void AppliedChangedEventHandler(bool value);

    [Export]
    string section;

    [Export]
    string key;

    [Export]
    bool asInt;

    [Export]
    double defaultValue = 0;

    [Export]
    bool tryBind = true;

    [Export]
    bool requireApply = true;

    [Export]
    bool printWithoutApply = false;


    public override void _Ready()
    {        
        if (tryBind)
        {
            if (HasSignal("value_changed"))
            {
                Connect("value_changed", Callable.From<double>(SetValue));
            }
            if (HasSignal("drag_ended"))
            {
                Connect("drag_ended", Callable.From<bool>(SetValue));
            }
            if ((double?)Get("value") is double)
            {
                ConfigValueChanged += newVal => Set("value", (double)newVal);
            }
        }

        base._Ready();
        EmitSignal(SignalName.AppliedChanged, true);
        AppConfig.OnConfigChanged += UpdateValue;
        var startValue = AppConfig.Get(section, key, defaultValue);
        EmitSignal(SignalName.ConfigValueChanged, startValue);
        EmitSignal(SignalName.UnappliedLabelChanged, startValue.ToString()[..Mathf.Min(startValue.ToString().Length, 4)]);
    }

    private void UpdateValue(string section, string key, JsonValue val)
    {
        if (section != this.section || key != this.key)
            return;
        valueIsChanging = true;
        if (val.TryGetValue(out int intVal))
            EmitSignal(SignalName.ConfigValueChanged, intVal);
        else if (val.TryGetValue(out double doubleVal))
            EmitSignal(SignalName.ConfigValueChanged, doubleVal);
        else
            GD.PushWarning($"Could not get number from config {section}:{key}");
        valueIsChanging = false;
    }

    double unappliedValue;
    public void ApplyValue()
    {
        if (requireApply)
        {
            ApplyValueTyped(unappliedValue, true);
            EmitSignal(SignalName.AppliedChanged, true);
        }
    }

    public void SetValue(bool sliderChanged)
    {
        if (requireApply || valueIsChanging || !sliderChanged)
            return;
        ApplyValueTyped((double)Get("value"), true);
    }

    bool valueIsChanging;
    public void SetValue(double newValue)
    {
        if (!valueIsChanging)
        {
            if (!requireApply)
            {
                ApplyValueTyped(newValue, printWithoutApply);
                EmitSignal(SignalName.UnappliedLabelChanged, newValue.ToString()[..Mathf.Min(newValue.ToString().Length, 4)]);
                EmitSignal(SignalName.AppliedChanged, true);
            }
            else
            {
                unappliedValue = newValue;
                EmitSignal(SignalName.UnappliedLabelChanged, unappliedValue.ToString()[..Mathf.Min(unappliedValue.ToString().Length, 4)]);
                EmitSignal(SignalName.AppliedChanged, false);
            }
        }
    }

    void ApplyValueTyped(double newValue, bool print)
    {
        if(asInt)
            AppConfig.Set(section, key, (int)newValue, print);
        else
            AppConfig.Set(section, key, newValue, print);
    }
}
