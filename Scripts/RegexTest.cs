using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

public partial class RegexTest : Control
{
    [Export]
    LineEdit source;
    [Export]
    RichTextLabel result;

    public override void _Ready()
    {
        base._Ready();
        //GD.Print($"LogicTest: {true && false || true}");
        //GD.Print($"LogicTest: {true && false || true && true}");
        //GD.Print($"LogicTest: {true && false || true && false || true && true}");
        source.TextSubmitted += EvaluateRegex;
    }

    private void EvaluateRegex(string newText)
    {
        var instructions = PLSearch.GenerateSearchInstructions(newText, out var failText);
        if (instructions is not null)
        {
            PLSearch.EvaluateInstructions(instructions, searchObjTest, out var resultText);
            result.Text = resultText;
        }
        else
        {
            result.Text = failText;
        }
    }

    static JsonObject searchObjTest = new()
    {
        ["foo"] = 5,
        ["bar"] = 10,
        ["test"] = "sussy",
        ["subObj"] = new JsonObject
        {
            ["oof"] = 8,
            ["rab"] = 8,
            ["bleeb"] = "amogus",
        },
        ["subArr"] = new JsonArray
        {
            "first",
            "second",
            "third"
        }
    };
}
