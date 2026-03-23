using Godot;
using System;
using System.Text.Json.Nodes;

public partial class ConfigFloatSwitchHook : SwitchController
{
	[Export]
	string section;

	[Export]
	string key;

	[Export]
	float[] values;

	bool valueIsChanging;

	protected override void Initialise(int defaultIndex)
	{
		if ((values?.Length ?? 0) == 0)
			values = [0];
		AppConfig.OnConfigChanged += OnConfigChanged;
		float? storedVal = AppConfig.Get<float?>(section, key);
		int storedIndex = 0;
		bool isValid = true;
		if (storedVal is not null)
		{
			var idx = Array.IndexOf(values, storedVal.Value);
			//GD.Print($"Stored Idx: {idx} ({storedVal.Value}), [{string.Join(", ", values)}]");
			if (idx == -1)
				isValid = false;
			storedIndex = idx == -1 ? (defaultIndex == -1 ? 0 : defaultIndex) : idx;
		}
		valueIsChanging = true;
		//GD.Print("Setting index " + storedIndex);
		base.Initialise(storedIndex);
		valueIsChanging = false;
		if (!isValid)
			AppConfig.Set(section, key, values[storedIndex]);
	}

	private void OnConfigChanged(string section, string key, JsonValue val)
	{
		if (this.section == section && this.key == key)
		{
			valueIsChanging = true;
			var idx = Array.IndexOf(values, val.TryGetValue(out float floatVal) ? floatVal : -999);
			if (idx == -1)
				idx = 0;
			SetIndex(idx);
			valueIsChanging = false;
		}
	}

	protected override void UpdateIndex(bool val, int index)
	{
		if (!val)
			return;
		if (!valueIsChanging)
			AppConfig.Set(section, key, values[index]);
		base.UpdateIndex(val, index);
	}
}
