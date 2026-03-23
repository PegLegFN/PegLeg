using Godot;
using System;

public partial class TooltipFixer : Node
{
	[Export]
	CustomTooltip tooltipControl;

	public override async void _Ready()
	{
		tooltipControl.Scale = Vector2.Zero;
		await Helpers.WaitForFrame();
		if (tooltipControl?.GetParent() is Node parent)
			parent.RemoveChild(tooltipControl);
		tooltipControl.Scale = Vector2.One;
		tooltipControl.Visible = true;
		GetTree().NodeAdded += OnNodeAdded;
		inTree = true;
	}

	public override void _ExitTree()
	{
		GetTree().NodeAdded -= OnNodeAdded;
		inTree = false;
	}

	bool inTree = false;

	private void OnNodeAdded(Node node)
	{
		if (node is not PopupPanel pp)
			return;
		pp.Transparent = true;
		pp.PopupWindow = false;

		if (pp.ThemeTypeVariation != "TooltipPanel")
			return;

		//GD.Print("hello tooltip");

		var label = pp.GetChild<Label>(0);
		pp.RemoveChild(label);
		label.QueueFree();

		var panel = pp.GetChild<Panel>(0, true);
		panel.Visible = false;

		pp.AddChild(tooltipControl);
		tooltipControl.SetTooltip(label.Text);
		pp.TreeExiting += () =>
		{
			if (inTree)
				pp.RemoveChild(tooltipControl);
		};

		ResetCSF(pp);
	}

	private async void ResetCSF(PopupPanel pp)
	{
		tooltipControl.Visible = false;
		await Helpers.WaitForFrame();
		try
		{
			pp.ContentScaleFactor = 1f;
			pp.Size = Vector2I.Zero;
			//tooltip offset is applied here
			tooltipControl.Visible = true;
		}
		catch (ObjectDisposedException) { }
	}
}
