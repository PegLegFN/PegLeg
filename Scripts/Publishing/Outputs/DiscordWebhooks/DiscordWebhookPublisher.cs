using Godot;
using System;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

public partial class DiscordWebhookPublisher : IPublisher
{
	string IPublisher.PlatformId => "Discord";

	static Uri discordAddress = new("https://discord.com");

	string displayName;
	public string InternalName { get; private set; }

	void IPublisher.Configure(PublisherConfig config)
	{
		InternalName = config.identifier;
		displayName = config.displayName;
	}

	async Task IPublisher.AttemptPublish(PublisherContent publisherContent)
	{
		if (!AppConfig.Get("webhooks", InternalName + "_enabled", false))
			return;
		var urlEnding = AppConfig.Get("webhooks", InternalName + "_url", "");
		if (urlEnding.StartsWith("https://discord.com/api/webhooks/"))
		{
			urlEnding = urlEnding[33..];
			AppConfig.Set("webhooks", InternalName + "_url", urlEnding);
		}
		if (!UrlRegex().IsMatch(urlEnding))
		{
			GD.Print($"WH: URL ending failed to validate: \"{urlEnding}\"");
			return;
		}

		var content = publisherContent.content;
		var files = publisherContent.files ?? [];
		var images = publisherContent.images ?? [];

		if (content == null && files.Length == 0 && images.Length == 0)
		{
			GD.Print($"WH: no content");
			return;
		}

		MultipartFormDataContent formContent = [];
		formContent.AddStringContent("username", displayName);

		if (content != null)
			formContent.AddStringContent("content", content);

		int fileIdx = 0;
		for (int i = 0; i < files.Length; i++)
		{
			if (!FileAccess.FileExists(files[i]))
				continue;
			string filename = files[i].Split("/")[^1];
			string fileContent = "";
			using (var targetFile = FileAccess.Open(files[i], FileAccess.ModeFlags.Read))
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

		var publishResponse = await discordAddress
			.MakeRequest($"/api/webhooks/{urlEnding}", HttpMethod.Post)
			.SetContent(formContent)
			.Send();
		if (await publishResponse.CheckForError())
		{
			GD.Print($"WH: publish failed");
			return;
		}
		GD.Print($"WH: published successfully");
	}

	[GeneratedRegex("^\\d*/[\\w_\\-]*$")]
	private static partial Regex UrlRegex();
}
