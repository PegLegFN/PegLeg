using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class TriggerHook : Node
{
	[Signal]
	public delegate void HasTriggerEventHandler(bool hasTrigger);
	[Export]
	string name;
	[Export]
	string[] defaultContext;
	public void Trigger() => TriggerInstance.Trigger(name, defaultContext);
	public void Trigger(string[] context) => TriggerInstance.Trigger(name, context);

	public override void _Ready()
	{
		UpdateTrigger(name);
		TriggerInstance.OnTriggersChanged += UpdateTrigger;
	}

	public override void _ExitTree()
	{
		TriggerInstance.OnTriggersChanged -= UpdateTrigger;
	}

	void UpdateTrigger(string updated)
	{
		if (updated == name)
			EmitSignalHasTrigger(TriggerInstance.HasTrigger(name));
	}
}

public class TriggerInstance : IDisposable
{
	public static Action<string> OnTriggersChanged;
	static Dictionary<string, List<TriggerInstance>> activeTriggers = [];

	public static bool HasTrigger(string name) => activeTriggers.TryGetValue(name, out var list) && list.Any(t => t.Enabled);
	public static void Trigger(string name, string[] context = null)
	{
		if (!activeTriggers.TryGetValue(name, out var list))
			return;
		if (list.FirstOrDefault(t => !t.AllowMultiple && t.Enabled) is { } first)
			first.TriggerInst(context);
		foreach (var inst in list.Where(t => t.AllowMultiple && t.Enabled))
		{
			inst.TriggerInst(context);
		}
	}

	static bool changeNotifQueued = false;
	static HashSet<string> changeQueue = [];
	static async void NotifyTriggerChange(string name)
	{
		changeQueue.Add(name);
		if (changeNotifQueued)
			return;
		changeNotifQueued = true;
		await Helpers.WaitForFrame();
		foreach (var queued in changeQueue)
		{
			OnTriggersChanged?.Invoke(queued);
		}
		changeNotifQueued = false;
	}

	public string Name { get; private init; }
	public string DisplayName { get; private init; }
	bool AllowMultiple { get; init; }

	public bool Enabled
	{
		get => field;
		set
		{
			if (field == value)
				return;
			field = value;
			NotifyTriggerChange(Name);
		}
	}

	public TriggerInstance(string name, bool allowMultiple = false)
	{
		Name = name;
		AllowMultiple = allowMultiple;
		if (!activeTriggers.TryGetValue(Name, out var list))
			activeTriggers.Add(name, list = []);
		list.Add(this);
		Enabled = true;
	}

	public TriggerInstance(string name, Action trigger) : this(name, false)
	{
		OnTriggered += trigger;
	}

	public TriggerInstance(string name, Action<string[]> trigger) : this(name, false)
	{
		OnTriggeredCtx += trigger;
	}

	public void BindNode(Node node, Action trigger = null)
	{
		node.TreeExiting += Dispose;
		OnTriggered += trigger;
	}

	public void BindNode(Node node, Action<string[]> trigger)
	{
		node.TreeExiting += Dispose;
		OnTriggeredCtx += trigger;
	}

	public void Dispose()
	{
		if (!activeTriggers.TryGetValue(Name, out var list))
			return;
		list.Remove(this);
		NotifyTriggerChange(Name);
	}

	void TriggerInst(string[] context)
	{
		OnTriggered?.Invoke();
		OnTriggeredCtx?.Invoke(context);
	}

	public event Action OnTriggered;
	public event Action<string[]> OnTriggeredCtx;
}