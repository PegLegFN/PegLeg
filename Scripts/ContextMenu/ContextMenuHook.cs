using Godot;

public partial class ContextMenuHook : Node
{
	[Signal]
	public delegate void ContextMenuTriggeredEventHandler(ContextMenuHook test);
	[Export]
	public MissionEntry missionSource;
	[Export]
	public GameItemEntry itemSource;
	[Export]
	public GameOfferEntry offerSource;
	[Export]
	public CosmeticShopOfferEntry cosmeticSource;
	[Export]
	public Control attachTo;
	[Export]
	public bool attachHorizontally;
	[Export]
	public ContextComponentList componentList;

	const float mobileHoldTime = 1.0f;
	static ContextMenuHook currentHover;
	bool hover;
	bool mobile;
	Timer holdTimer;

	public override void _Ready()
	{
		mobile = OS.HasFeature("mobile");
		if (mobile)
		{
			holdTimer = new();
			holdTimer.Autostart = false;
			holdTimer.WaitTime = mobileHoldTime;
			holdTimer.OneShot = true;
			holdTimer.Timeout += Trigger;
			AddChild(holdTimer);
		}
		var parent = GetParent();
		if (parent is Control ctrlParent)
		{
			ctrlParent.MouseEntered += () =>
			{
				currentHover = this;
				rClickWasPressed = false;
				hover = true;

				if (mobile && mouseLastPressed == Time.GetTicksMsec())
					holdTimer.Start();
			};
			ctrlParent.MouseExited += () =>
			{
				hover = false;
				rClickWasPressed = false;
				halfTriggered = false;
				if (mobile)
					holdTimer.Stop();
			};
		}
	}

	bool rClickWasPressed;
	bool halfTriggered;
	ulong mouseLastPressed = 0;
	public override void _Input(InputEvent @event)
	{
		if (@event is InputEventMouseButton mbEvent)
		{
			if (mobile)
			{
				var pressed = mbEvent.ButtonMask.HasFlag(MouseButtonMask.Left);
				if (pressed)
					mouseLastPressed = Time.GetTicksMsec();
				if (hover && pressed)
					holdTimer.Start();
				else
					holdTimer.Stop();
				return;
			}

			bool rClickPressed = mbEvent.ButtonMask.HasFlag(MouseButtonMask.Right);
			bool rClickJustPressed = rClickPressed && !rClickWasPressed;
			bool rClickJustReleased = !rClickPressed && rClickWasPressed;
			rClickWasPressed = rClickPressed;
			if (hover && !halfTriggered && rClickJustPressed)
			{
				halfTriggered = true;
				return;
			}
			if (halfTriggered && rClickJustReleased)
			{
				halfTriggered = false;
				Trigger();
			}
		}
		//todo: on android, detect hold press, then show tooltip with button to expand context menu options
	}

	public static void TriggerHovered()
	{
		currentHover?.Trigger();
	}

	void Trigger()
	{
		if (mobile)
		{
			Input.WarpMouse(Vector2.Zero);
			holdTimer.Stop();
		}
		if (missionSource is not null && missionSource.currentMission is null)
			return;
		if (itemSource is not null && itemSource.currentItem is null)
			return;
		if (offerSource is not null && offerSource.currentOffer is null)
			return;
		//GD.Print($"Triggering {Name} ({componentList?.ResourceName.Split("/")[^1]})");
		if ((componentList?.components?.Length ?? 0) == 0)
			EmitSignalContextMenuTriggered(this);
		else
			ContextMenu.ShowMenu(this);
	}
}
