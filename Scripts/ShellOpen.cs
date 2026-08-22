using Godot;
using System;

public partial class ShellOpen : Node
{
	[Export]
	string defaultURI;
	[Export]
	bool globalise;
	[Export]
	bool helperGlobalise;
	[Export]
	Node sourceNode;
	[Export]
	string sourceNodeProp;
	[Export]
	string sourceNodeFormat;


	public void ShellOpenDefault() => ShellOpenURI(defaultURI, globalise, helperGlobalise);
	public void ShellOpenNode()
	{
		if (sourceNode?.Get(sourceNodeProp).AsString() is not string input)
			return;
		if (!string.IsNullOrWhiteSpace(sourceNodeFormat))
			input = sourceNodeFormat.Replace("{0}", input);
		ShellOpenURI(input, globalise, helperGlobalise);
	}

	public void ShellOpenURI(string uri, bool globalise=false, bool helperGlobalise=false)
	{
		if (globalise)
		{
			if (helperGlobalise)
				uri = Helpers.GlobalisePath(uri);
			else
				uri = ProjectSettings.GlobalizePath(uri);
		}
		OS.ShellOpen(uri);
	}
}
