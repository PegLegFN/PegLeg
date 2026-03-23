using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

public partial class RewardNotifications : Node
{
	static RewardNotifications instance;
	public override void _Ready()
	{
		instance = this;
	}
	public record Request
    {
        public JsonObject itemInstance { get; private set; }
        public Action<JsonObject> onComplete {  get; private set; }
        Action<Request> onCancel;
        public Request(JsonObject itemInstance, Action<JsonObject> onComplete, Action<Request> onCancel)
        {
            this.itemInstance = itemInstance;
            this.onComplete = onComplete;
            this.onCancel = onCancel;
        }
        public void Cancel()
        {
            onComplete = null;
            onCancel?.Invoke(this);
        }
    }

	List<Request> itemQueue = [];
    Queue<Request> completedItems = new();

    Thread notifThread;
    public static Request RequestNotification(JsonObject itemInstance, Action<JsonObject> onComplete)
    {
        Request req = new(itemInstance, onComplete, r => instance.itemQueue.Remove(r));
        instance.itemQueue.Add(req);
        if (instance.notifThread is null)
        {
            instance.notifThread = new Thread(new ThreadStart(instance.ItemNotificationProcess));
            instance.notifThread.Start();
        }
        return req;
    }

    public static async Task EnqueueItemTask(JsonObject itemInstance, Action<JsonObject> onComplete, CancellationToken cancellationToken = default)
    {
        bool isComplete = false;
        RequestNotification(itemInstance, i => isComplete = true);
        while (!isComplete)
        {
            if (cancellationToken.IsCancellationRequested)
                return;
            await Helpers.WaitForFrame();
        }
        onComplete?.Invoke(itemInstance);
    }

    async void ItemNotificationProcess()
    {
        Request currentItem;
        await Helpers.WaitForFrame();
        while (true)
        {
            while (itemQueue.Count > 0)
            {
                lock (itemQueue)
                {
                    currentItem = itemQueue[0];
                    itemQueue.RemoveAt(0);
                }
                //await currentItem.itemInstance.SetItemRewardNotification();
                lock (completedItems)
                {
                    completedItems.Enqueue(currentItem);
                }
            }
            await Helpers.WaitForFrame();
            while (itemQueue.Count == 0)
            {
                await Task.Delay(50);
            }
            await Helpers.WaitForFrame();
        }
    }

    public override void _Process(double delta)
    {
        while (completedItems.Count>0)
        {
            var completedItem = completedItems.Dequeue();
            if (completedItem?.itemInstance is null)
                continue;
            completedItem.onComplete?.Invoke(completedItem.itemInstance);
        }
    }
}
