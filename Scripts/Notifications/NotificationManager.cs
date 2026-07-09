using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

public partial class AppConfig
{
	public partial class NotificationConfig
	{
		public float scale = 1;
		public bool showTrayTutorial = true;
	}

	public partial class ExperimentalConfig
	{
		public bool notifications = true;
	}
}

public partial class NotificationManager : Control
{
	[Export]
	NotificationInstance[] notificationInstances;
	[Export]
	Control mouseBlocker;
	public List<NotificationDataContainer> activeNotifications = [];

	static Queue<NotificationData> notificationQueue = new();
	static NotificationManager instance;

	public static void PushOne(NotificationData data) => Push([data]);
	public static void Push(IEnumerable<NotificationData> data)
	{
		foreach (var item in data)
		{
			notificationQueue.Enqueue(item);
		}

		if (instance is null)
			return;

		if (notificationQueue.Count > 0)
			instance.queueTimer.Start();
	}

	Window window;
	Vector2I baseSize;
	Timer queueTimer;
	const string notificationWindowTag = "NotifWindow";
	public override void _Ready()
	{
		window = GetWindow();
		UpdateWindowTangibility();
		baseSize = window.Size;
		window.Visible = AppConfig.Get("experimental", "notifications", false);
		SetScale((float)AppConfig.Get("notification", "scale", 1.0));

		foreach (var notification in notificationInstances)
		{
			notification.OnDismiss += DismissCurrent;
		}
		instance = this;
		queueTimer = new() { WaitTime = 0.5, OneShot = true, Autostart = false };
		AddChild(queueTimer);
		if (notificationQueue.Count > 0)
			queueTimer.Start();
		queueTimer.Timeout += AppendFromQueue;
		ListProgress = _listProgress;
		AppConfig.OnConfigChanged += OnConfigChanged;
		MouseEntered += CheckTangibility;
		window.FocusEntered += CheckTangibility;
		RefreshTimerController.OnSecondChanged += ClearExpired;
	}

	public void CheckIfFullscreen()
	{
		//use win32 api to get focused window handle
		//If focused window is on the same display as
		// notifications and is the same size as the
		// screen, move notifications to another screen
		// or hide them
		//An exception is provided for if the current process
		// is Fortnite, in which we can move the notification
		// window to the bottom center, out of the way of the HUD
	}

	static readonly Vector2[] fullPassthrough = new Vector2[2];
	bool IsWindowVisible => window.Visible && window.MousePassthroughPolygon.Length == 0;
	void UpdateWindowTangibility()
	{
		//window.Unfocusable = activeNotifications.Count == 0;
		window.MousePassthroughPolygon = activeNotifications.Count == 0 ? fullPassthrough : [];
		//window.Unfocusable = activeNotifications.Count == 0;
	}


	private void CheckTangibility()
	{
		if ((window.MousePassthroughPolygon.Length == 0) == (activeNotifications.Count == 0))
			GD.Print("Mismatching notification window state. Correcting...");
		UpdateWindowTangibility();
	}

	private void OnConfigChanged(string section, string key, JsonNode node)
	{
		if (node is not JsonValue value)
			return;
		if (section == "experimental" && key == "notifications" && value.TryGetValue(out bool notifsVisible))
			window.Visible = notifsVisible;
		if (section == "notification" && key == "scale")
			SetScale(value.TryGetValue(out double scale) ? (float)scale : 1);
	}

	public override void _ExitTree()
	{
		AppConfig.OnConfigChanged -= OnConfigChanged;
		RefreshTimerController.OnSecondChanged -= ClearExpired;
		instance = null;
	}

	void SetScale(float scaleAmt)
	{
		var newSize = (Vector2I)((Vector2)baseSize * scaleAmt);
		window.ContentScaleFactor = scaleAmt;
		//the window size is acting weird... perhaps one of the aspect ratio containers is messing things up?
		for (int i = 0; i < 15; i++)
		{
			window.Size = newSize;
			var safeRect = DisplayServer.ScreenGetUsableRect();
			window.Position = safeRect.Position + safeRect.Size - (newSize + new Vector2I(5, 5 - (int)(9 * scaleAmt)));
			//await Helpers.WaitForFrame();
		}
		//GD.Print(baseSize);
		//GD.Print(newSize);
		//GD.Print(safeRect);
		//GD.Print(window.Size);
		//GD.Print(scaleAmt);
	}

	bool isAppendQueued = false;
	bool isListChanging = false;
	bool allowOutOfRange = false;
	async void AppendFromQueue()
	{
		if (listTarget != _listProgress || isListChanging)
		{
			isAppendQueued = true;
			instance.queueTimer.Start(0.1);
			return;
		}
		isAppendQueued = false;
		//show window if not already visible
		if (activeNotifications.Count == 0)
		{
			//TODO: move window to first display in which there are no boarderless fullscreen windows
			window.MousePassthroughPolygon = [];
			TrayIcon.RegisterResponsiveWindow(notificationWindowTag);
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
				listTarget = activeNotifications.Count - 1;
				GD.Print($"p:{_listProgress}, t:{listTarget}");

				//animate urgent items
				if (urgent.Length > 1)
					foreach (var inst in notificationInstances)
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

			if (nonUrgent.Length > 0)
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

	bool isClearQueued;
	async void ClearExpired()
	{
		if (isClearQueued)
			return;
		isClearQueued = true;
		while (listTarget != _listProgress || isAppendQueued || isListChanging)
		{
			await Helpers.WaitForFrame();
		}
		isClearQueued = false;
		if (!activeNotifications.Any(n => n.data.HasExpired))
			return;
		isListChanging = true;

		ResetList();

		//loop through instances, store offsets of non-expired notifs for later
		List<float> offsets = [];
		for (int i = 0; i < activeNotifications.Count; i++)
		{
			if (!activeNotifications[i].data.HasExpired)
				offsets.Add((activeNotifications.Count - 1) - i);
		}
		offsets.Reverse();

		//animate scale of expired notifs
		for (int i = 0; i < notificationInstances.Length; i++)
		{
			if (notificationInstances[i].CurrentData?.HasExpired != true)
				continue;
			notificationInstances[i].freezeAnim = true;
			var scaleTween = notificationInstances[i].CreateTween();
			scaleTween.TweenProperty(notificationInstances[i], "scale", Vector2.Zero, 0.1);
		}

		await Helpers.WaitForTimer(0.1);
		await Helpers.WaitForFrames(2);

		activeNotifications = [.. activeNotifications.Where(n => !n.data.HasExpired)];

		//if notifs are empty, hide list and return

		ReassignInstances();

		//restore the offsets of non-expired notifs
		//animate offsets back down
		int animCount = Mathf.Min(notificationInstances.Length - 1, offsets.Count);
		for (int i = 0; i < animCount; i++)
		{
			GD.Print($"{offsets[i]}=>{i}");
			notificationInstances[i + 1].AnimateStage(i, 0.15, 0.05 * i, offsets[i]);
		}
		if (animCount > 0)
			await Helpers.WaitForTimer(0.1 + (animCount * 0.05));

		for (int i = 0; i < notificationInstances.Length; i++)
		{
			notificationInstances[i].freezeAnim = false;
		}

		isListChanging = false;
	}

	void ResetList()
	{
		if (currentListIdx == activeNotifications.Count || currentListIdx == -1)
			return;
		//make foreground notification be at last index
		var beforeCurrent = activeNotifications[..(currentListIdx + 1)];
		activeNotifications.RemoveRange(0, currentListIdx + 1);
		activeNotifications.AddRange(beforeCurrent);
		_listProgress = listTarget = currentListIdx = activeNotifications.Count - 1;
	}

	void ReassignInstances()
	{
		for (int i = 0; i < notificationInstances.Length; i++)
		{
			notificationInstances[i].Visible = false;
			notificationInstances[i].SetNotifData(null);
			notificationInstances[i].SetNotifInteractable(false);
		}
		for (int i = 1; i < notificationInstances.Length; i++)
		{
			if (i > activeNotifications.Count)
				break;
			GD.Print($"Set {i} to {activeNotifications[^i].data.header}");
			notificationInstances[i].Visible = true;
			notificationInstances[i].SetNotifData(activeNotifications[^i]);
			notificationInstances[i].SetNotifInteractable(i == 1);
		}
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
			UpdateWindowTangibility();
			TrayIcon.UnregisterResponsiveWindow(notificationWindowTag);
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
			newProgress -= activeNotifications.Count;
			listTarget -= activeNotifications.Count;
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
		if (@event is InputEventMouseButton btnEvent)
		{
			if (btnEvent.ButtonIndex == MouseButton.WheelUp && btnEvent.Pressed)
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
		if (isListChanging || isAppendQueued || activeNotifications.Count <= 1 || Mathf.Abs(listTarget - ListProgress) > 5)
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
	public bool HasExpired => DateTime.UtcNow > expires;
	public bool urgent = false;

	public NotificationItemData[] items = [];
	public string itemPrefix;
	public NotificationItemData[] secondaryItems = [];
	public string secondaryItemPrefix;

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
	public Func<NotifAction, bool> HandleAction;//first, second, super

	public bool SubmitAction(NotifAction action)
	{
		if (HandleAction is null)
		{
			GD.Print("No Handler");
			return false;
		}
		return HandleAction.Invoke(action);
	}

}
public enum NotifAction
{
	FirstAction,
	SecondAction,
	SuperAction,
}

public record class NotificationItemData()
{
	public string powerTooltip;
	public string powerLabel;
	public GameItem item;
}