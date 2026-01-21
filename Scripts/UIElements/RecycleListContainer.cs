using Godot;
using System.Collections.Generic;

public partial class RecycleListContainer : ScrollContainer
{
    [Export]
    float elementSpace;
    [Export]
    DynamicGridContainer linkedGrid;
    [Export]
    Control offsetControl;
    [Export]
    Control paddingControl;
    [Export]
    Control elementParent;
    [Export]
    PackedScene elementScene;
    [Export]
    bool dumbMode;

    IRecyclableEntry basis;
    Vector2 basisSize;
    Queue<IRecyclableEntry> pooledEntries = new();
    Dictionary<int, IRecyclableEntry> activeEntries = [];
    IRecyclableElementProvider linkedProvider;

    public override async void _Ready()
    {
        base._Ready();

        basis = elementScene.Instantiate<IRecyclableEntry>();
        await Helpers.WaitForFrame();
        basis.node.Visible = false;
        basis.node.Size = Vector2.Zero;
        basisSize = basis.BasisSize;

        ItemRectChanged += CheckForListUpdate;
        GetChild<Control>(0).ItemRectChanged += CheckForListUpdate;
        //GD.Print($"basis {basisSize} initialised from {basis.node.Name}");

        //SetProvider(new TestRecyclableElementProvider());
    }

    public void SetProvider(IRecyclableElementProvider provider)
    {
        linkedProvider = provider;
        for (int i = 0; i < activeEntries.Count; i++)
        {
            activeEntries[i].SetRecyclableElementProvider(provider);
        }
        UpdateList(true);
    }

    int lastStartingIndex = 0;
    int lastOnScreenElements = 0;
    bool lockList = false;
    public void UpdateList() => UpdateList(false);
    public void UpdateList(bool force)
    {
        if(linkedProvider is null)
        {
            GD.PushWarning("no linked provider in recyclable list");
            return;
        }
        if ((!IsVisibleInTree() && !force) || lockList)
            return;
        lockList = true;
        try
        {
            var elementSize = basis.BasisSize;
            if ((linkedGrid?.GetChildCount() ?? 0) > 0)
                elementSize = linkedGrid.GetFirstControlChild().GetCombinedMinimumSize();
            float elementHeight = elementSize.Y;
            int columns = linkedGrid?.GetColCount(elementSize.X) ?? 1;

            //calculate how many elements fit on screen (cols*(ceil(heightOfElement/heightOfThis)+1))
            int totalElements = linkedProvider.GetRecycleElementCount();
            int newOnScreenElements = columns * (Mathf.CeilToInt(Size.Y / (elementHeight + elementSpace)) + 2);

            if (dumbMode)
                newOnScreenElements = totalElements;

            //use scrollVertical and elementParent y pos to get the offset height
            float offsetHeight = (GlobalPosition.Y - elementParent.GlobalPosition.Y) + offsetControl.Size.Y;
            int fakeElements = totalElements - newOnScreenElements;
            int maxFakeRows = Mathf.Max(Mathf.CeilToInt((float)fakeElements / columns), 0);
            int startingRow = Mathf.Clamp(Mathf.FloorToInt(offsetHeight / (elementHeight + elementSpace)), 0, maxFakeRows);
            offsetHeight = startingRow * (elementHeight + elementSpace);
            //GD.Print($"offsetCols: {offsetHeight / (elementHeight + elementSpace)}");
            offsetControl.CustomMinimumSize = new(0, offsetHeight);

            //set the indexes of the elements
            int newStartingIndex = startingRow * columns;
            if (lastStartingIndex != newStartingIndex || lastOnScreenElements != newOnScreenElements || force)
            {
                //GD.Print($"relinking ({lastStartingIndex}->{newStartingIndex}) ({lastOnScreenElements}->{newOnScreenElements}) (force:{force})");
                //GD.Print($"activeEntries: {activeEntries.Count}");
                //GD.Print($"basis size: ({elementSize.X}x{elementSize.Y}) collumns: {columns}");
                //GD.Print($"visible area: ({Size.X}x{Size.Y})");
                if (activeEntries.Count > lastOnScreenElements)
                    GD.PushWarning("what happen?");
                int lastEndIndex = Mathf.Min(lastStartingIndex + lastOnScreenElements, totalElements);
                int newEndIndex = Mathf.Min(newStartingIndex + newOnScreenElements, totalElements);

                if (lastStartingIndex>=newEndIndex || lastEndIndex<=newStartingIndex)
                {
                    force = true;
                }

                linkedGrid?.SetDisableSort(true);
                if (force)
                {
                    //complete clear and relink
                    foreach (var item in activeEntries)
                    {
                        //GD.Print("force clearing " + item.Key);
                        item.Value.ClearRecycleIndex();
                        pooledEntries.Enqueue(item.Value);
                        item.Value.node.Visible = false;
                    }
                    activeEntries.Clear();
                    //GD.Print($"forcing from {newStartingIndex} to {newEndIndex}");
                    for (int i = newStartingIndex; i < newEndIndex; i++)
                    {
                        //GD.Print("force adding " + i);
                        var item = pooledEntries.Count > 0 ? pooledEntries.Dequeue() : SpawnPoolEntry();
                        activeEntries[i] = item;
                        activeEntries[i].node.Visible = true;
                        activeEntries[i].SetRecycleIndex(i);
                        activeEntries[i].node.MoveToFront();
                    }
                }
                else
                {
                    if (lastStartingIndex <= newStartingIndex)
                    {
                        //collect elements that went offscreen
                        for (int i = lastStartingIndex; i < newStartingIndex; i++)
                        {
                            //GD.Print("removing " + i);
                            if (!activeEntries.TryGetValue(i, out var item))
                                continue;
                            activeEntries[i].ClearRecycleIndex();
                            pooledEntries.Enqueue(item);
                            activeEntries.Remove(i);
                            item.node.Visible = false;
                        }
                    }
                    if (lastEndIndex >= newEndIndex)
                    {
                        //collect elements that went offscreen
                        for (int i = newEndIndex; i < lastEndIndex; i++)
                        {
                            //GD.Print("removing " + i);
                            if (!activeEntries.TryGetValue(i, out var item))
                                continue;
                            activeEntries[i].ClearRecycleIndex();
                            pooledEntries.Enqueue(item);
                            activeEntries.Remove(i);
                            item.node.Visible = false;
                        }
                    }

                    if (lastStartingIndex >= newStartingIndex)
                    {
                        //deploy elements and send them to back
                        for (int i = lastStartingIndex - 1; i > newStartingIndex - 1; i--)
                        {
                            //GD.Print("adding " + i);
                            var item = pooledEntries.Count > 0 ? pooledEntries.Dequeue() : SpawnPoolEntry();
                            activeEntries[i] = item;
                            activeEntries[i].node.Visible = true;
                            activeEntries[i].SetRecycleIndex(i);
                            elementParent.MoveChild(activeEntries[i].node, 0);
                        }
                    }
                    if (lastEndIndex <= newEndIndex)
                    {
                        //deploy elements and send them to front
                        for (int i = lastEndIndex; i < newEndIndex; i++)
                        {
                            //GD.Print("adding " + i);
                            var item = pooledEntries.Count > 0 ? pooledEntries.Dequeue() : SpawnPoolEntry();
                            activeEntries[i] = item;
                            activeEntries[i].node.Visible = true;
                            activeEntries[i].SetRecycleIndex(i);
                            activeEntries[i].node.MoveToFront();
                        }
                    }
                }
                linkedGrid?.SetDisableSort(false);

                lastStartingIndex = newStartingIndex;
                lastOnScreenElements = newOnScreenElements;

            }

            //pad out the remaining space

            int remainingElements = totalElements - (newStartingIndex + newOnScreenElements);
            int remainingRows = Mathf.Max(Mathf.CeilToInt((float)remainingElements / columns), 0);
            float paddingHeight = remainingRows * (elementHeight + elementSpace);
            //GD.Print($"paddingCols: {paddingHeight / (elementHeight+elementSpace)}");
            paddingControl.CustomMinimumSize = new(0, paddingHeight);
            Visible = true;
        }
        finally
        {
            lockList = false;   
        }
    }

    IRecyclableEntry SpawnPoolEntry()
    {
        var spawnedEntry = elementScene.Instantiate<IRecyclableEntry>();
        linkedProvider.OnElementSpawned(spawnedEntry);
        spawnedEntry.SetRecyclableElementProvider(linkedProvider);
        elementParent.AddChild(spawnedEntry.node);
        return spawnedEntry;
    }

    int lastScroll;
    Vector2 lastSize;

    async void CheckForListUpdate()
    {
        await Helpers.WaitForFrame();
        UpdateList();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (lastSize != Size || lastScroll != ScrollVertical)
            UpdateList();
        lastSize = Size;
        lastScroll = ScrollVertical;
        //if pooled entries is more than the length of active entries (plus a buffer of 20), remove 1 pooled entry per physics frame
        if (pooledEntries.Count > activeEntries.Count + 20)
        {
            var toFree = pooledEntries.Dequeue();
            toFree.node.QueueFree();
        }
    }
}

public class TestRecyclableElementProvider : IRecyclableElementProvider
{
    public int GetRecycleElementCount()
    {
        return 25;
    }
}

public interface IRecyclableEntry
{
    public Control node { get; }
    public Vector2 BasisSize => node.Size;

    public void SetRecyclableElementProvider(IRecyclableElementProvider provider);
    public void SetRecycleIndex(int index);
    public void ClearRecycleIndex() { }
}

public interface IRecyclableElementProvider 
{
    public int GetRecycleElementCount();
    public void OnElementSpawned(IRecyclableEntry entry) { }
    public void OnElementSelected(int index, string context = "") { }
}

public interface IRecyclableElementProvider<T> : IRecyclableElementProvider
{
    public T GetRecycleElement(int index);
}

public interface ISelectableElementProvider<T>
{
    public bool IsSelected(T value);
    public bool IsSelectable(T value) => true;

    public Color GetSelectableColor(T value) => IsSelectable(value) ? Colors.Red : (IsSelected(value) ? Colors.Green : Colors.Transparent);
    public Texture2D GetSelectableIcon(T value) => null;
}
