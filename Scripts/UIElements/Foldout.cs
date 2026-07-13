using Godot;
using System;
using System.Linq;

public partial class Foldout : Control
{
	[Signal]
	public delegate void NameChangedEventHandler(string name);
	[Signal]
	public delegate void NotificationVisibleEventHandler(bool visible);

	[Export]
	public Control elementContainer { get; private set; }
	[Export]
	Control foldoutInteractionPanel;
	[Export]
	Control foldoutTarget;
	[Export]
	Control foldoutChildParent;
	[Export]
	Control rotationTarget;
	[Export]
	float openRotation;
	[Export]
	float closedRotation;
	[Export]
	float extraSpace = 4;
	[Export]
	float foldoutTime = 0.25f;
	[Export]
	bool hideWhenNoChildren = true;

	public override void _Ready()
	{
		elementContainer.ItemRectChanged += RefreshExpandedHeight;
		ItemRectChanged += RefreshExpandedHeight;
		rotationTarget.Rotation = closedRotation;
		foldoutTarget.CustomMinimumSize = Vector2.Down * foldoutInteractionPanel.Size.Y;
		Size = Vector2.Zero;
		RefreshExpandedHeight();
	}
	float totalSize = 0;
	void RefreshExpandedHeight()
	{
		float newSize = foldoutInteractionPanel.Size.Y + extraSpace + elementContainer.Size.Y;
		bool hasChanged = newSize != totalSize;
		totalSize = newSize;
		if (currentFoldoutState && !(foldoutTween?.IsRunning() ?? false) && hasChanged)
			foldoutTarget.CustomMinimumSize = Vector2.Down * newSize;

		var childCount = VisibleChildren;
		if (hideWhenNoChildren)
			Visible = childCount != 0;

		if (currentFoldoutState && childCount == 0)
			SetFoldoutState(false, childCount);
	}

	int VisibleChildren => foldoutChildParent.GetChildren().Count(n => n is Control c && c.Visible);

	Tween foldoutTween = null;
	public void ToggleFoldoutState() =>
		SetFoldoutState(!currentFoldoutState);

	bool currentFoldoutState = false;
	public void SetFoldoutState(bool value) => SetFoldoutState(value, null);
	public void SetFoldoutState(bool value, int? visibleChildren)
	{
		visibleChildren ??= VisibleChildren;
		if (currentFoldoutState == value || (value && visibleChildren == 0))
			return;
		currentFoldoutState = value;
		if (foldoutTween?.IsRunning() ?? false)
			foldoutTween.Kill();
		foldoutTween = GetTree().CreateTween().SetParallel().SetTrans(Tween.TransitionType.Quint);

		//foldoutTween.SetEase(value ? Tween.EaseType.Out : Tween.EaseType.In);

		foldoutTween.TweenProperty(foldoutTarget, "custom_minimum_size:y", foldoutInteractionPanel.Size.Y + (value ? extraSpace + elementContainer.Size.Y : 0), foldoutTime);

		if (rotationTarget is not null)
			foldoutTween.TweenProperty(rotationTarget, "rotation", Mathf.DegToRad(value ? openRotation : closedRotation), foldoutTime);
	}

	public void SetFoldoutStateImmediate(bool value)
	{
		currentFoldoutState = value;
		if (foldoutTween?.IsRunning() ?? false)
			foldoutTween.Kill();
		var cms = foldoutTarget.CustomMinimumSize;
		cms.Y = foldoutInteractionPanel.Size.Y + (value ? extraSpace + elementContainer.Size.Y : 0);
		foldoutTarget.CustomMinimumSize = cms;
		rotationTarget?.Rotation = Mathf.DegToRad(value ? openRotation : closedRotation);
	}

	public void SetFoldoutName(string name) => EmitSignal(SignalName.NameChanged, name);
	public void SetNotification(bool visible) => EmitSignal(SignalName.NotificationVisible, visible);

	public void AddFoldoutChild(Node node) => foldoutChildParent.AddChild(node);
	public Node[] GetFoldoutChildren() => [.. foldoutChildParent.GetChildren()];
	public void RemoveFoldoutChild(Node node) => foldoutChildParent.RemoveChild(node);
}
