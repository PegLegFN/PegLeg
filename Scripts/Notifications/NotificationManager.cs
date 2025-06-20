using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

public partial class NotificationManager : Control
{
    [Export]
    NotificationInstance[] notificationInstances;
    [Export]
    Control mouseBlocker;
    public List<NotificationDataContainer> activeNotifications = [];

    static Queue<NotificationData> notificationQueue = new();
    static NotificationManager instance;
    static readonly Vector2[] fullPassthrough = new Vector2[2];

    public static void Push(IEnumerable<NotificationData> data)
    {
        foreach (var item in data)
        {
            notificationQueue.Enqueue(item);
        }

        if (instance is null)
            return;

        instance.queueTimer.Start();
    }

    Window window;
    Vector2I baseSize;
    Timer queueTimer;
	public override void _Ready()
	{
        window = GetWindow();
        window.MousePassthroughPolygon = fullPassthrough;
        baseSize = window.Size;
        window.Visible = AppConfig.Get("experimental", "notifications", false);
        SetScale((float)AppConfig.Get("notification", "scale", 1.0));

        foreach (var notification in notificationInstances)
        {
            notification.OnDismiss += DismissCurrent;
        }
        instance = this;
        queueTimer = new() { WaitTime = 0.5, OneShot = true };
        AddChild(queueTimer);
        if (notificationQueue.Count > 0)
            queueTimer.Start();
        queueTimer.Timeout += AppendFromQueue;
        ListProgress = _listProgress;
        AppConfig.OnConfigChanged += OnConfigChanged;
        MouseEntered += CheckPassthrough;
    }

    private void CheckPassthrough()
    {
        if ((window.MousePassthroughPolygon.Length == 0) == (activeNotifications.Count == 0))
            GD.Print("Mismatching notification window state. Correcting...");
        window.MousePassthroughPolygon = activeNotifications.Count == 0 ? fullPassthrough : [];
    }

    private void OnConfigChanged(string section, string key, JsonValue value)
    {
        if (section == "experimental" && key == "notifications" && value.TryGetValue(out bool notifsVisible))
            window.Visible = notifsVisible;
        if (section == "notification" && key == "scale")
            SetScale(value.TryGetValue(out double scale) ? (float)scale : 1);
    }

    public override void _ExitTree()
    {
        AppConfig.OnConfigChanged -= OnConfigChanged;
        instance = null;
    }

    void SetScale(float scaleAmt)
    {
        var newSize = (Vector2I)((Vector2)baseSize * scaleAmt);
        window.Size = newSize;
        window.ContentScaleFactor = scaleAmt;
        var safeRect = DisplayServer.GetDisplaySafeArea();
        window.Position = safeRect.Position + safeRect.Size - (window.Size + new Vector2I(5, 5 - (int)(9 * scaleAmt)));
    }

    bool appendQueued = false;
    bool isListChanging = false;
    bool allowOutOfRange = false;
    async void AppendFromQueue()
    {
        if (listTarget != _listProgress || isListChanging)
        {
            appendQueued = true;
            instance.queueTimer.Start(0.1);
            return;
        }
        appendQueued = false;
        //show window if not already visible
        if (activeNotifications.Count == 0)
        {
            window.MousePassthroughPolygon = [];
            GD.Print("open notifs");
        }
        isListChanging = true;
        allowOutOfRange = false;
        try
        {
            var queueContent = notificationQueue.ToArray();
            GD.Print($"dequeuing {queueContent.Length}");
            notificationQueue.Clear();
            var urgent = queueContent.Where(d => d.urgent).ToArray();
            var nonUrgent = queueContent.Where(d => !d.urgent).ToArray();


            if (urgent.Length > 0)
            {
                ResetList();
                //insert urgent items before foreground notification
                _listProgress = currentListIdx = (activeNotifications.Count - urgent.Length);
                activeNotifications.AddRange(urgent.Select(WrapData));
                listTarget = activeNotifications.Count-1;
                GD.Print($"p:{_listProgress}, t:{listTarget}");

                //animate urgent items
                if(urgent.Length>1)
                    foreach ( var inst in notificationInstances)
                    {
                        inst.freezeAnim = true;
                    }
                while (_listProgress != listTarget)
                {
                    await Helpers.WaitForFrame();
                }
                if (urgent.Length > 1)
                    foreach (var inst in notificationInstances)
                    {
                        inst.freezeAnim = false;
                    }
            }

            if(nonUrgent.Length > 0)
            {
                //add non-urgent notifs
                int preNonUrgentCount = activeNotifications.Count;
                ResetList();
                activeNotifications.InsertRange(0, nonUrgent.Select(WrapData));
                if (preNonUrgentCount > 0)
                {
                    _listProgress += nonUrgent.Length;
                    listTarget += nonUrgent.Length;
                    currentListIdx += nonUrgent.Length;
                }
                allowOutOfRange = true;
                ListProgress = _listProgress;
                if (preNonUrgentCount < 3 && nonUrgent.Length != 0)
                {
                    //animate non-urgent items
                    double delay = 0;
                    int maxAnim = Mathf.Min(3, activeNotifications.Count);
                    GD.Print($"animating from {preNonUrgentCount + 1} to {maxAnim}");
                    for (int i = preNonUrgentCount + 1; i <= maxAnim; i++)
                    {
                        GD.Print(i);
                        notificationInstances[i].AnimateStage(i - 1, 0.15, delay, 3);
                        delay += 0.05;
                    }
                }

            }
        }
        finally
        {
            isListChanging = false;
            allowOutOfRange = true;
        }
    }

    void ResetList()
    {
        if (currentListIdx == activeNotifications.Count || currentListIdx==-1)
            return;
        //make foreground notification be at last index
        var beforeCurrent = activeNotifications[..(currentListIdx + 1)];
        activeNotifications.RemoveRange(0, currentListIdx + 1);
        activeNotifications.AddRange(beforeCurrent);
        _listProgress = listTarget = currentListIdx = activeNotifications.Count - 1;
    }

    public async void DismissCurrent()
    {
        if (listTarget != _listProgress || isListChanging)
            return;
        isListChanging = true;
        allowOutOfRange = false;
        ResetList();
        if (activeNotifications.Count > 1)
        {
            //animate up
            listTarget -= 1;
            while (_listProgress != listTarget)
            {
                await Helpers.WaitForFrame();
            }
            activeNotifications.RemoveAt(activeNotifications.Count - 1);
        }
        else
        {
            //manually dismiss animation, hide window
            notificationInstances[1].AnimateStage(-1, 0.15, 0, 0);
            await Helpers.WaitForTimer(0.15);
            window.MousePassthroughPolygon = fullPassthrough;
            activeNotifications.Clear();
            currentListIdx = 0;
            listTarget = 0;
            _listProgress = 0;
            for (int i = 0; i < notificationInstances.Length; i++)
            {
                notificationInstances[i].Visible = false;
                notificationInstances[i].SetNotifData(null);
                notificationInstances[i].SetNotifInteractable(false);
            }
            GD.Print("close notifs");
        }
        allowOutOfRange = true;
        isListChanging = false;
    }

    public override void _Process(double delta)
    {
        if (!window.Visible)
            return;
        if (ListProgress == listTarget)
        {
            mouseBlocker.Visible = false;
            return;
        }
        mouseBlocker.Visible = true;
        var speed = Mathf.Clamp(listTarget - ListProgress, -2, 2) * 10 * (isListChanging ? 3 : 1);
        var newProgress = ListProgress + (speed * (float)delta);

        if (Math.Abs(speed) < 0.1)
        {
            newProgress = listTarget;
        }

        var newIdx = Mathf.FloorToInt(newProgress);
        int cycleLength = Mathf.Min(3, activeNotifications.Count + 1);
        if (newIdx < currentListIdx)
        {
            //GD.Print("forwards");
            //push frame progress forward
            for (int i = 0; i < cycleLength - 1; i++)
            {
                notificationInstances[i].currentState = notificationInstances[i + 1].currentState;
            }
            for (int i = cycleLength - 1; i < notificationInstances.Length; i++)
            {
                notificationInstances[i].currentState = null;
            }
        }
        if (newIdx > currentListIdx)
        {
            //GD.Print("backwards");
            //push frame progress backward
            for (int i = notificationInstances.Length - 1; i > cycleLength - 1; i--)
            {
                notificationInstances[i].currentState = null;
            }
            for (int i = cycleLength - 1; i > 0; i--)
            {
                notificationInstances[i].currentState = notificationInstances[i - 1].currentState;
            }
            notificationInstances[0].currentState = null;
        }
        if (newProgress == listTarget)
            notificationInstances[0].currentState = new();

        if (listTarget >= activeNotifications.Count)
        {
            newProgress-=activeNotifications.Count;
            listTarget-=activeNotifications.Count;
        }
        if (!isListChanging && listTarget < 0)
        {
            newProgress += activeNotifications.Count;
            listTarget += activeNotifications.Count;
        }
        ListProgress = newProgress;
        //GD.Print(ListProgress);
    }

    public override void _GuiInput(InputEvent @event)
    {
        if(@event is InputEventMouseButton btnEvent)
        {
            if(btnEvent.ButtonIndex == MouseButton.WheelUp && btnEvent.Pressed)
            {
                ShiftTarget(1);
                GetViewport().SetInputAsHandled();
            }

            if (btnEvent.ButtonIndex == MouseButton.WheelDown && btnEvent.Pressed)
            {
                ShiftTarget(-1);
                GetViewport().SetInputAsHandled();
            }
        }
    }

    public void ShiftTarget(int changeAmount)
    {
        if (isListChanging || appendQueued || activeNotifications.Count <= 1 || Mathf.Abs(listTarget - ListProgress) > 5)
            return;
        listTarget += changeAmount;
    }

    [Export]
    int currentListIdx = 0;
    [Export]
    int listTarget;

    [Export]
    float _listProgress;
    float ListProgress
    {
        get => _listProgress;
        set
        {
            _listProgress = value;
            //animate
            currentListIdx = Mathf.FloorToInt(value);

            var offset = value % 1;
            if (offset < 0)
                offset += 1;
            for (int i = 0; i < notificationInstances.Length; i++)
            {
                int rawDataIdx = currentListIdx - (i - 1);
                int dataIdx = LoopDataIdx(rawDataIdx);
                notificationInstances[i].Visible =
                    (allowOutOfRange || (activeNotifications.Count > rawDataIdx && rawDataIdx >= 0)) &&
                    activeNotifications.Count > dataIdx &&
                    (i != 0 || offset > 0.5) &&
                    (i <= 1 || activeNotifications.Count > 1);
                if (!notificationInstances[i].Visible)
                {
                    notificationInstances[i].SetNotifData(null);
                    notificationInstances[i].SetNotifInteractable(false);
                    continue;
                }
                notificationInstances[i].SetNotifData(activeNotifications[dataIdx]);
                notificationInstances[i].SetNotifInteractable(i == 1 && offset < 0.1);
                float thisOffset = offset + (i - 1);
                if (thisOffset > 1 && activeNotifications.Count == 2)
                {
                    thisOffset = 1 + (thisOffset - 1) * 2;
                }
                notificationInstances[i].Stage = thisOffset;
            }
        }
    }

    int LoopDataIdx(int index)
    {
        if (activeNotifications.Count == 0)
            return -1;
        while (index < 0)
            index += activeNotifications.Count;
        while (index >= activeNotifications.Count)
            index -= activeNotifications.Count;
        return index;
    }

    static NotificationDataContainer WrapData(NotificationData _data) => new(_data);
    public class NotificationDataContainer(NotificationData _data)
    {
        public readonly NotificationData data = _data;
        public bool unread { get; private set; } = true;
        public bool audioPlayed { get; set; }
        public void MarkAsRead() =>
            unread = false;
    }
}

public record struct NotificationData()
{
    public string header;
    public string body;
    public DateTime expires = DateTime.MaxValue;
    public bool urgent = false;

    public NotificationItemData[] items = [];

    public Texture2D icon;
    public Color itemColor;
    public float animDuration = 0.35f;
    public int flipbookLength = 0;
    public Vector2I flipbookSlice;

	public AudioStream sound;
    public bool useSound = true;

    public string firstAction;
    public string secondAction;
    public string superAction;
    public Action<string> OnAction;//first, second, super

    public void SubmitAction(string action)=>
        OnAction?.Invoke(action);
}

public record struct NotificationItemData()
{
    public string label;
    public string displayTemplate;
    public int displayAmount;
    public int displayPower;

    public string linkedItemId;

    public string linkedMissionId;

    public string linkedOfferId;
}