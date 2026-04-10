using Godot;
using System;
using System.Collections.Generic;

public interface IListEntry
{
	public Control Node => this is Control ctrl ? ctrl : null;
	public void SetListProvider(IListProvider provider);
	public void SetTargetListIndex(int index);
	public void ClearListEntry() { }
}

public interface IListEntry<T> : IListEntry
{
	int CurrentIndexTarget { get; protected set; }
	protected IListProvider<T> CurrentListProvider { get; set; }
	void IListEntry.SetListProvider(IListProvider provider)
	{
		if (CurrentListProvider == provider || provider is not IListProvider<T> typed)
			return;
		CurrentListProvider = typed;
	}

	void IListEntry.SetTargetListIndex(int index)
	{
		if (index >= 0)
			CurrentIndexTarget = index;
		if (CurrentListProvider is null)
		{
			ClearListEntry();
			return;
		}
		var list = CurrentListProvider.List;
		if (list.Count <= index)
		{
			ClearListEntry();
			return;
		}
		SetListEntryValue(list[index]);
	}

	public void SetListEntryValue(T newValue);
}

public class EntryList<T> : List<T>, IListProvider<T>
{
	public delegate void ItemSelected(int index, string context);
	public event ItemSelected OnItemSelectedEvt;
	public List<T> List => this;
	void IListProvider.OnItemSelected(int index, string context) => 
		OnItemSelectedEvt?.Invoke(index, context);
}

public interface IListProvider
{
	public int ListItemCount { get; }
	public void OnItemSelected(int index, string context = "") { }
}

public interface IListProvider<T> : IListProvider
{
	public List<T> List { get; }
	int IListProvider.ListItemCount => List.Count;
}
