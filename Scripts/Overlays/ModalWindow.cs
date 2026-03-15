using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

public partial class ModalWindow : Control
{
    protected static List<ModalWindow> windowStack = [];
    public static bool StackEmpty()
    {
        windowStack.RemoveAll(x => x?.IsInsideTree() != true);
        return windowStack.Count == 0;
    }
    protected bool IsTopOfStack() => windowStack.LastOrDefault() == this;

    [Signal]
    public delegate void WindowOpenedEventHandler();
    [Signal]
    public delegate void WindowClosedEventHandler();

    [Export]
    Control backgroundPanel;
    [Export]
    protected CanvasGroup windowCanvas;
    [Export]
    protected Control windowControl;

    [Export]
    float tweenTime = 0.1f;
    [Export]
    float shrunkScale = 0.5f;
    [Export]
    bool startOpen;
    [Export]
    protected bool useSounds = true;
    [Export]
    protected bool isUserClosable = false;
    protected virtual bool UseWindowAnim => true;

    Window linkedWindow;
    public override void _Ready()
    {
        Visible = false;
        backgroundPanel.MouseFilter = MouseFilterEnum.Ignore;
        MouseFilter = MouseFilterEnum.Ignore;
        backgroundPanel.Modulate = Colors.Transparent;

        if (UseWindowAnim)
        {
            if (windowCanvas is not null)
            {
                windowCanvas.SelfModulate = Colors.Transparent;
                windowCanvas.Scale = Vector2.One * shrunkScale;
            }
            if (windowControl is not null)
            {
                windowControl.Modulate = Colors.Transparent;
                windowControl.Scale = Vector2.One * shrunkScale;
            }
        }

        if (startOpen)
            SetWindowOpen(true);

        linkedWindow = GetWindow();
        linkedWindow.GoBackRequested += TryCloseWindowViaInput;
    }

    public override void _ExitTree()
    {
        linkedWindow.GoBackRequested -= TryCloseWindowViaInput;
    }

    public override void _Process(double delta)
    {
        openedThisFrame = false;
        if (windowControl is not null)
        {
            windowControl.PivotOffset = windowControl.Size * 0.5f;
        }
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is InputEventKey keyEvent && keyEvent.Pressed && keyEvent.Keycode == Key.Escape)
        {
            TryCloseWindowViaInput(out var result);
            if (result)
                GetViewport().SetInputAsHandled();
        }
    }

    void TryCloseWindowViaInput() => TryCloseWindowViaInput(out _);
    void TryCloseWindowViaInput(out bool result)
    {
        result = false;
        if (!isUserClosable || !IsOpen || windowStack.LastOrDefault() != this)
            return;
        result = true;
        CloseWindowViaInput();
    }

    protected virtual void CloseWindowViaInput() => SetWindowOpen(false);

    public bool IsOpen { get; private set; }
    void OpenWindow() => SetWindowOpen(true);
    void CloseWindow() => SetWindowOpen(false);

    Tween currentTween;
    bool openedThisFrame = false;
    public float Dummy = 0;
    public virtual void SetWindowOpen(bool openState)
    {
        if (openState == IsOpen || !IsInstanceValid(this))
            return;

        windowStack.Remove(this);
        if (openState)
            windowStack.Add(this);

        if (currentTween is not null && currentTween.IsRunning())
            currentTween.Kill();
        IsOpen = openState;
        if (openedThisFrame && !openState)
        {
            CancelOpenImmediate();
            return;
        }
        if (openState)
        {
            openedThisFrame = true;
            WhileOpen().StartTask();
        }
        currentTween = BuildTween(openState, tweenTime);
        currentTween.Finished += () =>
        {
            if (IsInstanceValid(this))
                OnTweenFinished(openState);
        };
        currentTween.Play();
    }

    protected virtual async Task WhileOpen()
    {
        while (IsOpen)
            await Helpers.WaitForFrame();
    }

    protected virtual string OpenSound => "PanelAppear";
    protected virtual string CloseSound => "PanelDisappear";

    protected virtual void CancelOpenImmediate()
    {
        backgroundPanel.MouseFilter = MouseFilterEnum.Ignore;
        MouseFilter = MouseFilterEnum.Ignore;
        Visible = false;
        UISounds.StopSound(OpenSound);
        ProcessMode = ProcessModeEnum.Disabled;
    }

    protected virtual Tween BuildTween(bool openState, double duration)
    {
        var tween = CreateTween().SetParallel();
        if (openState)
        {
            if (useSounds)
                UISounds.PlaySound(OpenSound);
            backgroundPanel.MouseFilter = MouseFilterEnum.Stop;
            MouseFilter = MouseFilterEnum.Stop;
            Visible = true;
            ProcessMode = ProcessModeEnum.Inherit;
            if (windowControl is not null)
                windowControl.PivotOffset = windowControl.Size * 0.5f;
            EmitSignal(SignalName.WindowOpened);
        }
        else
        {
            if (useSounds)
                UISounds.PlaySound(CloseSound);
            EmitSignal(SignalName.WindowClosed);
        }

        var newSize = openState ? 1 : shrunkScale;
        var newColour = openState ? Colors.White : Colors.Transparent;
        tween.TweenProperty(backgroundPanel, "modulate", newColour, duration);

        if (UseWindowAnim)
        {
            if (windowCanvas is not null)
            {
                tween.TweenProperty(windowCanvas, "self_modulate", newColour, duration);
                tween.TweenProperty(windowCanvas, "scale", Vector2.One * newSize, duration);
            }

            if (windowControl is not null)
            {
                tween.TweenProperty(windowControl, "modulate", newColour, duration);
                tween.TweenProperty(windowControl, "scale", Vector2.One * newSize, duration);
            }
        }
        return tween;
    }

    protected virtual void OnTweenFinished(bool openState)
    {
        if (!openState)
        {
            backgroundPanel.MouseFilter = MouseFilterEnum.Ignore;
            MouseFilter = MouseFilterEnum.Ignore;
            Visible = false;
            ProcessMode = ProcessModeEnum.Disabled;
        }
    }
}
