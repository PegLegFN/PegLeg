using System;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;


class NeoConfigRoot : NeoConfigObject
{
	//could prob be automated with codegen
	protected override (string, NeoConfigObject)[] Children => 
	[
		AsChild(SubObj)
	];

	public float TestProp { get; set => NotifySet(ref field, value); }
	public SubObject SubObj { get; private set; } = new();
}

class SubObject : NeoConfigObject
{
	public float TestProp { get; set => NotifySet(ref field, value); }
}

abstract class NeoConfigObject : IJsonOnDeserialized
{
	public delegate void OnConfigChangedDelegate(string path);
	static event OnConfigChangedDelegate OnConfigChanged;
	[JsonIgnore]
	protected NeoConfigObject Parent { get; private set; }
	[JsonIgnore]
	protected string PropName { get; private set; }

	void IJsonOnDeserialized.OnDeserialized()
	{
		foreach (var (propName, childObj) in Children)
		{
			childObj.Parent = this;
			childObj.PropName = propName;
		}
	}

	public static bool PathIncludes<T>(string configPath, T configValue, [CallerArgumentExpression(nameof(configValue))] string expression = null)
	{
		if (configPath == "*")
			return true;
		if (expression.Contains("ConfigRoot.", StringComparison.OrdinalIgnoreCase))
			expression = expression.Split("ConfigRoot.")[^1];
		return configPath.StartsWith(expression, StringComparison.OrdinalIgnoreCase);
	}

	protected virtual (string, NeoConfigObject)[] Children => [];
	protected static (string, NeoConfigObject) AsChild(NeoConfigObject child, [CallerArgumentExpression(nameof(child))] string expression = null) => (expression, child);

	protected void NotifySet<T>(ref T field, T value, [CallerMemberName] string memberName = null)
	{
		field = value;
		NotifyPropChanged(memberName);
	}

	protected void NotifyPropChanged(string path)
	{
		if(Parent is not null)
		{
			path = $"{PropName}.{path}";
			Parent.NotifyPropChanged(path);
			return;
		}
		OnConfigChanged?.Invoke(path);
	}
}

