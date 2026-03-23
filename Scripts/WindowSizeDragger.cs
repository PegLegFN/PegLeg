using Godot;

public partial class WindowSizeDragger : Control
{
	bool isDragging;
	Vector2I windowSizeStart;
	Vector2I windowPosStart;
	Vector2I mouseStart;
	[Export]
	GrabberType grabberType;
	[Export]
	StatusIndicator statusIndicator;

	public static readonly Vector2I limitSize = new(1000, 500);
	const double minAspectRatio = 4.0 / 3;
	const double maxAspectRatio = 2;

	public enum GrabberType
	{
		Top = 0,
		Left = 1,
		Bottom = 2,
		Right = 3
	}

	public override void _GuiInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton mouseEvent)
		{
			if (mouseEvent.ButtonIndex == MouseButton.Left)
			{
				isDragging = !isDragging;
				var window = GetWindow();
				windowPosStart = window.Position;
				windowSizeStart = window.Size;
				mouseStart = DisplayServer.MouseGetPosition();
			}
		}
	}

	public override void _Process(double delta)
	{
		if (isDragging)
		{
			bool attachToWindow = ((int)grabberType) < 2;
			Vector2I axisScale = ((int)grabberType) % 2 == 0 ? Vector2I.Down : Vector2I.Right;
			var window = GetWindow();

			Vector2I mouseDiff = (DisplayServer.MouseGetPosition() - mouseStart) * axisScale;


			Vector2I newSize = windowSizeStart + (mouseDiff * (attachToWindow ? -1 : 1));

			newSize =
				new(
					Mathf.Max(newSize.X, limitSize.X),
					Mathf.Max(newSize.Y, limitSize.Y)
				);

			window.Size = newSize;
			mouseDiff = (newSize - windowSizeStart) * (attachToWindow ? -1 : 1);

			if (attachToWindow)
				window.Position = windowPosStart + mouseDiff;
		}
	}
}
