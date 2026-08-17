using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

public class PublisherProxy
{
	static Dictionary<string, PublisherProxy> existingProxies = [];
	public static PublisherProxy GetOrCreatePublisher(PublisherConfig config)
	{
		if(existingProxies.TryGetValue(config.identifier, out var existing))
			return existing;
		PublisherProxy newProxy = new(config);
		existingProxies.Add(config.identifier, newProxy);
		return newProxy;
	}

	string internalName;
	IPublisher[] publishers;
	PublisherProxy(PublisherConfig config)
	{
		internalName = config.identifier;
		publishers =
		[
			//construct publishers here, perhaps can be automated with codegen?
			new DiscordWebhookPublisher(),
			new BufferPublisher(),
		];
		foreach (var publisher in publishers)
			publisher.Configure(config);
	}

	public bool IsEnabled => AppConfig.Get("advanced", "publishing", false) && AppConfig.Get("publishing", internalName + "_enabled", false);

	public async Task AttemptPublish(Func<string, PublisherContent?> platformContent)
	{
		if (!IsEnabled || platformContent is null)
			return;
		await Task.WhenAll(publishers.Select(p =>
		{
			var content = platformContent.Invoke(p.PlatformId);
			if (content is null)
				return Task.CompletedTask;
			return p.AttemptPublish(content.Value);
		}));
	}

}

public interface IPublisher
{
	string PlatformId { get; }
	void Configure(PublisherConfig config);
	Task AttemptPublish(PublisherContent content);
}

public readonly record struct PublisherConfig(string identifier, string displayName);
public readonly record struct PublisherContent(string content, string[] files = null, Image[] images = null);
