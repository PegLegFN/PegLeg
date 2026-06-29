using Godot;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

public class PublishingManager
{
	static Dictionary<string, IPublishOutput> cachedOutputs = [];
	public static IPublishOutput GetPublisher(string identfier)
	{
		if (cachedOutputs.TryGetValue(identfier, out var cached))
			return cached;
		return null;
	}
}

public class CompositePublisher(IPublishOutput[] outputs) : IPublishOutput
{
	public async Task AttemptPublish(string content, string[] files, Image[] images)
	{
		await Task.WhenAll(outputs.Select(o => o.AttemptPublish(content, files, images)));
	}

	public void Configure(string identifier)
	{
		foreach (var o in outputs)
		{
			o.Configure(identifier);
		}
	}
}

public interface IPublishOutput
{
	void Configure(string identifier);
	Task AttemptPublish(string content, string[] files, Image[] images);
}

