using Godot;
using System;

public partial class ToggleIconAnim : Control
{
    [Export]
    Control onIcon;
    [Export]
    Control offIcon;
    [Export]
    float onPadding = 5;
    [Export]
    float offPadding = -3;
    [Export]
    bool hideOff = true;
    [Export]
    bool defaultState = false;
    [Export]
    float onDuration = 0.3f;
    [Export]
    float offDuration = 0.2f;

    Tween animTween;

    float OnSpacing
    {
        get => -onIcon.OffsetTop;
        set
        {
            onIcon.OffsetTop = -value;
            onIcon.OffsetLeft = -value;
            onIcon.OffsetBottom = value;
            onIcon.OffsetRight = value;
        }
    }
    float OffSpacing
    {
        get => -offIcon.OffsetTop;
        set
        {
            offIcon.OffsetTop = -value;
            offIcon.OffsetLeft = -value;
            offIcon.OffsetBottom = value;
            offIcon.OffsetRight = value;
        }
    }

    public override void _Ready()
    {
        SetState(defaultState);
    }

    public void SetState(bool state)
    {
        if (animTween?.IsRunning() == true)
            animTween.Stop();
        OnSpacing = 0;
        OffSpacing = 0;
        onIcon.SelfModulate = state ? Colors.White : Colors.Transparent;
        offIcon.SelfModulate = state && hideOff ? Colors.Transparent : Colors.White;
    }

    public void Animate(bool state)
    {
        if (animTween?.IsRunning() == true)
            animTween.Stop();
        animTween = CreateTween();
        if (state)
        {
            animTween.SetParallel();
            OnSpacing = onPadding;

            var spacingSequence = CreateTween().SetTrans(Tween.TransitionType.Cubic);
            spacingSequence.TweenProperty(this, "OnSpacing", offPadding, onDuration * 0.33).SetDelay(onDuration * 0.33);
            spacingSequence.TweenProperty(this, "OnSpacing", 0, onDuration * 0.33).SetEase(Tween.EaseType.In);

            animTween.TweenProperty(onIcon, "self_modulate", Colors.White, onDuration * 0.5);
            animTween.TweenProperty(offIcon, "self_modulate", hideOff ? Colors.Transparent : Colors.White, onDuration * 0.33).SetDelay(onDuration * 0.33);
            animTween.TweenProperty(this, "OffSpacing", offPadding, onDuration * 0.33).SetDelay(onDuration * 0.33);
            animTween.TweenSubtween(spacingSequence);
        }
        else
        {
            animTween.SetParallel();
            OffSpacing = 0;

            var spacingSequence = CreateTween().SetTrans(Tween.TransitionType.Cubic);
            spacingSequence.TweenProperty(this, "OffSpacing", offPadding, offDuration * 0.5).SetEase(Tween.EaseType.Out);
            spacingSequence.TweenProperty(this, "OffSpacing", 0, offDuration * 0.5).SetEase(Tween.EaseType.In);

            animTween.TweenProperty(onIcon, "self_modulate", Colors.Transparent, offDuration * 0.5);
            animTween.TweenProperty(offIcon, "self_modulate", Colors.White, offDuration * 0.5);
            animTween.TweenProperty(this, "OnSpacing", offPadding, offDuration * 0.5);
            animTween.TweenSubtween(spacingSequence);
        }
    }
}
