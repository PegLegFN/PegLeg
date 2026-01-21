using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class VirtualTabBar : Control
{
    public struct TabData
    {
        public string text;
        public string tooltip;
        public bool hidden;
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
    bool singleTabMode = true;

    List<VirtualTab> allTabs;
    List<VirtualTab> activeTabs = [];

    public int LatestTab { get; private set; } = -1;
    public bool AnyTabsPressed => singleTabMode || activeTabs.Any(t => t.IsPressed);

    public override void _Ready()
    {
        PreloadTabs();
    }

    void PreloadTabs()
    {
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
            tab.SetContent(tabDatas[i].text, tabDatas[i].icon, tabDatas[i].tooltip);
            tab.Visible = !tabDatas[i].hidden;
        }
        for (int i = tabDatas.Length; i < allTabs.Count; i++)
        {
            allTabs[i].Visible = false;
        }
        UpdateTabModes();
        SetTabPressed(0);
    }

    public void UpdateTabModes()
    {
        if (activeTabs.Count == 1)
        {
            activeTabs[0].SetMode(2);
        }
        for (int i = 0; i < activeTabs.Count; i++)
        {
            activeTabs[i].SetMode(0);
        }
        activeTabs.FirstOrDefault(t => t.Visible)?.SetMode(-1);
        activeTabs.LastOrDefault(t => t.Visible)?.SetMode(1);
    }

    public void PressTab(VirtualTab tab, bool newVal)
    {
        int index = activeTabs.IndexOf(tab);
        if (lockTabPresses || index == -1)
            return;
        if (singleTabMode)
            newVal = true;

        if (!singleTabMode && Input.IsKeyPressed(Key.Alt) && activeTabs.Where(t => t.IsPressed).Count() <= 1)
            GD.Print("bleh");
        else
            SetTabPressed(index, newVal);
    }

    public void SetTabHidden(int index, bool value = true)
    {
        activeTabs[index].Visible = value;
        //update tab modes
        if (singleTabMode && activeTabs[index].IsPressed && !value)
        {
            var firstVisible = activeTabs.FirstOrDefault(t => t.Visible);
            if (firstVisible is null)
            {
                activeTabs[index].Visible = true;
                return;
            }
            var firstIdx = activeTabs.IndexOf(firstVisible);
            SetTabPressed(firstIdx);
        }
        else
            activeTabs[index].IsPressed = false;
    }

    bool lockTabPresses = false;
    public void SetTabPressed(int index, bool value = true)
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
        if (singleTabMode)
        {
            for (int i = 0; i < activeTabs.Count; i++)
            {
                activeTabs[i].IsPressed = i == index;
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
        if(tabIndex<0 || tabIndex>activeTabs.Count)
            return false;
        var tab = activeTabs[tabIndex];
        return tab.Visible && tab.IsPressed;
    }
}
