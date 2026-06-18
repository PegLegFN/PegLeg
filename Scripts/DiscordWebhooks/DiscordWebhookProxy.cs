using Godot;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

public partial class DiscordWebhookProxy
{
	static Dictionary<string, DiscordWebhookProxy> proxyDict = [];

	static Uri discordAddress = new("https://discord.com");

	public static bool TryGetProxy(string internalId, out DiscordWebhookProxy proxy) => proxyDict.TryGetValue(internalId, out proxy);

	string displayName;
	public string InternalName { get; private set; }
	Func<Task<string>> contentProvider;
	Func<Task<Image[]>> imageProvider;
	Func<Task<string[]>> filepathProvider;
	string uuid = Guid.NewGuid().ToString();

	public DiscordWebhookProxy(string displayName, string internalName, Func<Task<string>> contentProvider = null, Func<Task<string[]>> filepathProvider = null, Func<Task<Image[]>> imageProvider = null)
	{
		this.displayName = displayName;
		InternalName = internalName;
		this.contentProvider = contentProvider;
		this.filepathProvider = filepathProvider;
		this.imageProvider = imageProvider;
		proxyDict[internalName] = this;
	}

	public async Task CreateSyncMessage()
	{
		if (!AppConfig.Get("advanced", "webhooks", false))
			return;
		if (!AppConfig.Get("webhooks", InternalName + "_enabled", false))
			return;
		var urlEnding = AppConfig.Get("webhooks", InternalName + "_url", "");
		if (!UrlRegex().Match(urlEnding).Success)
		{
			GD.Print($"WH url failed: \"{urlEnding}\"");
			return;
		}
		var syncThreadId = AppConfig.Get("webhooks", InternalName + "_syncThread", "");

		var executionResponse = await discordAddress
			.MakeRequest($"/api/webhooks/{urlEnding}?wait=true&thread_id={syncThreadId}", HttpMethod.Post)
			.SetJsonContent($$"""
            {
                "username": "Sync: {{displayName}}", 
                "content": "Blank Sync Message"
            }
            """)
			.Send();
		if (await executionResponse.CheckForError())
		{
			GD.Print($"WH execution response failed");
			return;
		}
		var executionJson = await executionResponse.ReadJson();
		if (executionJson?["id"]?.ToString() is not string syncId)
		{
			GD.Print($"WH message id does not exist (how did this happen?): \n{executionJson}");
			return;
		}
		GD.Print("WH sync message created");
		AppConfig.Set("webhooks", InternalName + "_sync", syncId);
	}

	public bool UsesSync => AppConfig.Get("webhooks", InternalName + "_useSync", false);

	public bool IsEnabled => AppConfig.Get("advanced", "webhooks", false) && AppConfig.Get("webhooks", InternalName + "_enabled", false);

	public async Task Execute(
		Func<Task<string>> currentContentProvider = null,
		Func<Task<string[]>> currentFilepathProvider = null,
		Func<Task<Image[]>> currentImageProvider = null
	) => await Execute(false, currentContentProvider, currentFilepathProvider, currentImageProvider);

	public async Task Execute(
		bool skipSync,
		Func<Task<string>> currentContentProvider = null,
		Func<Task<string[]>> currentFilepathProvider = null,
		Func<Task<Image[]>> currentImageProvider = null
	)
	{
		if (!IsEnabled)
			return;

		var urlEnding = AppConfig.Get("webhooks", InternalName + "_url", "");
		if (urlEnding.StartsWith("https://discord.com/api/webhooks/"))
		{
			urlEnding = urlEnding[33..];
			AppConfig.Set("webhooks", InternalName + "_url", urlEnding);
		}
		if (!UrlRegex().Match(urlEnding).Success)
		{
			GD.Print($"WH: URL ending failed to validate: \"{urlEnding}\"");
			return;
		}

		if (AppConfig.Get("webhooks", InternalName + "_useSync", false) && !skipSync)
		{
			var syncId = AppConfig.Get("webhooks", InternalName + "_sync", "");
			var syncThreadId = AppConfig.Get("webhooks", InternalName + "_syncThread", "");
			var editResponse = await discordAddress
				.MakeRequest($"/api/webhooks/{urlEnding}/messages/{syncId}?wait=true&thread_id={syncThreadId}", HttpMethod.Patch)
				.SetJsonContent($$"""
                {
                    "content": "{{uuid}}"
                }
                """)
				.Send();
			if (await editResponse.CheckForError())
			{
				GD.Print($"WH: edit response failed");
				return;
			}
			await Task.Delay(1000);
			var winnerResponse = await discordAddress
				.MakeRequest($"/api/webhooks/{urlEnding}/messages/{syncId}?thread_id={syncThreadId}", HttpMethod.Get)
				.Send();
			if (await winnerResponse.CheckForError())
			{
				GD.Print($"WH: winner response failed");
				return;
			}
			var winnerJson = await winnerResponse.ReadJson();
			if (winnerJson["content"]?.ToString() != uuid)
			{
				GD.Print($"WH: did not win (\"{winnerJson["content"]?.ToString()}\" != \"{uuid}\")");
				return;
			}
			//GD.Print("WH: winner");
		}

		currentContentProvider ??= contentProvider;
		currentFilepathProvider ??= filepathProvider;
		currentImageProvider ??= imageProvider;

		string content = null;
		if (currentContentProvider?.Invoke() is Task<string> contentTask)
			content = await contentTask;

		string[] filepaths = [];
		if (currentFilepathProvider?.Invoke() is Task<string[]> filepathTask)
			filepaths = await filepathTask;

		Image[] images = [];
		if (currentImageProvider?.Invoke() is Task<Image[]> imageTask)
			images = await imageTask;

		if (filepaths.Length == 0 && images.Length == 0 && content == null)
		{
			GD.Print($"WH: no content");
			return;
		}

		MultipartFormDataContent formContent = [];
		int fileIdx = 0;
		formContent.AddStringContent("username", displayName);
		if (content != null)
			formContent.AddStringContent("content", content);
		for (int i = 0; i < filepaths.Length; i++)
		{
			if (!FileAccess.FileExists(filepaths[i]))
				continue;
			string filename = filepaths[i].Split("/")[^1];
			string fileContent = "";
			using (var targetFile = FileAccess.Open(filepaths[i], FileAccess.ModeFlags.Read))
				fileContent = targetFile.GetAsText();
			formContent.AddTextFileContent($"files[{fileIdx}]", fileContent, filename);
			fileIdx++;
		}

		for (int i = 0; i < images.Length; i++)
		{
			string filename = images[i].ResourceName;
			if (string.IsNullOrEmpty(filename))
				filename = "image";
			formContent.AddImageContent($"files[{fileIdx}]", images[i], filename);
			fileIdx++;
		}

		if (fileIdx == 0 && content == null)
		{
			GD.Print($"WH: no valid content");
			return;
		}

		//var stringContent = await formContent.ReadAsStringAsync();

		var executionResponse = await discordAddress
			.MakeRequest($"/api/webhooks/{urlEnding}", HttpMethod.Post)
			.SetContent(formContent)
			.Send();
		if (await executionResponse.CheckForError())
		{
			GD.Print($"WH: execution response failed");
			return;
		}
		GD.Print($"WH: executed successfully");
	}

	[GeneratedRegex("^\\d*/[\\w_\\-]*$")]
	private static partial Regex UrlRegex();
}
