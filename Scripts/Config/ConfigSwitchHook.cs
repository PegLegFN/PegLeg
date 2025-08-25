using Godot;
using System;
using System.Reflection;
using System.Text.Json.Nodes;

public partial class ConfigSwitchHook : SwitchController
{
    [Export]
    string section;

    [Export]
    string key;

    bool valueIsChanging;

    protected override void Initialise(int defaultIndex)
    {
        AppConfig.OnConfigChanged += OnConfigChanged;
        int storedIndex = AppConfig.Get(section, key, defaultIndex);
        valueIsChanging = true;
        base.Initialise(storedIndex);
        valueIsChanging = false;
        if (CurrentIndex != storedIndex)
            AppConfig.Set(section, key, CurrentIndex);
    }

    private void OnConfigChanged(string section, string key, JsonValue val)
    {
        if(this.section==section && this.key == key)
        {
            valueIsChanging = true;
            SetIndex(val.GetValue<int>());
            valueIsChanging = false;
        }
    }

    protected override void UpdateIndex(bool val, int index)
    {
        if (!val)
            return;
        AppConfig.Set(section, key, index);
        base.UpdateIndex(val, index);
    }
}
