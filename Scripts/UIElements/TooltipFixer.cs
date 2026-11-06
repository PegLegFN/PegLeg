using Godot;
using System;

public partial class TooltipFixer : Node
{
	[Export]
	CustomTooltip tooltipControl;

	public override async void _Ready()
	{
		tooltipControl.Scale = Vector2.Zero;
		await Helpers.WaitForFrames(3);
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

		if (pp.ThemeTypeVariation != "TooltipPanel" || !inTree)
			return;

		//GD.Print("hello tooltip");

		var label = pp.GetChild<Label>(0);
		pp.RemoveChild(label);
		label.QueueFree();

		var panel = pp.GetChild<Panel>(0, true);
		panel.Visible = false;

		ResetCSF(pp);

		pp.AddChild(tooltipControl);
		tooltipControl.SetTooltip(label.Text);
		pp.TreeExiting += () =>
		{
			if (inTree)
				pp.RemoveChild(tooltipControl);
		};
	}

	private async void ResetCSF(PopupPanel pp)
	{
		tooltipControl.Visible = false;
		await Helpers.WaitForFrame();
		try
		{
			var oldCSF = pp.ContentScaleFactor;
			pp.ContentScaleFactor = 1f;
			var oldSize = pp.Size;
			pp.Size = (Vector2I)((Vector2)pp.Size/oldCSF);
			var diff = pp.Size - oldSize;
			var mousePos = DisplayServer.MouseGetPosition();
			if (pp.Position.X < mousePos.X)
				pp.Position -= diff * Vector2I.Right;
			if (pp.Position.Y < mousePos.Y)
				pp.Position -= diff * Vector2I.Down;
			tooltipControl.Visible = true;
		}
		catch (ObjectDisposedException) { }
	}
}
