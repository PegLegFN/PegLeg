using Godot;
using System.Collections.Generic;
using System.Linq;

[Tool]
public partial class VerticalTabContainer : Node
{
	//automatically creates tabs for page nodes, and maintains state in the editor
	[Export]
	PackedScene tabScene;
	[Export]
	Control tabParent;
	[Export]
	Control pageParent;

	VerticalTab[] tabs;

	public override void _Ready()
	{
	}

	public override void _EnterTree()
	{
		lockTabs = false;
		GenerateTabs();
		if (Engine.IsEditorHint())
		{
			//ChildEnteredTree += PageAdded;
			//ChildOrderChanged += RefreshTabs;
			//ChildExitingTree += PageRemoved;
			pageParent.SafeConnect(SignalName.ChildEnteredTree, Callable.From<Node>(PageAdded));
			pageParent.SafeConnect(SignalName.ChildOrderChanged, Callable.From(RefreshTabs));
			pageParent.SafeConnect(SignalName.ChildExitingTree, Callable.From<Node>(PageRemoved));
		}
	}

	public async void RefreshTabs()
	{
		await Helpers.WaitForFrame();
		GenerateTabs();
	}

	private void PageAdded(Node node) => GenerateTabs();
	private void PageRemoved(Node node) => GenerateTabs(node);

	bool lockTabs = false;
	private void GenerateTabs(Node without = null)
	{
		if (tabScene is null || tabParent is null || pageParent is null || lockTabs)
			return;
		lockTabs = true;
		Control[] pages = [..
			pageParent
			.GetChildren()
			.Where(n => n != without)
			.OfType<Control>()
		];
		List<VerticalTab> newTabs = [.. tabParent.GetChildren().OfType<VerticalTab>()];
		for (int i = 0; i < pages.Length; i++)
		{
			if (i <= newTabs.Count)
			{
				var inst = tabScene.Instantiate<VerticalTab>();
				newTabs.Add(inst);
				tabParent.AddChild(inst);
			}
			newTabs[i].SetupTab(this, i);
			newTabs[i].SetPage(pages[i]);
		}
		for (int i = pages.Length; i < newTabs.Count; i++)
		{
			newTabs[i].SetPage(null);
			newTabs[i].QueueFree();
		}
		newTabs.RemoveRange(pages.Length, newTabs.Count - pages.Length);
		tabs = [.. newTabs];

		if (OS.HasFeature("editor"))
			SetTabState(Mathf.Max(0, newTabs.IndexOf(newTabs.FirstOrDefault(t => t.Page?.Visible == true))));
		else
			SetTabState(0);
		lockTabs = false;
	}

	public void SetTabState(int selectedTab)
	{
		for (int i = 0; i < tabs.Length; i++)
		{
			tabs[i].SetState(i == selectedTab);
		}
	}

	public override void _ExitTree()
	{
		lockTabs = true;
		if (Engine.IsEditorHint() && pageParent?.IsInsideTree() == true)
		{
			//ChildEnteredTree -= PageAdded;
			//ChildOrderChanged -= RefreshTabs;
			//ChildExitingTree -= PageRemoved;
			pageParent.SafeDisconnect(SignalName.ChildEnteredTree, Callable.From<Node>(PageAdded));
			pageParent.SafeDisconnect(SignalName.ChildOrderChanged, Callable.From(RefreshTabs));
			pageParent.SafeDisconnect(SignalName.ChildExitingTree, Callable.From<Node>(PageRemoved));
		}
	}
}
