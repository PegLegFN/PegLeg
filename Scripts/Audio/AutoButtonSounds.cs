using Godot;
using System;

public partial class AutoButtonSounds : Node
{
	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
        RecursiveConnect(GetTree().Root);
        GetTree().NodeAdded += ConnectButtonSounds;
	}

    public override void _ExitTree()
    {
        GetTree().NodeAdded -= ConnectButtonSounds;
    }

    static void ConnectButtonSounds(Node node) =>
        TryConnectButtonSounds(node);

    static bool TryConnectButtonSounds(Node node)
    {
        if(!node.IsInGroup("ExcludeButtonSounds"))
        {
            bool useHoverSounds = !node.IsInGroup("ExcludeHoverSounds");
            bool usePressSounds = !node.IsInGroup("ExcludePressSounds");
            string customHoverSound = node.HasMeta("OverrideHover") ? (string)node.GetMeta("OverrideHover") : null;
            string customPressSound = node.HasMeta("OverridePress") ? (string)node.GetMeta("OverridePress") : null;
            if (node is BaseButton buttonNode)
            {
                if (useHoverSounds)
                    buttonNode.MouseEntered += () =>
                    {
                        if (!buttonNode.Disabled)
                            UISounds.PlaySound(customHoverSound ?? "Hover");
                    };
                if (usePressSounds)
                    buttonNode.Pressed += () => UISounds.PlaySound(customPressSound ?? "ButtonPress");
                return true;
            }
            if (node is TabContainer tabContainer)
            {
                if (useHoverSounds)
                    tabContainer.TabHovered += i =>
                    {
                        if (!tabContainer.IsTabDisabled((int)i))
                            UISounds.PlaySound(customHoverSound ?? "Hover");
                    };
                if (usePressSounds)
                    tabContainer.TabClicked += i => UISounds.PlaySound(customPressSound ?? "Hover");
                return false;
            }
            if (node is TabBar tabBar)
            {
                if (useHoverSounds)
                    tabBar.TabHovered += i =>
                    {
                        if (!tabBar.IsTabDisabled((int)i))
                            UISounds.PlaySound(customHoverSound ?? "Hover");
                    };
                if (usePressSounds)
                    tabBar.TabClicked += i => UISounds.PlaySound(customPressSound ?? "Hover");
                return true;
            }
        }
        return false;
    }

    static void RecursiveConnect(Node parent)
    {
        foreach (var child in parent.GetChildren())
        {
            if (!TryConnectButtonSounds(child))
                RecursiveConnect(child);
        }
    }
}
