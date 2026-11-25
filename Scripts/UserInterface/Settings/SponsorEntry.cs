using Godot;
using System;
using System.Threading;

public partial class SponsorEntry : Control, IListEntry<SponsorDisplay.SponsoredUser>
{
	[Signal]
	public delegate void LoadingChangedEventHandler(bool loading);
	[Signal]
	public delegate void NameChangedEventHandler(string name);
	[Signal]
	public delegate void TooltipChangedEventHandler(string name);
	[Signal]
	public delegate void InitialChangedEventHandler(string name);
	[Signal]
	public delegate void IconChangedEventHandler(Texture2D icon);

	int IListEntry<SponsorDisplay.SponsoredUser>.CurrentIndexTarget { get; set; }
	IListProvider<SponsorDisplay.SponsoredUser> IListEntry<SponsorDisplay.SponsoredUser>.CurrentListProvider { get; set; }

	string currentUser;
	CancellationTokenSource cts;
	public async void SetListEntryValue(SponsorDisplay.SponsoredUser newValue)
	{
		if (currentUser == newValue.username)
			return;
		currentUser = newValue.username;

		cts = cts.CancelAndRegenerate(out var ct);
		EmitSignalNameChanged(newValue.username);
		EmitSignalIconChanged(null);
		EmitSignalLoadingChanged(true);
		EmitSignalInitialChanged("");
		EmitSignalTooltipChanged(CustomTooltip.GenerateSimpleTooltip($" {newValue.username} ", bannerCol: newValue.tier switch
		{
			>= 3 => PegLegResourceManager.supplimentaryData.RarityColours[5].ToHtml(),
			2 => PegLegResourceManager.supplimentaryData.RarityColours[4].ToHtml(),
			_ => PegLegResourceManager.supplimentaryData.RarityColours[3].ToHtml(),
		}));

		var url = newValue.avatar;
		if (url.Contains('?'))
			url = url.Split('?')[0];
		url += $"?s={Size.X}";

		try
		{
			//request image
			var imgRequest = await WebHelpers.MakeRequest(url).Send();
			if (await imgRequest.CheckForError())
			{
				if (ct.IsCancellationRequested)
					return;
				EmitSignalLoadingChanged(false);
				EmitSignalInitialChanged(newValue.username[..1]);
				return;
			}
			if (ct.IsCancellationRequested)
				return;
			var image = await imgRequest.ReadImage();
			if (ct.IsCancellationRequested)
				return;
			EmitSignalLoadingChanged(false);
			EmitSignalIconChanged(ImageTexture.CreateFromImage(image));
		}
		catch
		{
			if (ct.IsCancellationRequested)
				return;
			EmitSignalLoadingChanged(false);
			EmitSignalInitialChanged(newValue.username[..1]);
		}
	}
}
