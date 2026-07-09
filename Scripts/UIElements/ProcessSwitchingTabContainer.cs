using Godot;
using System.Text.Json.Nodes;
using System.Threading;

public partial class ProcessSwitchingTabContainer : TabContainer
{
	[Export]
	Node altParent;
	Node defaultParent;
	[Export]
	bool hideInDevMode;
	[Export]
	int defaultTab;

	public override void _Ready()
	{
		TabChanged += UpdateProcessingTab;
		foreach (var child in GetChildren())
		{
			child.ProcessMode = ProcessModeEnum.Disabled;
		}
		UpdateProcessingTab(CurrentTab);
		if (altParent is not null)
		{
			defaultParent = GetParent();
			UpdateDevState();
		}
		if (!OS.HasFeature("editor") && defaultTab >= 0)
			CurrentTab = defaultTab;
	}

	private void ConfigChange(string section, string key, JsonNode value)
	{
		if (section != "advanced" && key != "developer")
			return;
		UpdateDevState();
	}

	SemaphoreSlim stateQueue = new(1);
	async void UpdateDevState()
	{
		await stateQueue.WaitAsync();
		try
		{
			bool isAltParented = (GetParent() == altParent);
			bool shouldBeAltParented = AppConfig.Get("advanced", "developer", false) == hideInDevMode;
			GD.Print($"alt: {shouldBeAltParented}");
			if (isAltParented == shouldBeAltParented)
				return;
			if (GetParent() is null)
				return;
			if (shouldBeAltParented)
			{
				defaultParent.RemoveChild(this);
				await Helpers.WaitForFrame();
				altParent.AddChild(this);
			}
			else
			{
				altParent.RemoveChild(this);
				await Helpers.WaitForFrame();
				defaultParent.AddChild(this);
			}
		}
		finally
		{
			stateQueue.Release();
		}
	}

	public override void _EnterTree()
	{
		if (altParent is not null)
		{
			AppConfig.OnConfigChanged += ConfigChange;
		}
	}

	public override void _ExitTree()
	{
		AppConfig.OnConfigChanged -= ConfigChange;
	}

	Node activeTab = null;
	private void UpdateProcessingTab(long tab)
	{
		var tabChild = GetChild((int)tab);
		if (activeTab is not null)
			activeTab.ProcessMode = ProcessModeEnum.Disabled;
		activeTab = tabChild;
		activeTab.ProcessMode = ProcessModeEnum.Inherit;
	}
}
