using Godot;
using System;
using System.Collections.Generic;

public interface IListEntry
{
	public Control Node => this is Control ctrl ? ctrl : null;
	public void SetListProvider(IListProvider provider);
	public void SetTargetListIndex(int index, bool force = false);
	public void ClearListEntry() { }
}

public interface IListEntry<T> : IListEntry
{
	int CurrentIndexTarget { get; protected set; }
	IListProvider<T> CurrentListProvider { get; protected set; }
	void IListEntry.SetListProvider(IListProvider provider)
	{
		if (CurrentListProvider == provider || provider is not IListProvider<T> typed)
			return;
		CurrentListProvider = typed;
		if (CurrentListProvider.List is IList<T> list)
			SetListEntryValue(list[CurrentIndexTarget]);
		else
			ClearListEntry();
	}

	void SelectEntry(string context = "") =>
		CurrentListProvider?.SelectItem(CurrentIndexTarget, context);

	void IListEntry.SetTargetListIndex(int index, bool force)
	{
		if (index < 0)
			return;
		if (CurrentIndexTarget == index && !force)
			return;
		CurrentIndexTarget = index;
		if (CurrentListProvider is null)
			return;
		var list = CurrentListProvider.List;
		if (index >= 0 && index < list.Count)
			SetListEntryValue(list[index]);
		else
			ClearListEntry();
	}

	protected void SetListEntryValue(T newValue);
	void IListEntry.ClearListEntry() => SetListEntryValue(default);
}

public class EntryList<T> : List<T>, IListProvider<T>
{
	public delegate void ItemSelected(int index, T item, string context);

	public event ItemSelected OnItemSelectedEvt;

	public IList<T> List => this;

	void IListProvider<T>.SelectItem(int index, T item, string context) => 
		OnItemSelectedEvt?.Invoke(index, item, context);
}

public interface IListProvider
{
	public int ListItemCount { get; }
	public void SelectItem(int index, string context = "") { }
}

public interface IListProvider<T> : IListProvider
{
	public IList<T> List { get; }
	int IListProvider.ListItemCount => List.Count;

	void IListProvider.SelectItem(int index, string context) =>
		SelectItem(index, List[index], context);
	public void SelectItem(int index, T item, string context) { }
}

public interface IListHandler
{
	public void LinkListProvider(IListProvider listProvider);
	public void UpdateList() { }
}