using Godot;
using System;
using System.Text.Json.Nodes;

public partial class QuestObjective : Control
{
    [Signal]
    public delegate void NameChangedEventHandler(string name);
    [Signal]
    public delegate void TooltipChangedEventHandler(string name);
    [Signal]
    public delegate void ProgressTextChangedEventHandler(string progressText);
    [Signal]
    public delegate void TextColorChangedEventHandler(Color progressColor);
    [Signal]
    public delegate void IsCompleteEventHandler(bool isComplete);

    [Export]
    ProgressBar progressBar;
    [Export]
    Color incompleteBarColor;
    [Export]
    Color incompleteTextColor;
    [Export]
    Color completeColor;

    StyleBoxFlat progressBarStyle;
    public override void _Ready()
    {
        base._Ready();
        if (progressBar.GetThemeStylebox("fill") is StyleBoxFlat flatSB && flatSB.ResourceLocalToScene)
            progressBarStyle = flatSB;
    }

    public void SetupObjective(JsonObject objective, int currentProgress)
    {
        EmitSignal(SignalName.NameChanged, objective["Description"].ToString());

        int maxProgress = objective["Count"].GetValue<int>();
        progressBar.MaxValue = maxProgress;
        progressBar.Value = currentProgress;
        EmitSignal(SignalName.ProgressTextChanged, $"{currentProgress}/{maxProgress}");

        string tooltipText = $"{objective["HudShortDescription"]}: {currentProgress}/{maxProgress}";
        if (objective["HudShortDescription"].ToString() != objective["Description"].ToString())
            tooltipText = objective["Description"].ToString() +"\n"+ tooltipText;
        EmitSignal(SignalName.TooltipChanged, tooltipText);

        bool isComplete = currentProgress == maxProgress;
        EmitSignal(SignalName.IsComplete, isComplete);

        if (progressBarStyle is not null)
            progressBarStyle.BgColor = isComplete ? completeColor : incompleteBarColor;
        EmitSignal(SignalName.TextColorChanged, isComplete ? completeColor : incompleteTextColor);
    }
}
