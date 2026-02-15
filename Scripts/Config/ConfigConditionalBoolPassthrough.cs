using Godot;
using System;
using System.Text.Json.Nodes;

public partial class ConfigConditionalBoolPassthrough : Node
{
    [Signal]
    public delegate void OutputChangedEventHandler(bool newValue);

    [Export]
    string section;
    [Export]
    string key;
    [Export]
    bool defaultValue = false;
    [Export]
    bool requiredState = true;
    [Export]
    bool fallbackOutput = false;

    public override void _Ready()
    {
        AppConfig.OnConfigChanged += UpdateValue;
    }

    private void UpdateValue(string section, string key, JsonValue val)
    {
        if (section != this.section || key != this.key)
            return;
        Output(lastOutput);
    }

    bool lastOutput;
    public void Output(bool newValue)
    {
        lastOutput = newValue;
        EmitSignalOutputChanged(AppConfig.Get(section, key, defaultValue) == requiredState ? newValue : fallbackOutput);
    }
}
