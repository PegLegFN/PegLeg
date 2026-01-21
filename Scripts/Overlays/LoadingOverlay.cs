using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class LoadingOverlay : ModalWindow
{
    [Signal]
    public delegate void ProgressChangedEventHandler(float totalProgress);

    static LoadingOverlay instance;
    static List<TaskToken> loadingTokens = [];
    [Export]
    RichTextLabel progressLabel;

    public override void _Ready()
    {
        base._Ready();
        instance = this;
        bool complete = !loadingTokens.Any(t => !t.disposed);
        SetWindowOpen(!complete);
    }

    public override void _ExitTree()
    {
        if (instance == this)
            instance = null;
    }

    public override void _Process(double delta)
    {
        if(loadingTokens.Any(t=>t.dirty))
            UpdateLoadingState();
    }

    static void UpdateLoadingState()
    {
        bool complete = !loadingTokens.Any(t => !t.disposed);
        //GD.PushWarning("tokens complete: " + complete);
        if (complete == instance?.IsOpen)
            instance?.SetWindowOpen(!complete);
        if (complete)
        {
            loadingTokens.Clear();
            return;
        }
        foreach (var t in loadingTokens)
        {
            t.Clean();
        }
        //GD.PushWarning("tokens: " + loadingTokens.Count);
        instance?.UpdateLoadingProgress();
    }

    void UpdateLoadingProgress()
    {
        float totalProgress = loadingTokens.Select(t => t.progress).Sum();
        float totalMaxProgress = loadingTokens.Select(t => t.maxProgress).Sum();
        float progressPercent = totalProgress / totalMaxProgress;
        EmitSignal(SignalName.ProgressChanged, progressPercent);
        if (progressLabel is not null)
            progressLabel.Text = string.Join("\n", loadingTokens.Where(t => !t.disposed).Select(t => t.ProgressText));
    }

    public static TaskToken CreateToken(string taskName = null, float initialProgress = 0, float maxProgress = 1) =>
        TaskToken.Create(taskName, initialProgress, maxProgress);

    public class TaskToken() : IDisposable
    {
        public bool disposed { get; private set;}
        public string taskName { get; private set; }
        public float progress { get; private set; }
        public float maxProgress { get; private set; }
        public bool dirty { get; private set; }

        public string ProgressText => taskName + (maxProgress > 0 ? $"({progress}/{maxProgress})" : "");

        public static TaskToken Create(string taskName = null, float initialProgress = 0, float maxProgress = 1)
        {
            TaskToken token = new()
            {
                taskName = taskName,
                progress = initialProgress,
                maxProgress = Mathf.Max(0, maxProgress)
            };
            loadingTokens.Add(token);
            UpdateLoadingState();
            return token;
        }

        public void IncrementLoadingProgress() => SetLoadingProgress(progress + 1);
        public void SetLoadingProgress(float newProgress)=>
            SetLoadingProgress(newProgress, maxProgress);
        public void SetLoadingProgress(float newProgress, float maxProgress)
        {
            if (disposed)
                return;
            this.maxProgress = Mathf.Max(maxProgress, 0);
            progress = Mathf.Clamp(newProgress, 0, maxProgress);
            dirty = true;
        }

        public void Clean() => dirty = false;

        public void Dispose()
        {
            if (disposed)
                return;
            //GD.PushWarning($"disposing token \"{taskName}\" ({guid})");
            progress = maxProgress;
            if (loadingTokens.Contains(this))
            {
                disposed = true;
                //GD.PushWarning($"removing token \"{taskName}\" ({guid})");
                loadingTokens.Remove(this);
                UpdateLoadingState();
            }
        }
    }
}
