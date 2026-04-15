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
		if (CurrentListProvider.List is IList<T> list)
			SetListEntryValue(list[CurrentIndexTarget]);
	}

	void IListEntry.SetTargetListIndex(int index)
	{
		if (index >= 0)
			CurrentIndexTarget = index;
		else
		{
			ClearListEntry();
			return;
		}
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
	public delegate void IndexSelected(int index, string context);
	public delegate void ItemSelected(T item, string context);

	public event IndexSelected OnIndexSelectedEvt;
	public event ItemSelected OnItemSelectedEvt;

	public IList<T> List => this;

	void IListProvider.OnItemSelected(int index, string context)
	{
		OnIndexSelectedEvt?.Invoke(index, context);
		OnItemSelectedEvt?.Invoke(this[index], context);
	}
}

public interface IListProvider
{
	public int ListItemCount { get; }
	public void OnItemSelected(int index, string context = "") { }
}

public interface IListProvider<T> : IListProvider
{
	public IList<T> List { get; }
	int IListProvider.ListItemCount => List.Count;
}

public interface IListHandler
{
	public void LinkListProvider(IListProvider listProvider);
	public void UpdateList() { }
}
