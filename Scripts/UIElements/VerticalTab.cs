using Godot;
using System;
using System.Linq;

[Tool]
public partial class VerticalTab : Control
{
	[Export]
	int margin = 10;
	[Export]
	Button triggerButton;
    [Export]
    MarginContainer marginContainer;

	public override void _Ready()
    {
        if (!OS.HasFeature("editor_hint"))
        {
            triggerButton ??= FindChildren("*", "Button").FirstOrDefault() is Button b ? b : null;
            marginContainer ??= FindChildren("*", "MarginContainer").FirstOrDefault() is MarginContainer m ? m : null;
        }
        if (triggerButton is not null)
            triggerButton.Pressed += PressResponse;
	}

	VerticalTabContainer tabContainer;
	int tabIndex;
	public void SetupTab(VerticalTabContainer newTabContainer, int newIndex)
	{
		tabContainer = newTabContainer;
		tabIndex = newIndex;
	}
    bool visibilityLocked = false;
    private void PressResponse()
    {
        if (!visibilityLocked)
            tabContainer?.SetTabState(tabIndex);
    }

    Control pageNode;
    public Control Page => pageNode;
    public void SetPage(Control newPageNode)
	{
        if (OS.HasFeature("editor_hint") && pageNode is not null)
        {
            pageNode.Renamed -= UpdatePageName;
            pageNode.VisibilityChanged -= PressResponse;
        }
        pageNode = newPageNode;
        UpdatePageName();
        SetState(triggerButton?.ButtonPressed ?? false);
        if (OS.HasFeature("editor_hint") && pageNode is not null)
        {
            pageNode.Renamed += UpdatePageName;
            pageNode.VisibilityChanged += PressResponse;
        }
    }

    private void UpdatePageName()
    {
        if (triggerButton is not null)
            triggerButton.Text = pageNode?.Name ?? "";
        Visible = pageNode?.IsInGroup("HideTab") == false;
        Name = (pageNode?.Name ?? "Blank")+"Tab";
    }

    public void SetState(bool pressed)
    {
        if (marginContainer is not null)
        {
            marginContainer.AddThemeConstantOverride("margin_left", pressed ? 0 : margin);
            marginContainer.AddThemeConstantOverride("margin_right", pressed ? margin : 0);
        }
		if(triggerButton is not null)
			triggerButton.ButtonPressed = pressed;
		if(pageNode is not null)
        {
            visibilityLocked = true;
            pageNode.Visible = pressed;
            visibilityLocked = false;
        }
    }

    public override void _ExitTree()
    {
        if (OS.HasFeature("editor_hint") && pageNode is not null)
        {
            pageNode.Renamed -= UpdatePageName;
            pageNode.VisibilityChanged -= PressResponse;
        }
        if (triggerButton is not null)
            triggerButton.Pressed -= PressResponse;
    }
}
