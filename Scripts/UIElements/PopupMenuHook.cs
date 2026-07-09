using Godot;
using System;

public partial class PopupMenuHook : Node
{
	[Export]
	PopupMenu menu;
	[Export]
	Control hookPoint;
	[Export]
	Control hookEndPoint;

	public void OpenMenuOnHook()
	{
		hookEndPoint ??= hookPoint;
		var startPos = (Vector2I)(GetWindow().GetFinalTransform() * hookPoint.GetGlobalTransformWithCanvas()).Origin;
		var endPos = (Vector2I)(GetWindow().GetFinalTransform() * hookEndPoint.GetGlobalTransformWithCanvas()).Origin;
		menu.PopupOnParent(new(startPos, endPos-startPos));
	}
}
