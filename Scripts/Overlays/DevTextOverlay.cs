using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using static System.Net.Mime.MediaTypeNames;

public partial class DevTextOverlay : ModalWindow
{
    [Export]
    TextEdit textEdit;
    [Export]
    TabBar tabBar;
    static DevTextOverlay inst;

    public override void _Ready()
    {
        base._Ready();
        tabBar.TabSelected += TabSelected;
        inst = this;
    }

    string[][] tabContents;
    private void TabSelected(long tab)
    {
        inst.textEdit.Text = tabContents[tab][1];
    }

    public static void ShowText(string text)
    {
        if (inst?.IsInsideTree() != true)
            return;
        inst.textEdit.Text = text;
        inst.tabBar.Visible = false;
        inst.SetWindowOpen(true);
    }

    public static void ShowTabs(string[][] contents)
    {
        if (inst?.IsInsideTree() != true)
            return;
        if (contents.Length == 0)
            return;
        if (contents.Length == 1)
        {
            ShowText(contents[0][1]);
            return;
        }
        inst.tabContents = contents;
        inst.tabBar.Visible = true;
        inst.tabBar.ClearTabs();
        foreach (var item in contents)
        {
            inst.tabBar.AddTab(item[0]);
        }
        inst.SetWindowOpen(true);
    }
}
