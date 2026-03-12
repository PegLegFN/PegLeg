using Godot;
using System;
using System.Threading.Tasks;

[Tool]
public partial class ItemUpgradeAnimation : Control
{
    static ItemUpgradeAnimation instance;

    [Signal]
    public delegate void PlayParticlesEventHandler();

    [Export]
    Control _modalWindow;
    ModalWindow modalWindow;

    [ExportGroup("Sound Nodes")]
    [Export]
    AudioStreamPlayer upgradeStart;
    [Export]
    AudioStreamPlayer whoosh;
    [Export]
    AudioStreamPlayer hit;
    [Export]
    AudioStreamPlayer upgradeEnd;

    [ExportGroup("Anim Nodes")]
    [Export]
    Control anvilScaleNode;
    [Export]
    TextureRect itemNode;
    [Export]
    Control hammerPosition;
    [Export]
    Control hammerPivot;
    [Export]
    Control finalText;

    [ExportGroup("Anvil Settings")]
    [Export]
    float anvilGrowDuration = 0.5f;
    [Export]
    float anvilShrinkDelay = 0.2f;
    [Export]
    float anvilShrinkDuration = 0.5f;

    [ExportGroup("Hammer Settings")]
    [Export(PropertyHint.Range, "0,1")]
    float hammerSize;
    [Export(PropertyHint.Range, "0,1")]
    float hammerStartOffset;
    [Export(PropertyHint.Range, "0,1")]
    float hammerHoldOffset;
    [Export(PropertyHint.Range, "0,1")]
    float hammerEndOffset;
    [Export]
    float hammerStartRot;
    [Export]
    float hammerHoldRot;
    [Export]
    float hammerRaiseDuration = 1;
    [Export]
    float hammerWaitDuration = 0.1f;
    [Export]
    float hammerFallDuration = 0.5f;

    [ExportGroup("Item Settings")]
    [Export(PropertyHint.Range, "0,1")]
    float itemSize;
    [Export(PropertyHint.Range, "0,1")]
    float itemStartOffset;
    [Export(PropertyHint.Range, "0,1")]
    float itemEndOffset;
    [Export]
    float itemStartRot;
    [Export]
    float itemGrowDuration = 0.1f;
    [Export]
    float itemFallDuration = 0.5f;


    [ExportGroup("Text Settings")]
    [Export]
    float textEnterDuration = 0.7f;
    [Export]
    float textEnterDelay = 0.2f;
    [Export]
    float textStayDuration = 0.6f;
    [Export]
    float textLeaveDuration = 0.3f;

    public override void _Ready()
    {
        if (Engine.IsEditorHint())
            return;
        modalWindow = _modalWindow as ModalWindow;
        anvilScaleNode.Scale = Vector2.Zero;
        instance = this;
    }

    public static void PlayAnimation(Texture2D itemTexture, Func<Task> upgradeTask = null, bool forceFast = false) =>
        instance?.PlayAnimationInst(itemTexture, upgradeTask, forceFast);
    [ExportGroup("Custom Attributes")]
    [Export(PropertyHint.Range, "0,1")]
    float HammerOffset
    {
        get => hammerPosition?.AnchorTop ?? 0;
        set
        {
            if (hammerPosition is null)
                return;
            hammerPosition.AnchorTop = value;
            hammerPosition.AnchorBottom = value + hammerSize;
        }
    }

    [Export(PropertyHint.Range, "0,1")]
    float ItemOffset
    {
        get => itemNode?.AnchorTop ?? 0;
        set
        {
            if (itemNode is null)
                return;
            itemNode.AnchorTop = value;
            itemNode.AnchorBottom = value + itemSize;
        }
    }

    bool lockAnimation = false;
    async void PlayAnimationInst(Texture2D itemTexture, Func<Task> upgradeTask, bool forceFast)
    {
        if (lockAnimation)
            return;
        lockAnimation = true;
        bool fastAnimations = AppConfig.Get("misc", "fast_animations", false) || forceFast;
        //GD.Print(fastAnimations);
        modalWindow.SetWindowOpen(true);

        hammerPivot.Modulate = Colors.White;
        hammerPivot.RotationDegrees = hammerStartRot;
        hammerPivot.Scale = Vector2.Zero;
        HammerOffset = hammerStartOffset;

        itemNode.Scale = Vector2.Zero;
        itemNode.RotationDegrees = itemStartRot;
        ItemOffset = itemStartOffset;
        itemNode.Texture = itemTexture;

        anvilScaleNode.Scale = Vector2.Zero;
        finalText.Scale = Vector2.Zero;

        var upgradeOperation = upgradeTask?.Invoke();

        if (!fastAnimations)
        {
            upgradeStart.Play();
            var anvilgrowTween = CreateTween().SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Back);
            anvilgrowTween.TweenProperty(anvilScaleNode, "scale", Vector2.One, anvilGrowDuration);
            await Helpers.WaitForTimer(anvilGrowDuration * 0.5);

            var itemScaleTween = CreateTween().SetParallel().SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
            itemScaleTween.TweenProperty(itemNode, "scale", Vector2.One, itemGrowDuration);
            itemScaleTween.TweenProperty(itemNode, "rotation_degrees", 0, itemGrowDuration).SetTrans(Tween.TransitionType.Quad);
            itemScaleTween.TweenProperty(this, "ItemOffset", itemEndOffset, itemFallDuration).SetEase(Tween.EaseType.In);
            //await Helpers.WaitForTimer(itemFallDuration);

            var hammerRaiseTween = CreateTween().SetParallel();
            hammerRaiseTween.TweenProperty(hammerPivot, "scale", Vector2.One, hammerRaiseDuration).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
            hammerRaiseTween.TweenProperty(hammerPivot, "rotation_degrees", hammerHoldRot, hammerRaiseDuration).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Quad);
            hammerRaiseTween.TweenProperty(this, "HammerOffset", hammerHoldOffset, hammerRaiseDuration).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
            await Helpers.WaitForTimer(hammerRaiseDuration + hammerWaitDuration);
            if (upgradeOperation is not null)
                await upgradeOperation;
            whoosh.Play();

            var hammerFallTween = CreateTween().SetParallel().SetEase(Tween.EaseType.In).SetTrans(Tween.TransitionType.Back);
            hammerFallTween.TweenProperty(hammerPivot, "rotation_degrees", 0, hammerFallDuration);
            hammerFallTween.TweenProperty(this, "HammerOffset", hammerEndOffset, hammerFallDuration);
            await Helpers.WaitForTimer(hammerFallDuration);
            EmitSignalPlayParticles();
            hit.Play();

            var anvilShrinkTween = GetTree().CreateTween().SetEase(Tween.EaseType.In).SetTrans(Tween.TransitionType.Back);
            anvilShrinkTween.TweenProperty(anvilScaleNode, "scale", Vector2.Zero, anvilShrinkDuration).SetDelay(anvilShrinkDelay);

            await Helpers.WaitForTimer(textEnterDelay);
        }
        upgradeEnd.Play();
        finalText.RotationDegrees = -270;
        var textAppearTween = CreateTween().SetParallel().SetTrans(Tween.TransitionType.Cubic);
        textAppearTween.TweenProperty(finalText, "rotation_degrees", 0, textEnterDuration);
        textAppearTween.TweenProperty(finalText, "scale", Vector2.One, textEnterDuration);

        await Helpers.WaitForTimer(fastAnimations ? textEnterDuration + textStayDuration*0.5f : anvilShrinkDuration + textStayDuration);

        if(fastAnimations && upgradeOperation is not null)
            await upgradeOperation;

        var textLeaveTween = CreateTween().SetParallel();
        textLeaveTween.TweenProperty(finalText, "scale", Vector2.Zero, textLeaveDuration).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.In);

        await Helpers.WaitForTimer(textLeaveDuration);

        modalWindow.SetWindowOpen(false);
        lockAnimation = false;
    }
}
