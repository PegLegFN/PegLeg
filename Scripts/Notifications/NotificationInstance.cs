using Godot;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Xml;
using NotifDataContainer = NotificationManager.NotificationDataContainer;

public partial class NotificationInstance : Control
{
	[Export]
	float basisOffset = 25;
    [Export]
    float currentStage;
    [Export]
    Curve itemAnimCurve;
	[Export]
	bool forceIgnoreMouse;

    [ExportGroup("Nodes")]
	[Export]
	CanvasGroup canvasGroup;
	[Export]
	Control interactionBlocker;
	[Export]
	Button[] buttons;
	[Export]
	Label headerLabel;
	[Export]
	Label bodyLabel;
	[Export]
	ShaderHook itemIcon;
    [Export]
    ShaderHook flipbookIcon;
	[Export]
	Button firstBtn;
    [Export]
    Button secondBtn;
    [Export]
    Button superBtn;
	[Export]
	AudioStreamPlayer sfxPlayer;

    NotifDataContainer currentContainer;
	public NotifInstanceState? currentState;
	public bool freezeAnim;

    public event Action OnDismiss;
	public void Dismiss() => OnDismiss?.Invoke();
	public float Stage {
		get => currentStage;
		set => SetNotifStage(value);
	}

	bool flipbookMode;
    void SetNotifStage(float stage)
	{
		currentStage = stage;
        OffsetTop = basisOffset - (-2.5f * stage * stage + 15 * stage);
		float newSize = -0.07f * stage * stage + 1;
		Scale = Vector2.One * newSize;
		float alpha = stage > 0 ?
			-0.25f * stage * stage + 1.5f :
			1.25f + stage * 2.5f;

        float blackout = stage > 0 ?
            -0.2f * stage * stage + 1f :
            1;
        alpha = Mathf.Clamp(alpha, 0, 1);
        blackout = Mathf.Clamp(blackout, 0, 1);
        canvasGroup.SelfModulate = new(blackout, blackout, blackout, alpha);

		float flipAlpha = stage > 0 ?
			2 - stage :
			1;
        flipAlpha = Mathf.Clamp(flipAlpha, 0, 1);
        flipbookIcon.SelfModulate = new(1,1,1, flipAlpha);
    }

    public override void _Ready()
    {
		if (forceIgnoreMouse)
			IgnoreMouseChildren(this);
    }

	void IgnoreMouseChildren(Node child)
	{
		if (child is Control c)
			c.MouseFilter = MouseFilterEnum.Ignore;
		int childCount = child.GetChildCount();
        for (int i = 0; i < childCount; i++)
        {
            IgnoreMouseChildren(child.GetChild(i));
        }
    }

    public override void _Process(double delta)
    {
		if (currentContainer is null || currentState is null || freezeAnim)
			return;
        var realState = currentState ?? default;
		var animLength = flipbookMode ? currentContainer.data.flipbookLength : 1;
        if (realState.animProgress == 0 && Mathf.Abs(currentStage) >= 0.5f)
			return;
		if (realState.animProgress == animLength)
			return;
		if(!currentContainer.audioPlayed)
		{
			//play audio
			if(currentContainer.data.sound is AudioStream stream)
            {
                sfxPlayer.Stream = stream;
                sfxPlayer.Play();
            }
			currentContainer.audioPlayed = true;
		}
        //progress animation
        var fps = animLength / currentContainer.data.animDuration;
        realState.animProgress += fps * (float)delta;
		if (realState.animProgress > animLength)
            realState.animProgress = animLength;
		UpdateProgress(realState.animProgress);
		currentState = realState;
    }

    public void SetNotifInteractable(bool interactable)
    {
        interactionBlocker.Visible = !interactable;
        foreach (var item in buttons)
        {
			item.Disabled = !interactable;
        }
    }

	public void UpdateProgress(float progress)
	{
        if (flipbookMode)
            FlipbookFrame = (int)progress;
        else
            ItemProgress = itemAnimCurve.SampleBaked(progress);
    }

	int FlipbookFrame
	{
		get => flipbookIcon.GetShaderInt("Frame");
		set => flipbookIcon.SetShaderInt(value, "Frame");
	}

    float ItemProgress
    {
        get => itemIcon.GetShaderFloat("transformProgress");
        set => itemIcon.SetShaderFloat(value, "transformProgress");
    }

    public void SetNotifData(NotifDataContainer newContainer)
    {
		if(newContainer is null)
        {
            currentState = null;
			currentContainer = null;
			return;
        }
        if (currentContainer == newContainer)
			return;
        currentContainer = newContainer;
        currentState ??= new(newContainer);
        var data = currentContainer.data;
		headerLabel.Text = data.header;
		bodyLabel.Text = data.body;
		bodyLabel.Visible = !string.IsNullOrWhiteSpace(data.body) && data.items.Length < 8;

		flipbookMode = data.flipbookLength>0;
        itemIcon.Visible = !flipbookMode;
        flipbookIcon.Visible = flipbookMode;
		if (flipbookMode)
		{
			var slice = data.flipbookSlice;
			slice.X = Mathf.Max(slice.X, 1);
            slice.Y = Mathf.Max(slice.Y, 1);
			flipbookIcon.Texture = data.icon;
			flipbookIcon.SetShaderVector(slice, "SliceAmount");
        }
		else
        {
            itemIcon.Texture = data.icon;
            itemIcon.SelfModulate = data.itemColor;
        }
		UpdateProgress(currentState.Value.animProgress);

		firstBtn.Visible = !string.IsNullOrWhiteSpace(data.firstAction);
		firstBtn.Text = data.firstAction;

        secondBtn.Visible = !string.IsNullOrWhiteSpace(data.secondAction);
        secondBtn.Text = data.secondAction;

        superBtn.Visible = !string.IsNullOrWhiteSpace(data.superAction);
        superBtn.Text = data.superAction;
    }

	public void PerformAction(string actionType)
	{
		if (Enum.TryParse<NotifAction>(actionType, out var action))
        {
            currentContainer.data.SubmitAction(action);
            Dismiss();
        }
	}

	public void AnimateStage(float toStage, double duration, double delay = 0, float? fromStage = null)
    {
		GD.Print("animating stage");
		Stage = fromStage ?? Stage;
		var tween = CreateTween();
		tween.TweenInterval(delay);
		tween.TweenProperty(this, "Stage", toStage, duration);
		tween.Play();
    }

	public struct NotifInstanceState
	{
		public float animProgress;

		public NotifInstanceState() { }

        public NotifInstanceState(NotifDataContainer container)
        {
			if (container?.audioPlayed == true)
				animProgress = Mathf.Max(container.data.flipbookLength, 1);
        }
    }
}
