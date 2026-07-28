using Godot;
using System;

public partial class ClickbaitTransform : Control
{
	[Export]
	float rotationOffset;
	[Export]
	float rotationSpeed;
	[Export]
	float heightOffset;
	[Export]
	float heightSpeed;

	float startRot;
	float endRot;
	public override void _Ready()
	{
		startRot = RotationDegrees;
		endRot = RotationDegrees + rotationOffset;
	}

	double time = 0;
	public override void _Process(double delta)
	{
		if (!IsVisibleInTree())
			return;
		time += delta;
		if (rotationOffset != 0)
			RotationDegrees = Mathf.Lerp(startRot, endRot, ConvertTime(time, rotationSpeed));
		if (heightOffset != 0)
			OffsetTransformPosition = new(0, heightOffset * ConvertTime(time, heightSpeed));
	}
	static float ConvertTime(double time, float speed) => (Mathf.Sin((float)time * speed) * 0.5f) + 0.5f;
}
