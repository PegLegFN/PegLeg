using Godot;
using System;

public partial class ResponsiveButton : BaseButton
{
    [Signal]
    public delegate void HoverChangedEventHandler(bool hoverState);
    [Signal]
    public delegate void HoldChangedEventHandler(bool holdState);
    [Signal]
    public delegate void HoldPressedEventHandler();
    [Signal]
    public delegate void HoldReleasedEventHandler();

    [Export]
    bool circleMode;
    [Export]
    bool useHold;
    [Export]
    float outlinePadding = 0;
    [Export]
    float resetTime = 0.1f;
    [Export]
    float hoverTime = 0.1f;
    [Export]
    float pressTime = 0.1f;
    [Export]
    float holdTime = 1.5f;
    [Export]
    float holdRecoverTime = 1f;
    [Export]
    float holdTriggerTime = 0.3f;

    [ExportGroup("Anchor Offsets")]
    [Export]
    int baseOffset = 10;
    [Export]
    int hoverOffset = 0;
    [Export]
    int pressOffset = -2;

    [ExportGroup("Line Size")]
    [Export]
    int baseLineSize = 0;
    [Export]
    int hoverLineSize = 3;
    [Export]
    int pressLineSize = 3;

    [ExportGroup("Sounds")]
    [Export]
    bool useHoverSound = true;
    [Export]
    bool useHoldSound = true;
    [Export]
    bool usePressSound = true;

    [ExportGroup("Nodes")]
    [Export]
    Control target;
    [Export]
    ShaderHook outlineObject;
    [Export]
    Panel fallbackOutlineObject;
    [Export]
    ProgressBar fallbackProgressBar;

    [ExportGroup("Other Settings")]
    [Export]
    bool autoAttach = true;
    [Export]
    bool cancelHoldOnMouseExit = true;
    [Export]
    Color holdActivationColor = Colors.Yellow;
    [Export]
    bool alwaysSendHoldReleasedWhenPressed = false;

    Tween outlineTween;
    Tween holdTween;
    Tween holdPressTween;
    Timer holdTimer;

    bool fallback = false;
    StyleBoxFlat fallbackOutline;
    StyleBoxFlat fallbackProgressFill;

    float lineSize = 0;
    float LineSize
    {
        get => outlineObject?.GetShaderFloat("LineSize") ?? lineSize;
        set => outlineObject?.SetShaderFloat(lineSize = value, "LineSize");
    }

    float FillAmount
    {
        get
        {
            if (fallback)
                return (float)fallbackProgressBar.Value;
            return outlineObject?.GetShaderFloat("Progress") ?? 0;
        }
        set
        {
            if (fallback)
            {
                fallbackProgressBar.Value = value;
                return;
            }
            outlineObject?.SetShaderFloat(value, "Progress");
        }
    }

    // Called when the node enters the scene tree for the first time.
    public override void _Ready()
	{
        if (OS.HasFeature("mobile"))
        {
            fallback = true;
            outlineObject.QueueFree();
            outlineObject = null;
            fallbackOutlineObject.Visible = true;
            fallbackProgressBar.Visible = true;
            fallbackProgressBar.Value = 0;
        }
		target ??= GetChild<Control>(0);
        holdTimer = new();
        holdTimer.Timeout += OnHoldPerformed;
        AddChild(holdTimer);
        target.OffsetTop = -baseOffset;
        target.OffsetBottom = baseOffset;
        target.OffsetLeft = -baseOffset;
        target.OffsetRight = baseOffset;
        //if (useAnchors)
        //{
        //    target.AnchorTop = fromAnchors.W;
        //    target.AnchorBottom = fromAnchors.X;
        //    target.AnchorLeft = fromAnchors.Y;
        //    target.AnchorRight = fromAnchors.Z;
        //}
        target.Modulate = Colors.Transparent;
        outlineObject?.SetShaderFloat(baseLineSize, "LineSize");
        outlineObject?.SetShaderFloat(0, "Progress");
        if (circleMode)
            outlineObject?.SetShaderBool(true, "Circle");

        if (autoAttach)
        {
            MouseEntered += () => SetHoverState(true);
            MouseExited += () => SetHoverState(false);
            VisibilityChanged += () => SetHoverState(false);
            
            ButtonDown += () => SetHeldState(true);
            ButtonUp += () => SetHeldState(false);
        }
        ItemRectChanged += UpdateTargetSize;
        UpdateTargetSize();
    }

    private void UpdateTargetSize()
    {
        outlineObject?.SetShaderVector(circleMode ? Vector2.Zero : (Size - new Vector2(outlinePadding, outlinePadding)), "TargetSize");
    }

    private void OnHoldPerformed()
    {
        holdTimer.Stop();
        holdActive = true;
        //GD.Print("Hold Pressed");
        if (usePressSound)
            UISounds.PlaySound("ButtonHoldComplete");

        target.Modulate = holdActivationColor;

        if (holdTween?.IsRunning() ?? false)
            holdTween.Kill();
        holdPressTween = GetTree().CreateTween().SetParallel();
        holdPressTween.TweenProperty(target, "modulate", Colors.White, holdTriggerTime);
        holdPressTween.Finished += () => SetHoverState(false);

        EmitSignal(SignalName.HoldPressed);
    }
    private void OnHoldReleased()
    {
        //GD.Print("Hold Release");
        EmitSignal(SignalName.HoldReleased);
    }

    public void SetHoverState(bool newHovered, bool playSounds=true)
    {
        //prevents accidental highlights when using spinbox, in which case hover events wouldnt change anyway
        if (Input.MouseMode == Input.MouseModeEnum.Captured)
            return;
        if (hovered == newHovered)
            return;

        EmitSignal(SignalName.HoverChanged, newHovered);
        if (cancelHoldOnMouseExit && !newHovered && held)
        {
            held = false;
            if (useHold)
            {
                holdTimer.Stop();
                SetHoldTween(false);
                if (alwaysSendHoldReleasedWhenPressed && holdActive)
                    OnHoldReleased();
                if (useHoldSound && !holdActive)
                {
                    UISounds.StopSound("ButtonHold");
                    UISounds.PlaySound("ButtonHoldCancel");
                }
                holdActive = false;
                EmitSignal(SignalName.HoldChanged, false);
            }
        }

        hovered = newHovered;
        if (hovered && useHoverSound && playSounds)
            UISounds.PlaySound("Hover");
        UpdateOutlineState();
    }

    public void SetHeldState(bool newHeld)
    {
        if (held == newHeld)
            return;
        bool skipHoldSound = false;
        if (useHold)
        {
            if (newHeld && holdTimer.IsStopped())
            {
                holdTimer.WaitTime = holdTime;
                holdTimer.Start();
            }
            if (!newHeld && !holdTimer.IsStopped())
                holdTimer.Stop();
            if (!newHeld && holdActive)
            {
                if (held || alwaysSendHoldReleasedWhenPressed)
                    OnHoldReleased();
                holdActive = false;
            }
            if (held != newHeld)
            {
                EmitSignal(SignalName.HoldChanged, newHeld);
                SetHoldTween(newHeld);
            }
        }
        held = newHeld;
        if (held && !hovered)
        {
            hovered = true;
            if (useHoverSound)
                UISounds.PlaySound("Hover");
            EmitSignal(SignalName.HoverChanged, true);
        }
        if (useHold)
        {
            if (useHoldSound)
            {
                if (held)
                    UISounds.PlaySound("ButtonHold");
                else if (!skipHoldSound)
                {
                    UISounds.StopSound("ButtonHold");
                    UISounds.PlaySound("ButtonHoldCancel");
                }
            }
        }
        else
        {
            if (!held && usePressSound)
                UISounds.PlaySound("ButtonPress");
        }
        UpdateOutlineState();
    }

    void SetHoldTween(bool value)
    {
        if (holdTween?.IsRunning() ?? false)
            holdTween.Kill();
        holdTween = GetTree().CreateTween().SetParallel().SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        if (value)
        {
            holdTween.TweenProperty(this, "FillAmount", 1, holdTime);
        }
        else
        {
            holdTween.TweenProperty(this, "FillAmount", 0, holdRecoverTime).SetTrans(Tween.TransitionType.Expo);
        }
    }

    bool hovered = false;
    bool held = false;
    bool holdActive;
    public T StateCheck<T>(T onPress, T onHover, T onBase)=> held ? onPress : (hovered ? onHover : onBase);
    public void UpdateOutlineState()
    {
        float offsetTarget = StateCheck(pressOffset, hoverOffset, baseOffset);
        Color colorTarget = StateCheck(Colors.White, Colors.White, Colors.Transparent);
        float lineSizeTarget = StateCheck(pressLineSize, hoverLineSize, baseLineSize);
        float duration = StateCheck(pressTime, hoverTime, resetTime);
        PerformTween(offsetTarget, colorTarget, lineSizeTarget, duration);
    }

    public void PerformTween(float offsetTarget, Color colorTarget, float lineSizeTarget, float duration)
    {
        if (outlineTween?.IsRunning() ?? false)
            outlineTween.Kill();
        if (holdPressTween?.IsRunning() ?? false)
            holdPressTween.Kill();
        outlineTween = GetTree().CreateTween().SetParallel();

        outlineTween.TweenProperty(target, "offset_top", -offsetTarget, duration);
        outlineTween.TweenProperty(target, "offset_bottom", offsetTarget, duration);
        outlineTween.TweenProperty(target, "offset_left", -offsetTarget, duration);
        outlineTween.TweenProperty(target, "offset_right", offsetTarget, duration);

        outlineTween.TweenProperty(target, "modulate", colorTarget, duration);

        outlineTween.TweenProperty(this, "LineSize", lineSizeTarget, duration);
    }

}
