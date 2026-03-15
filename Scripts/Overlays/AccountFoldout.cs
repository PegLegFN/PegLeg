using Godot;
using System.Collections.Generic;

public partial class AccountFoldout : ModalWindow
{
    [Export]
    Control selectorButtonLabel;
    [Export]
    Control selectorButtonLabelTarget;
    [Export]
    Control selectorButtonIcons;
    [Export]
    Foldout foldout;
    [Export]
    Button foldoutBtn;
    [Export]
    AccountList accountList;

    protected override string OpenSound => "WipeAppear";
    protected override string CloseSound => "WipeDisappear";

    public override void _Ready()
    {
        accountList.AccountSelected += _ => SetWindowOpen(false);
    }

    protected override Tween BuildTween(bool openState, double duration)
    {
        var tween = CreateTween();
        duration *= 2;
        if (openState)
        {
            accountList.PopulateAccounts();
            selectorButtonIcons.Modulate = Colors.White;
            selectorButtonLabel.Modulate = Colors.Transparent;
            foldout.SetFoldoutStateImmediate(false);
        }
        else
        {
            foldout.SetFoldoutState(false);
        }
        foldoutBtn.Disabled = true;

        tween.TweenInterval(openState ? 0 : 0.1f);
        tween.SetParallel();
        tween.TweenSubtween(base.BuildTween(openState, openState ? duration : duration * 0.5))
            .SetDelay(openState ? 0 : duration * 0.5);

        tween.TweenProperty(selectorButtonIcons, "modulate", openState ? Colors.Transparent : Colors.White, duration*0.5)
            .SetDelay(openState ? duration * 0.25 : duration * 0.5);
        tween.TweenProperty(selectorButtonLabel, "modulate", openState ? Colors.White : Colors.Transparent, duration*0.5)
            .SetDelay(openState ? duration * 0.25 : duration * 0.5);

        tween.TweenProperty(selectorButtonLabel, "custom_minimum_size:x", openState ? selectorButtonLabelTarget.GetCombinedMinimumSize().X : 0, duration * 0.5)
            .SetDelay(duration * 0.5);

        tween.TweenInterval(0.1f);
        return tween;
    }

    protected override void OnTweenFinished(bool openState)
    {
        base.OnTweenFinished(openState);
        if (openState)
        {
            foldoutBtn.Disabled = false;
            foldout.SetFoldoutState(true);
        }
    }
}
