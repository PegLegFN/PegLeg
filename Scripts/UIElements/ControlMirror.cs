using Godot;

[Tool]
public partial class ControlMirror : Control
{
	[Export]
	Control basis;
	public override async void _Ready()
	{
		await Helpers.WaitForFrame();
		if (basis is null)
			return;
		//ItemRectChanged += UpdatePosAndSize;
		basis.SafeConnect(SignalName.ItemRectChanged, Callable.From(UpdatePosAndSize));
		UpdatePosAndSize();
	}

	public override void _ExitTree()
	{
		//ItemRectChanged -= UpdatePosAndSize;
		basis.SafeDisconnect(SignalName.ItemRectChanged, Callable.From(UpdatePosAndSize));
	}

	private void UpdatePosAndSize()
	{
		GlobalPosition = basis.GlobalPosition;
		Size = basis.Size;
	}
}
