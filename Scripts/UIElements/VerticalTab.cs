using Godot;
using System.Linq;

[Tool]
public partial class VerticalTab : Control
{
	[Signal]
	public delegate void IsUpdateEventHandler(bool value);
	[Export]
	int margin = 10;
	[Export]
	Button triggerButton;
	[Export]
	MarginContainer marginContainer;

	public override void _Ready()
	{
		if (!Engine.IsEditorHint())
		{
			triggerButton ??= FindChildren("*", "Button").FirstOrDefault() is Button b ? b : null;
			marginContainer ??= FindChildren("*", "MarginContainer").FirstOrDefault() is MarginContainer m ? m : null;
		}
	}

	public override void _EnterTree()
	{
		//if (triggerButton is not null)
		//    triggerButton.Pressed += PressResponse;
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
		if (Engine.IsEditorHint() && pageNode is not null)
		{

			//Renamed -= UpdatePageName;
			//VisibilityChanged -= PressResponse;
			pageNode.SafeDisconnect(SignalName.Renamed, Callable.From(UpdatePageName));
			pageNode.SafeDisconnect(SignalName.VisibilityChanged, Callable.From(PressResponse));
		}
		pageNode = newPageNode;
		UpdatePageName();
		SetState(triggerButton?.ButtonPressed ?? false);
		if (Engine.IsEditorHint() && pageNode is not null)
		{
			//Renamed += UpdatePageName;
			//VisibilityChanged += PressResponse;
			pageNode.SafeConnect(SignalName.Renamed, Callable.From(UpdatePageName));
			pageNode.SafeConnect(SignalName.VisibilityChanged, Callable.From(PressResponse));
		}
	}

	private void UpdatePageName()
	{
		triggerButton?.Text = pageNode?.Name ?? "";
		EmitSignalIsUpdate(triggerButton.Text == "Updates");
		Visible = pageNode?.IsInGroup("HideTab") == false;
		Name = (pageNode?.Name ?? "Blank") + "Tab";
	}

	public void SetState(bool pressed)
	{
		if (marginContainer is not null)
		{
			marginContainer.AddThemeConstantOverride("margin_left", pressed ? 0 : margin);
			marginContainer.AddThemeConstantOverride("margin_right", pressed ? margin : 0);
		}
		triggerButton?.ButtonPressed = pressed;
		if (pageNode is not null && pageNode.IsInsideTree())
		{
			visibilityLocked = true;
			pageNode.Visible = pressed;
			visibilityLocked = false;
		}
	}

	public override void _ExitTree()
	{
		if (Engine.IsEditorHint() && pageNode is not null && pageNode.IsInsideTree())
		{
			//Renamed -= UpdatePageName;
			//VisibilityChanged -= PressResponse;
			pageNode.SafeDisconnect(SignalName.Renamed, Callable.From(UpdatePageName));
			pageNode.SafeDisconnect(SignalName.VisibilityChanged, Callable.From(PressResponse));
		}
		//if (triggerButton is not null)
		//    triggerButton.Pressed -= PressResponse;
		if (triggerButton?.IsInsideTree() == true)
			triggerButton.SafeDisconnect(Button.SignalName.Pressed, Callable.From(PressResponse));
	}
}
