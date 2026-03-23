using Godot;
using Godot.Collections;
using System;

//what am i even doing here...
public partial class JsonHighlighter : SyntaxHighlighter
{
    static readonly Color[] depthCols = new Color[] { Colors.Yellow, Colors.Purple, Colors.Blue };
    Dictionary<int, int> startingDepths = [];
    public override Dictionary _GetLineSyntaxHighlighting(int line)
    {
        var lineText = GetTextEdit().GetLine(line);
        Dictionary<int, Color> lineColors = [];
        Color topColor = Colors.White;
        int currentDepth = 0;
        if (line > 0 && startingDepths.ContainsKey(line - 1))
            currentDepth = startingDepths[line - 1];
        for (int i = 0; i < lineText.Length; i++)
        {
            var character = lineText[i];

            bool addDepth = "{[".Contains(character);
            bool removeDepth = "}]".Contains(character);
            if (addDepth || removeDepth)
            {
                var depthCol = depthCols[currentDepth % depthCols.Length];
                if (topColor != depthCol)
                {
                    topColor = depthCol;
                    lineColors.Add(i, topColor);
                }
                if (addDepth)
                    currentDepth++;
                else if (removeDepth)
                    currentDepth--;
                continue;
            }

            if (topColor != Colors.White)
            {
                topColor = Colors.White;
                lineColors.Add(i, topColor);
            }

        }
        return base._GetLineSyntaxHighlighting(line);
    }
}
