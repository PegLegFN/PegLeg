using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

public partial class VirtualTabBar : Control
{
	public struct TabData
	{
		public string text;
		public string tooltip;
		public bool hidden;
		public bool disabled;
		public Texture2D icon;
	}

	[Signal]
	public delegate void LatestTabChangedEventHandler(int value);
	[Signal]
	public delegate void TabsChangedEventHandler();

	[Export]
	PackedScene virtualTabScene;
	[Export]
	Control virtualTabParent;
	[Export]
	Control useAsTabContainer;
	[Export]
	bool singleTabMode = true;
	[Export]
	bool preferSingleTab = true;

	List<VirtualTab> allTabs;
	List<VirtualTab> activeTabs = [];

	public int LatestTab { get; private set; } = -1;
	public int TabCount => activeTabs.Count;
	public bool AnyTabsPressed => singleTabMode || activeTabs.Any(t => t.IsPressed);
	public int[] PressedTabIndexes => [.. activeTabs.Where(t => t.IsPressed).Select(t => allTabs.IndexOf(t))];
	public VirtualTab[] PressedTabs => [.. activeTabs.Where(t => t.IsPressed)];

	public override void _Ready()
	{
		PreloadTabs();
		if (useAsTabContainer is not null && singleTabMode)
		{
			var children = useAsTabContainer.GetChildren().ToArray();
			for (int i = 0; i < children.Length; i++)
			{
				int childIndex = i;
				var child = children[i];
				if (child is Control ctrlChild)
				{
					LatestTabChanged += tabIndex => ctrlChild.Visible = childIndex == tabIndex;
					ctrlChild.Visible = childIndex == LatestTab;
				}
			}
		}
	}

	void PreloadTabs()
	{
		if (virtualTabParent is null)
			return;
		if (allTabs is not null)
			return;
		VirtualTab firstVisible = null;
		allTabs = [.. virtualTabParent?.GetChildren().OfType<VirtualTab>() ?? []];
		activeTabs ??= [];
		activeTabs.Clear();
		activeTabs.AddRange(allTabs);
		for (int i = 0; i < activeTabs.Count; i++)
		{
			var tab = activeTabs[i];
			tab.SetTabBar(this);

			if (tab.Visible)
				firstVisible ??= tab;
			else
				tab.IsPressed = false;

			if (LatestTab != -1)
			{
				if (singleTabMode)
					tab.IsPressed = false;
			}
			else if (tab.IsPressed && tab.Visible)
				LatestTab = i;
		}
		UpdateTabModes();
		if (singleTabMode && LatestTab == -1 && firstVisible is not null)
			firstVisible.IsPressed = true;
	}

	public void SetTabContents(TabData[] tabDatas)
	{
		if (virtualTabParent is null)
			return;
		PreloadTabs();
		activeTabs.Clear();
		for (int i = 0; i < tabDatas.Length; i++)
		{
			while (allTabs.Count <= i)
			{
				var newTab = virtualTabScene.Instantiate<VirtualTab>();
				virtualTabParent.AddChild(newTab);
				allTabs.Add(newTab);
				newTab.SetTabBar(this);
			}
			var tab = allTabs[i];
			activeTabs.Add(tab);
			tab.SetFromTabData(tabDatas[i]);
		}
		for (int i = tabDatas.Length; i < allTabs.Count; i++)
		{
			allTabs[i].Visible = false;
		}
		UpdateTabModes();
		SetFirstValidTabPressed();
	}

	public void UpdateTabModes()
	{
		var visibleTabs = activeTabs.Where(t => t.Visible).ToArray();
		if (visibleTabs.Length == 0)
			return;
		if (visibleTabs.Length == 1)
		{
			visibleTabs[0].SetMode(2);
			return;
		}
		for (int i = 1; i < visibleTabs.Length - 1; i++)
		{
			visibleTabs[i].SetMode(0);
		}
		visibleTabs[0].SetMode(-1);
		visibleTabs[^1].SetMode(1);
	}

	public void ClearTabs()
	{
		if (singleTabMode || !AnyTabsPressed)
			return;

		lockTabPresses = true;
		for (int i = 0; i < activeTabs.Count; i++)
		{
			activeTabs[i].IsPressed = false;
		}
		lockTabPresses = false;

		LatestTab = -1;
		EmitSignalLatestTabChanged(-1);
		EmitSignalTabsChanged();
	}

	public void PressTab(VirtualTab tab, bool newVal)
	{
		int index = activeTabs.IndexOf(tab);
		if (lockTabPresses || index == -1)
			return;
		if (singleTabMode)
			newVal = true;

		int totalPressed = activeTabs.Count(t => t.IsPressed);

		if (!newVal)
		{
			//in multi tab mode, turn off tab
			if (!singleTabMode)
			{
				if (totalPressed > 1 && preferSingleTab != Input.IsKeyPressed(Key.Shift))
				{
					SetTabPressed(index, true, true);
					return;
				}
				SetTabPressed(index, false);
			}
			//otherwise, do nothing
			return;
		}

		if (!singleTabMode)
		{
			//when holding alt in multitab mode, turn on all tabs except this
			if (Input.IsKeyPressed(Key.Alt))
			{
				SetTabPressed(index, false, true);
				return;
			}
			//otherwise, when holding shift equals prefer single tab in multitab, just turn on tab
			if (preferSingleTab == Input.IsKeyPressed(Key.Shift))
			{
				SetTabPressed(index, true);
				return;
			}
		}
		//otherwise, turn on tab and turn off others
		SetTabPressed(index, true, true);
	}

	public void SetTabHidden(VirtualTab tab, bool hidden = true)
	{
		var idx = activeTabs.IndexOf(tab);
		if (idx >= 0)
			SetTabHidden(idx, hidden);
	}

	public void SetTabHidden(int index, bool hidden = true)
	{
		activeTabs[index].Visible = !hidden;
		//update tab modes
		if (singleTabMode && activeTabs[index].IsPressed && !TabPressable(activeTabs[index]))
		{
			SetFirstValidTabPressed();
		}
		else
			activeTabs[index].IsPressed = false;
	}

	public void SetTabDisabled(VirtualTab tab, bool disabled = true)
	{
		var idx = activeTabs.IndexOf(tab);
		if (idx >= 0)
			SetTabDisabled(idx, disabled);
	}

	public void SetTabDisabled(int index, bool disabled = true)
	{
		activeTabs[index].Disabled = disabled;
		//update tab modes
		if (singleTabMode && activeTabs[index].IsPressed && !TabPressable(activeTabs[index]))
		{
			SetFirstValidTabPressed();
		}
		else
			activeTabs[index].IsPressed = false;
	}

	static bool TabPressable(VirtualTab tab) => !tab.Disabled && tab.Visible;

	public void SetFirstValidTabPressed()
	{
		var firstPressable = activeTabs.FirstOrDefault(TabPressable);
		if (firstPressable is null)
			return;
		SetTabPressed(activeTabs.IndexOf(firstPressable));
	}

	bool lockTabPresses = false;
	public void SetTabPressed(int index, bool value = true, bool invertOthers = false)
	{
		if (lockTabPresses)
			return;
		if (index < 0 || index >= activeTabs.Count)
			return;
		if (!value && singleTabMode)
			return;
		var tab = activeTabs[index];
		if (!tab.Visible)
			return;

		lockTabPresses = true;
		tab.IsPressed = value;
		if (singleTabMode || invertOthers)
		{
			for (int i = 0; i < activeTabs.Count; i++)
			{
				activeTabs[i].IsPressed = singleTabMode ? (i == index) : ((i == index) == value);
			}
		}
		lockTabPresses = false;

		if (value)
		{
			LatestTab = index;
			EmitSignalLatestTabChanged(index);
		}
		EmitSignalTabsChanged();
	}

	public bool IsTabPressed(int tabIndex)
	{
		if (tabIndex < 0 || tabIndex > activeTabs.Count)
			return false;
		var tab = activeTabs[tabIndex];
		return tab.Visible && tab.IsPressed;
	}
}
