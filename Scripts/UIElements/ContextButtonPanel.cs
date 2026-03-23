using Godot;
using System;

public partial class ContextButtonPanel : Control
{
	[Export]
	TextureButton button;
    [Export]
    float baseMargin = 6;
    [Export]
    float hoverMargin = 0;
    [Export]
    float pressMargin = 3;
    [Export]
    float duration = 0.1f;

    public override void _Ready()
    {
        button.MouseEntered += () => SetHoverState(true);
        button.MouseExited += () => SetHoverState(false);
        VisibilityChanged += () => SetHoverState(false);

        button.ButtonDown += () => SetHeldState(true);
        button.ButtonUp += () => SetHeldState(false);
        Offset = baseMargin;
        SelfModulate = Colors.Transparent;
    }

    float Offset
    {
        get => OffsetTop;
        set
        {
            OffsetTop = value;
            OffsetLeft = value;
            OffsetBottom = -value;
            OffsetRight = -value;
        }
    }

    bool hovered = false;
    bool held = false;
    public void SetHoverState(bool newHovered)
    {
        //prevents accidental highlights when using spinbox, in which case hover events wouldnt change anyway
        if (Input.MouseMode == Input.MouseModeEnum.Captured)
            return;
        if (hovered == newHovered)
            return;
        hovered = newHovered;
        if (!newHovered)
            held = false;
        AnimateOffset(newHovered ? hoverMargin : baseMargin, newHovered);
    }

    public void SetHeldState(bool newHeld)
    {
        if (held == newHeld)
            return;
        held = newHeld;
        AnimateOffset(newHeld ? pressMargin : hoverMargin, true);
    }

    Tween offsetTween;
    void AnimateOffset(float target, bool visible)
    {
        if (offsetTween?.IsRunning() ?? false)
            offsetTween.Kill();
        offsetTween = CreateTween().SetParallel();

        offsetTween.TweenProperty(this, "Offset", target, duration);
        offsetTween.TweenProperty(this, "self_modulate", visible ? Colors.White : Colors.Transparent, duration);
    }
}
