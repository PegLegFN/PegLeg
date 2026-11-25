using Godot;
using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using GDFileAccess = Godot.FileAccess;

public partial class CosmoRequests
{
	// Based off Krowe Mohs RE work (and an Ai summary document Marlon made)

	public record struct Config
	{

		public string Version { get; init; }
		public string Key { get; init; }
		public string BaseURL { get; init; }

		public static void OverrideConfig(Config? overrideConfig) => Config.overrideConfig = overrideConfig;
		static Config? overrideConfig = null;

		public static Config ActiveConfig
		{
			get
			{
				if (overrideConfig is Config realOverride)
					return realOverride;
				if (PegLegResourceManager.MiscData["Cosmo"] is JsonObject cosmoData)
					return cosmoData.Deserialize<Config>();
				return FallbackConfig;
			}
		}

		public static readonly Config FallbackConfig = new()
		{
			Version = "43.11",
			Key = "WvZrlCTeEGZslMa6RT29x3432+ONpfk/qju6vCTicwU=",
			BaseURL = "https://cosmo.fdeb.live.use1a.on.epicgames.com/v1/item/"
		};
	}

	readonly record struct ConfigOverride(Config Config, DateTime? ValidUntil);

	const string overridePath = "user://cosmoOverride.json";
	public static async Task LoadConfigOverride()
	{
		if (GDFileAccess.FileExists(overridePath))
		{
			using var localOverrideFile = GDFileAccess.Open(overridePath, GDFileAccess.ModeFlags.Read);
			try
			{
				var localOverride = JsonSerializer.Deserialize<ConfigOverride>(localOverrideFile.GetAsText());
				if (localOverride.ValidUntil is not DateTime vUntil || DateTime.Now < vUntil.ToLocalTime())
				{
					Config.OverrideConfig(localOverride.Config);
					if (localOverride.ValidUntil is not null)
						return;
				}
			}
			catch { }
		}

		var cosmoOverrideResponse = await ApiWebAddresses.pegLegLiteBucket
				.MakeRequest("cosmoOverride.json")
				.Send();

		if (await cosmoOverrideResponse.CheckForError(logError: false))
			return;

		try
		{
			var overrideData = await cosmoOverrideResponse.ReadJson<ConfigOverride>();

			using var localOverrideFile = GDFileAccess.Open(overridePath, GDFileAccess.ModeFlags.Write);
			localOverrideFile.StoreString(JsonSerializer.Serialize(overrideData));

			if (overrideData.ValidUntil is not DateTime vUntil || DateTime.Now < vUntil.ToLocalTime())
				Config.OverrideConfig(overrideData.Config);
			GD.Print("Fetched Cosmo Override");
		}
		catch { }

	}

	private static byte[] B64ToBytes(string base64)
	{
		base64 = base64.Replace("-", "+").Replace("_", "/");
		return Convert.FromBase64String(base64);
	}

	
	private static string BytesToB64(byte[] bytes)
	{
		var base64 = Convert.ToBase64String(bytes);
		base64 = base64.Replace("+", "-").Replace("/", "_");
		return B64Ending().Replace(base64, "");
		//return base64;
	}

	[GeneratedRegex("=+$")]
	private static partial Regex B64Ending();

	public static CosmoImageData GetDisplayAsset(string displayAssetName, int index = 0, string aes = null)
	{
		if (displayAssetName is null)
			return default;
		return GetImageData(
			$"AthenaItemShopOfferDisplayData:{displayAssetName.ToLower()}",
			"store_image",
			[index],
			"2048x2048",
			aes
		);
	}
		

	public static CosmoImageData GetItemIcon(string templateId) => 
		GetImageData(
			templateId,
			"preview_image",
			[],
			"1024x1024"
		);

	public static CosmoImageData GetItemStyleIcon(string templateId, int channel, int style) => 
		GetImageData(
			templateId,
			"preview_image",
			[channel, style],
			"1024x1024"
		);

	public static CosmoImageData GetItemPreview(string templateId, int[] stylePermutations = null) => 
		GetImageData(
			templateId,
			"locker_preview_image",
			stylePermutations,
			"1024x1024"
		);

	public static CosmoImageData GetImageData(
		string templateId,
		string descriptorSuffix,
		int[] styles = null,
		string urlSuffix = "png",
		string aes = null,
		Config? config = null
	)
	{
		config ??= Config.ActiveConfig;
		if (!templateId.Contains(':'))
			return default;
		var splitTemplate = templateId.Split(':');
		if (splitTemplate.Length == 2)
			templateId = $"{splitTemplate[0]}:{splitTemplate[1].ToLower()}";
		if ((styles?.Length ?? 0) > 0)
			descriptorSuffix += $"[{string.Join(",", styles)}]";
		//if (templateIdExtra is not null)
		//	templateId += $"[{templateIdExtra}]";

		string baseDescriptor = $"fn/{config?.Version}/{templateId}/{descriptorSuffix}";

		byte[] hashDescriptorBytes = baseDescriptor.ToUtf8Buffer();
		byte[] releaseKeyBytes = B64ToBytes(config?.Key);
		byte[] encryptionBytes = aes is null ? [] : B64ToBytes(aes);
		byte[] mergedBytes = [.. hashDescriptorBytes, .. releaseKeyBytes, .. encryptionBytes];

		var hashDescriptor = BytesToB64(SHA256.HashData(new MemoryStream(mergedBytes)));
		//var publicDescriptor = BytesToB64(SHA256.HashData(new MemoryStream($"{baseDescriptor}/{config.key[..4]}nullnull".ToUtf8Buffer())));

		return new(
			$"{config?.BaseURL}{hashDescriptor}/{urlSuffix}", //url
			$"{templateId.Replace(":","__")}-{descriptorSuffix}" //unique name for local caching
		);
	}

	public readonly record struct CosmoImageData(string url, string uniqueName)
	{
		public ImageTexture GetCachedTexture()
		{
			if (uniqueName is null)
				return null;
			if (CatalogRequests.TryGetCosmeticTexture(uniqueName, cacheOnly: true) is ImageTexture existingTexture)
				return existingTexture;
			return null;
		}

		public ImageTexture GetLocalTexture(float resolutionScale = 1)
		{
			if (uniqueName is null)
				return null;
			if (CatalogRequests.TryGetCosmeticTexture(uniqueName, resolutionScale) is ImageTexture existingTexture)
				return existingTexture;
			return null;
		}

		public Image ReadLocalImageDirect()
		{
			if (uniqueName is null)
				return null;
			var path = CatalogRequests.LocalCosmeticResourcePathFromId(uniqueName);
			if (path is null)
				return null;
			return Image.LoadFromFile(path);
		}

		public async Task<ImageTexture> FetchTexture(float resolutionScale = 1)
		{
			if (url is null || uniqueName is null)
				return null;
			if (CatalogRequests.TryGetCosmeticTexture(uniqueName, resolutionScale) is ImageTexture existingTexture)
				return existingTexture;
			await FetchImage(resolutionScale);
			return CatalogRequests.TryGetCosmeticTexture(uniqueName);
		}

		static bool hasAlertedNotFound = false;

		public async Task<Image> FetchImage(float resolutionScale = 1)
		{
			using var result = await WebHelpers.MakeRequest(url).Accepts(WebMedia.Image.Any).Send();

			if(!hasAlertedNotFound && result.StatusCode == System.Net.HttpStatusCode.NotFound)
			{
				GD.PushWarning("WARNING: A Cosmo request has 404ed, the Cosmo key may be incorrect");
				hasAlertedNotFound = true;
			}
			
			if (result.StatusCode == System.Net.HttpStatusCode.NotFound || await result.CheckForError())
				return null; //silently fails when encountering 404s

			(Image image, byte[] buffer, string type) = await result.ReadImageWithBuffer();
			//Image image = await result.ReadDownloadImage(testStream);
			if (image is null)
				return null;

			CatalogRequests.RegisterCosmeticImageWithBuffer(ref image, buffer, type, uniqueName, resolutionScale);
			return CatalogRequests.TryGetCosmeticImage(uniqueName);
		}

		public void ShellOpenRemote() => OS.ShellOpen(url);//wont work with encrypted stuff
		public void ShellOpenLocal() => OS.ShellOpen(ProjectSettings.GlobalizePath(CatalogRequests.LocalCosmeticResourcePathFromId(uniqueName)));
	}
}
