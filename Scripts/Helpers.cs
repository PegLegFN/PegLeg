using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

public static partial class Helpers
{
	// Godot's FileAccess.GetAsText has issues with unicode files and can throw exceptions, this is a workaround for that
	public static string GetAsText(this FileAccess file)
	{
		var buffer = file.GetBuffer((long)file.GetLength());
		return Encoding.UTF8.GetString(buffer);
	}

	public static bool EvalMetaBool(this Node node, string metaKey) => node.GetMeta(metaKey).AsBool();

	public static T FirstChildOfType<T>(this Node parent) where T : Node
	{
		var childCount = parent.GetChildCount();
		for (int i = 0; i < childCount; i++)
		{
			if (parent.GetChild(i) is T typedChild)
				return typedChild;
		}
		return null;
	}

	public static void Unpress(this ButtonGroup group)
	{
		bool allowUnpress = group.AllowUnpress;
		group.AllowUnpress = true;
		if (group.GetPressedButton() is BaseButton button)
			button.ButtonPressed = false;
		group.AllowUnpress = allowUnpress;
	}

	public static bool TryGetWeakRef<T>(this Dictionary<string, WeakRef> refDict, string key, out T resource) where T : Resource
	{
		if (refDict.TryGetValue(key, out var wRef) && wRef?.GetRef().Obj is T cachedResource)
		{
			resource = cachedResource;
			return true;
		}
		resource = null;
		return false;
	}

	public static int ConvertRarityString(this string rarity) => rarity switch
	{
		"Common" => 1,
		"Uncommon" => 2,
		"Rare" => 3,
		"Epic" => 4,
		"Legendary" => 5,
		"Mythic" => 6,
		_ => 2
	};

	public static void SetVisibleIfHasContent(this Label label)
	{
		label.Visible = !string.IsNullOrWhiteSpace(label.Text);
	}

	public static string GlobalisePath(string godotPath)
	{
		if (godotPath.StartsWith("res://") && !OS.HasFeature("editor"))
			return OS.GetExecutablePath().Split("/")[..^1].Join("/") + "/" + ProjectSettings.GlobalizePath(godotPath);
		else
			return ProjectSettings.GlobalizePath(godotPath);
	}

	public static CancellationTokenSource CancelAndRegenerate(this CancellationTokenSource original, out CancellationToken ct)
	{
		original?.Cancel();
		original?.Dispose();
		var replacement = new CancellationTokenSource();
		ct = replacement.Token;
		return replacement;
	}

	static SceneTree MainLoopSceneTree => (SceneTree)Engine.GetMainLoop();

	public static async Task WaitForFrame() => await MainLoopSceneTree.WaitForFrame();
	public static async Task WaitForFrames(int count)
	{
		for (int i = 0; i < count; i++)
		{
			await MainLoopSceneTree.WaitForFrame();
		}
	}
	static async Task WaitForFrame(this SceneTree sceneTree) =>
		await sceneTree.ToSignal(sceneTree, SceneTree.SignalName.ProcessFrame);

	public static async Task WaitForTimer(double time, Action<double> timeLeft = null) => await MainLoopSceneTree.WaitForTimer(time, timeLeft);
	static async Task WaitForTimer(this SceneTree sceneTree, double time, Action<double> timeLeft = null) => await sceneTree.CreateTimer(time).WaitForTimer(timeLeft);
	public static async Task WaitForTimer(this SceneTreeTimer timer, Action<double> timeLeft = null)
	{
		if (timeLeft is null)
		{
			await timer.ToSignal(timer, SceneTreeTimer.SignalName.Timeout);
			return;
		}
		bool finished = false;
		timer.Timeout += () => finished = true;
		while (!finished)
		{
			timeLeft.Invoke((float)timer.TimeLeft);
			await MainLoopSceneTree.WaitForFrame();
		}
	}

	public static async void Defer(Action action, int frames = 1, CancellationToken ct = default)
	{
		await WaitForFrames(frames);
		if (ct.IsCancellationRequested)
			return;
		action?.Invoke();
	}

	public static async Task ChangeSceneAsync(this SceneTree tree, string path, Action<float> onProgress = null)
	{
		var reqErr = ResourceLoader.LoadThreadedRequest(path, useSubThreads: true);
		if (reqErr != Error.Ok)
		{
			GD.PushWarning($"Async scene change request failure: {reqErr} (\"{path}\")");
			return;
		}

		Godot.Collections.Array progress = [];
		var stage = ResourceLoader.LoadThreadedGetStatus(path, progress);
		while (stage == ResourceLoader.ThreadLoadStatus.InProgress)
		{
			onProgress?.Invoke((float)progress[0]);
			await WaitForFrame();
			stage = ResourceLoader.LoadThreadedGetStatus(path);
		}

		if (stage != ResourceLoader.ThreadLoadStatus.Loaded)
		{
			GD.PushWarning($"Async scene load failure: {stage} (\"{path}\")");
			return;
		}
		onProgress?.Invoke(1);
		await WaitForFrame();
		var scene = (PackedScene)ResourceLoader.LoadThreadedGet(path);
		tree.ChangeSceneToPacked(scene);
	}

	public static async void RunWithDelay(Task task, float delay)
	{
		try
		{
			await WaitForTimer(delay);
			await task;
		}
		catch (Exception e) { GD.PushError($"Unhandled async exception in RunWithDelay: {e}"); }
	}

	//public static void GetNodeOrNull<T>(this Node owner, NodePath path, out T result) where T : Node
	//{
	//    result = owner.GetNodeOrNull<T>(path);
	//}
	//public static void GetNodesOrNull<T>(this Node owner, NodePath[] paths, out T[] result) where T : Node
	//{
	//    result = new T[paths.Length];
	//    for (int i = 0; i < paths.Length; i++)
	//    {
	//        result[i] = owner.GetNodeOrNull<T>(paths[i]);
	//    }
	//}

	/* Old Web Request system
	public static async Task<JsonNode> MakeRequest(HttpMethod method, System.Net.Http.HttpClient endpoint, string uri, string body, AuthenticationHeaderValue authentication, string mediaType = "application/x-www-form-urlencoded", bool addCosmeticHeader = false)
	{
		using StringContent content = mediaType != "" ? new(body, Encoding.UTF8, mediaType) : null;
		using var request = new HttpRequestMessage(method, uri) { Content = content };
		if (authentication is not null)
			request.Headers.Authorization = authentication;
		if (addCosmeticHeader)
			request.Headers.Add("x-api-key", cosmeticSalsa);
		return await MakeRequest(endpoint, request);
	}

	public static async Task<JsonNode> MakeRequest(System.Net.Http.HttpClient endpoint, HttpRequestMessage request)
	{
		using var result = await MakeRequestRaw(endpoint, request);
		var resultText = result is not null ? await result.Content.ReadAsStringAsync() : null;

		JsonNode resultNode = null;
		try
		{
			resultNode = JsonNode.Parse(resultText);
		}
		catch (JsonException)
		{
			GD.Print("result was not json: " + resultText);
			resultNode = new JsonObject()
			{
				["success"] = result.IsSuccessStatusCode,
				["code"] = (int)result.StatusCode,
				["response"] = resultText,
			};
			if (result.StatusCode == System.Net.HttpStatusCode.GatewayTimeout)
				resultNode["offline"] = true;
		}
		catch (ArgumentNullException)
		{
			GD.Print("result text was null");
			resultNode = new JsonObject()
			{
				["success"] = result?.IsSuccessStatusCode ?? false,
			};
			if(result is not null)
				resultNode["code"] = (int)result.StatusCode;
			if (result?.StatusCode == System.Net.HttpStatusCode.GatewayTimeout)
				resultNode["offline"] = true;
		}

		if (result?.IsSuccessStatusCode == true)
			resultNode ??= new JsonObject() { ["success"] = true };
		//todo: throw exception when encountering a response with an errorMessage

		if (resultNode is JsonObject && resultNode["numericErrorCode"]?.GetValue<int>() == 1031)
		{
			//todo: move this web stuff around so that we know which account is making the request
			GameAccount.activeAccount.ForceExpireToken();
		}

		return resultNode;
	}

	public static async Task<HttpResponseMessage> MakeRequestRaw(System.Net.Http.HttpClient endpoint, HttpRequestMessage request)
	{
		//GD.Print("Debug: "+request.ToString());
		HttpResponseMessage response = null;
		try
		{
			response = await endpoint.SendAsync(request);
			response.EnsureSuccessStatusCode();
		}
		catch (HttpRequestException ex)
		{
			if (await IsOffline())
			{
				GD.Print($"Offline ({response})");
				return new() { Content = new StringContent("Offline"), StatusCode = System.Net.HttpStatusCode.GatewayTimeout };
			}
			try
			{
				var resultText = response is not null ? await response.Content.ReadAsStringAsync() : null;
				if(resultText is not null)
				{
					var resultNode = JsonNode.Parse(resultText);
					if (resultNode["numericErrorCode"]?.GetValue<int>() == 1012)
					{
						// silences Device Code check fails
						return response;
					}
				}
			}
			catch (JsonException) { }
			catch (Exception e)
			{
				GD.PushError("Exception in web request: "+e.Message);
			}
			GD.Print($"\nException Caught! ({ex.HttpRequestError})");
			GD.Print($"Message :{ex.Message} ");
			GD.Print($"Response :{(response is not null ? (await response.Content.ReadAsStringAsync()) : "null response")} ");
		}
		return response;
	}
	*/

	//assumes google is online
	public static async Task<bool> IsOffline()
	{
		var reply = await new Ping().SendPingAsync("8.8.8.8", 500);
		return reply.Status != IPStatus.Success;
	}


	static char[] compactNumberMilestones = "KMBT".ToCharArray();
	public static string Compactify(this int number)
	{
		if (number == int.MaxValue)
			return "Max";
		return ((long)number).Compactify();
	}
	public static string Compactify(this long number)
	{
		if (number == long.MaxValue)
			return "Max";
		int milestoneLevel = 0;
		long solidNumber = number;
		int decimalNumber = 0;
		for (int i = 0; i < compactNumberMilestones.Length + 1; i++)
		{
			if (solidNumber > 999)
			{
				decimalNumber = (int)solidNumber % 1000;
				solidNumber = (long)Math.Floor(solidNumber * 0.001);
			}
			else
			{
				milestoneLevel = i;
				break;
			}
		}

		int solidFigures = solidNumber.ToString().Length; //2=>2 1=>1

		bool decimalExists = decimalNumber > 0 && solidFigures < 3;
		string decimalString = decimalNumber.ToString();
		if (decimalExists)
		{
			while (decimalString.Length < 3)
			{
				decimalString = "0" + decimalString;
			}
			decimalString = decimalString[..(solidFigures == 2 ? 1 : 2)];
		}

		string combinedNumber = solidNumber + (decimalExists ? ("." + decimalString) : "");
		return combinedNumber + (milestoneLevel == 0 ? "" : compactNumberMilestones[milestoneLevel - 1]);
	}

	public static string Notate(this int number)
	{
		if (number == int.MaxValue)
			return "Max";
		return ((long)number).Notate();
	}
	public static string Notate(this long number)
	{
		if (number == int.MaxValue)
			return "Max";
		string soFar = "";
		string raw = new([.. number.ToString().Reverse()]);
		int milestones = (raw.Length - 1) / 3;
		for (int i = 0; i < milestones; i++)
		{
			int m = i * 3;
			soFar = "," + new string([.. raw[m..(m + 3)].Reverse()]) + soFar;
		}
		for (int i = milestones * 3; i < raw.Length; i++)
		{
			soFar = raw[i] + soFar;
		}
		return soFar;
	}

	public static string FormatTimeSeconds(this int timeInSeconds) =>
		TimeSpan.FromSeconds(timeInSeconds).FormatTime();

	public enum TimeFormat
	{
		Full,
		SigLong,
		SigShort,
	}

	public static string FormatTime(this TimeSpan time, TimeFormat timeFormat = TimeFormat.Full, bool useWeeks = false, bool longDecimals = true)
	{
		if (timeFormat == TimeFormat.Full)
		{
			string text = time.Seconds.ToString();
			if (time.TotalMinutes >= 1)
			{
				text = time.Minutes + ":" + (time.Seconds < 10 ? "0" : "") + text;
				if (time.TotalHours >= 1)
				{
					text = time.Hours + ":" + (time.Minutes < 10 ? "0" : "") + text;

					if (time.TotalDays >= 1)
					{
						text = time.Days + ":" + (time.Hours < 10 ? "0" : "") + text;
					}
				}
			}
			return text;
		}

		bool longTime = timeFormat == TimeFormat.SigLong;
		if (useWeeks && (time.TotalDays > 70 || (!longDecimals && time.TotalDays > 7)))
		{
			return Mathf.Floor(time.TotalDays / 7) + (longTime ? ((int)(time.TotalDays / 7) > 1 ? " weeks" : " week") : "W");
		}
		else if (useWeeks && time.TotalDays > 7)
		{
			return $"{time.TotalDays / 7:0.0}" + (longTime ? ((int)(time.TotalDays / 7) > 1 ? " weeks" : " week") : "W");
		}
		else if (time.TotalDays > 10 || (!longDecimals && time.TotalDays > 2))
		{
			return Mathf.Floor(time.TotalDays) + (longTime ? ((int)time.TotalDays > 1 ? " days" : " day") : "D");
		}
		else if (time.TotalDays > 2)
		{
			return $"{time.TotalDays:0.0}{(longTime ? (time.TotalDays > 1 ? " days" : " day") : "D")}";
		}
		else if (time.TotalHours > 10)
		{
			return Mathf.Floor(time.TotalHours) + (longTime ? " hours" : "H");
		}
		else if (time.TotalHours > 1.1)
		{
			string timerText = Mathf.FloorToInt(time.TotalHours * 10).ToString();
			return $"{timerText[..^1]}.{timerText[^1]}{(longTime ? " hours" : "H")}";
		}
		else
		{
			return Mathf.Floor(Mathf.Max(time.TotalMinutes, -0.1)) + (longTime ? " mins" : "m");
		}
	}

	public static async void StartTask(this Task task)
	{
		try { await task; }
		catch (Exception e) { GD.PushError($"Unhandled async exception: {e}"); }
	}

	public static async Task<SemaphoreToken> AwaitToken(this SemaphoreSlim source, Action onRelease = null)
	{
		bool immadiate = source.CurrentCount > 0;
		await source.WaitAsync();
		return new SemaphoreToken(source, onRelease, immadiate);
	}

	public struct SemaphoreToken(SemaphoreSlim source, Action onRelease, bool wasImmediate) : IDisposable
	{
		public readonly bool wasImmediate = wasImmediate;

		public void Dispose()
		{
			onRelease?.Invoke();
			source?.Release();
		}
	}

	public static int RandomIndexFromWeights(this float[] weights, int preventRepeat = -1)
	{
		//remove negative weights
		weights = weights.Select(w => w >= 0 ? w : 0).ToArray();

		if (preventRepeat >= 0 && preventRepeat < weights.Length)
		{
			float backup = weights[preventRepeat];
			weights[preventRepeat] = 0;
			if (weights.All(w => w == 0))
				weights[preventRepeat] = backup;
		}

		float totalRange = weights.Sum();
		float roll = (float)GD.RandRange(0, totalRange);
		float total = 0;
		for (int i = 0; i < weights.Length; i++)
		{
			total += weights[i];
			if (roll < total)
				return i;
		}
		return weights.Length - 1;
	}

	public static T PickFromWeights<T>(this T[] source, Func<T, float> weightPicker, T prev = null, float[] weights = null) where T : class
	{
		if ((source?.Length ?? 0) == 0)
			return null;
		weights ??= [.. source.Select(weightPicker)];
		if (weights.Length < source.Length)
			weights = [.. weights.Union(source[weights.Length..].Select(weightPicker))];
		if (weights.Length > source.Length)
			weights = weights[..source.Length];
		int lastIdx = Array.IndexOf(source, prev);
		return source[weights.RandomIndexFromWeights(lastIdx)];
	}

	//workaround for bug introduced in 4.3
	public static void FixControlOffsets(this Control control)
	{
		control.OffsetTop = control.OffsetTop;
		control.OffsetBottom = control.OffsetBottom;
		control.OffsetLeft = control.OffsetLeft;
		control.OffsetRight = control.OffsetRight;
	}

	public static void ResetOffsets(this Control control)
	{
		control.OffsetTop = 0;
		control.OffsetBottom = 0;
		control.OffsetLeft = 0;
		control.OffsetRight = 0;
	}

	public static void ResetAnchors(this Control control)
	{
		control.AnchorTop = 0;
		control.AnchorBottom = 1;
		control.AnchorLeft = 0;
		control.AnchorRight = 1;
	}

	public static void SafeConnect(this Node node, StringName signalName, Callable callable)
	{
		//var connections = node.GetSignalConnectionList(signalName);
		//if (!connections?.Any(c => c["callable"].AsCallable().Equals(callable)) ?? false)
		//    node.Connect(signalName, callable);
		if (!node.IsConnected(signalName, callable))
			node.Connect(signalName, callable);
	}

	public static void SafeDisconnect(this Node node, StringName signalName, Callable callable)
	{
		//var connections = node.GetSignalConnectionList(signalName);
		//if (connections?.Any(c => c["callable"].AsCallable().Equals(callable)) ?? false)
		//    node.Disconnect(signalName, callable);
		if (node.IsConnected(signalName, callable))
			node.Disconnect(signalName, callable);
	}

	public static bool DevTextKeybindPressed(this InputEvent e, Key customKey = Key.D) =>
		e is InputEventKey keyEvent &&
		keyEvent.Pressed &&
		keyEvent.ShiftPressed &&
		!keyEvent.CtrlPressed &&
		keyEvent.AltPressed &&
		keyEvent.Keycode == customKey;

	public static async Task<GameAccount> EnsureProfile(this Task<GameAccount> accountTask, string profileId, bool force = false)
	{
		var account = await accountTask;
		return await account.EnsureProfile(profileId, force);
	}

	public static string ToAttribute(this OrderRange range) => range switch
	{
		OrderRange.Daily => "daily_purchases",
		OrderRange.Weekly => "weekly_purchases",
		OrderRange.Monthly => "monthly_purchases",
		_ => null,
	};

	public static DateTime ToInterval(this OrderRange range) => range switch
	{
		OrderRange.Daily => RefreshTimerController.GetLastRefreshTime(RefreshTimeType.Daily),
		OrderRange.Weekly => RefreshTimerController.GetLastRefreshTime(RefreshTimeType.Weekly),
		_ => default,
	};

	public static string FixNewlines(this string toPrint) => toPrint.Replace("\r\n", "\n");

	public static Func<T, bool> ToFunc<T>(this Predicate<T> predicate, bool defaultResult = true) => t => predicate.Try(t, defaultResult);
	public static bool Try<T>(this Predicate<T> predicate, T t, bool defaultResult = true) => predicate is not null ? predicate(t) : defaultResult;
	public static bool Try<T>(this Func<T, bool> filter, T t, bool defaultResult = true) => filter is not null ? filter(t) : defaultResult;
}