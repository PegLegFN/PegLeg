using Godot;
using System;

public partial class UpdateNotificationHook : Control
{
	static event Action ShowNotif;
	static bool markedVisible = false;


	public static void SetNotifVisible()
	{
		markedVisible = true;
		ShowNotif?.Invoke();
	}

	public override void _Ready()
	{
		UpdateVisible();
		ShowNotif += UpdateVisible;
	}

	public override void _ExitTree()
	{
		ShowNotif -= UpdateVisible;
	}

	void UpdateVisible() => Visible = markedVisible;
}
