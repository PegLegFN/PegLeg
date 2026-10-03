using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

public partial class FileDistributer : Node
{
	static FileDistributer instance;
	[Export]
	FileDialog fileDialog;

	public override void _Ready()
	{
		instance = this;
		fileDialog.FileSelected += SaveFile;
	}

	static bool fileHandlingQueued = false;
	public override void _Process(double delta)
	{
		if (fileHandlingQueued)
		{
			fileHandlingQueued = false;
			TryHandleFiles();
		}
	}

	static List<PegLegDataFile> unhandledFiles = [];
	static Dictionary<string, Func<JsonElement, bool>> fileHandlers = [];

	public static void RegisterFileHandler<T>(string targetType, Func<T, bool> handler, JsonSerializerOptions options = null)
	{
		if (fileHandlers.ContainsKey(targetType))
		{
			GD.PushWarning($"Duplicate file handler failed for: {targetType}");
			return;
		}
		fileHandlers[targetType] = element =>
		{
			try
			{
				if (element.Deserialize<T>(options) is T typed)
					return handler.Invoke(typed);
			}
			catch { }
			return false;
		};
		TryHandleFiles();
	}

	public static void TryAddFileFromPath(string filePath)
	{
		try
		{
			if (!filePath.EndsWith(".pegleg"))
			{
				GD.PushWarning($"Tried to load a file that wasnt a Data File at \"{filePath}\"".FixNewlines());
				return;
			}
			if (!FileAccess.FileExists(filePath))
			{
				GD.PushWarning($"Data File doesn't exist at \"{filePath}\"");
				return;
			}
			using var peglegDataFile = FileAccess.Open(filePath, FileAccess.ModeFlags.Read);
			AddFile(JsonSerializer.Deserialize<PegLegDataFile>(peglegDataFile.GetAsText()));
		}
		catch(Exception e)
		{
			GD.PushWarning($"Failed to load Data File at \"{filePath}\":\n{e}".FixNewlines());
		}
	}

	public static void AddFile(PegLegDataFile file)
	{
		unhandledFiles.Add(file);
		fileHandlingQueued = true;
	}

	static bool isHandling;
	public static async void TryHandleFiles()
	{
		if (isHandling)
			return;
		isHandling = true;
		await Helpers.WaitForFrame();
		isHandling = false;

		if (unhandledFiles.Count == 0)
			return;
		List<PegLegDataFile> handled = [];
		foreach (var file in unhandledFiles)
		{
			if (!fileHandlers.TryGetValue(file.type, out var handler))
				continue;
			if (handler?.Invoke(file.content) == true)
				handled.Add(file);
		}
		if (handled.Count == 0)
			return;
		unhandledFiles = [.. unhandledFiles.Except(handled)];
	}

	//data container for .pegleg files
	public class PegLegDataFile
	{
		public string type { get; init; }
		public int version { get; init; } = 1;
		public JsonElement content { get; init; }
	}

	static PegLegDataFile savableFile;
	public static void SaveFile<T>(string type, T content, string filename=null, int version = 1, JsonSerializerOptions options = null)
	{
		if (instance is null)
			return;
		savableFile = new()
		{
			type = type,
			version = version,
			content = JsonSerializer.SerializeToElement(content, options)
		};
		instance.fileDialog.CurrentFile = filename;
		instance.fileDialog.Show();
	}

	private void SaveFile(string path)
	{
		using var destination = FileAccess.Open(path, FileAccess.ModeFlags.Write);
		destination.StoreString(JsonSerializer.Serialize(savableFile, Helpers.JsonOptions.Indented));
	}
}
