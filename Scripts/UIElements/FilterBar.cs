using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class FilterBar : Node
{
    [Signal]
    public delegate void FilterChangedEventHandler();

    [Export]
    bool allowMultiselect;
    [Export]
    bool invertMultiselectShift;
    [Export]
    Control filterButtonParent;

    Button[] filterButtons;

    public int FilterIndex => filterButtons?.FirstOrDefault(b => b.ButtonPressed)?.GetIndex() ?? -1;
    public bool[] FilterState => filterButtons?.Select(b => b.ButtonPressed).ToArray() ?? [];

    public override void _Ready()
    {
        var filterBtnNodes = filterButtonParent.GetChildCount();
        List<Button> filterBtns = [];
        for (int i = 0; i < filterBtnNodes; i++)
        {
            if (filterButtonParent.GetChild(i) is not Button btn)
                continue;
            filterBtns.Add(btn);
            int newIdx = filterBtns.Count - 1;
            btn.Pressed += () => OnFilterPressed(newIdx);
        }
        filterButtons = [.. filterBtns];
    }

    void OnFilterPressed(int idx)
    {
        bool targetState = !filterButtons[idx].ButtonPressed;
        if (Input.IsKeyPressed(Key.Alt) && allowMultiselect)
            targetState = false;
        bool addToSelection = allowMultiselect && (Input.IsKeyPressed(Key.Shift)==invertMultiselectShift);

        if (!addToSelection)
        {
            foreach (var btn in filterButtons)
            {
                btn.ButtonPressed = !targetState;
            }
        }
        filterButtons[idx].ButtonPressed = targetState;

        EmitSignalFilterChanged();
    }

    public void ResetFilter()
    {
        foreach (var btn in filterButtons)
        {
            btn.ButtonPressed = false;
        }
        if (!allowMultiselect)
            filterButtons[0].ButtonPressed = true;

        EmitSignalFilterChanged();
    }
}
