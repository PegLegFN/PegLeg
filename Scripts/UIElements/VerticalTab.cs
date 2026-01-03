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
	}

    public override void _EnterTree()
    {
        triggerButton?.SafeConnect(Button.SignalName.Pressed, Callable.From(PressResponse));
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
            pageNode.SafeDisconnect(SignalName.Renamed, Callable.From(UpdatePageName));
            pageNode.SafeDisconnect(SignalName.VisibilityChanged, Callable.From(PressResponse));
        }
        pageNode = newPageNode;
        UpdatePageName();
        SetState(triggerButton?.ButtonPressed ?? false);
        if (OS.HasFeature("editor_hint") && pageNode is not null)
        {
            pageNode.SafeConnect(SignalName.Renamed, Callable.From(UpdatePageName));
            pageNode.SafeConnect(SignalName.VisibilityChanged, Callable.From(PressResponse));
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
		if(pageNode is not null && pageNode.IsInsideTree())
        {
            visibilityLocked = true;
            pageNode.Visible = pressed;
            visibilityLocked = false;
        }
    }

    public override void _ExitTree()
    {
        if (OS.HasFeature("editor_hint") && pageNode is not null && pageNode.IsInsideTree())
        {
            pageNode.SafeDisconnect(SignalName.Renamed, Callable.From(UpdatePageName));
            pageNode.SafeDisconnect(SignalName.VisibilityChanged, Callable.From(PressResponse));
        }
        triggerButton?.SafeDisconnect(Button.SignalName.Pressed, Callable.From(PressResponse));
    }
}
