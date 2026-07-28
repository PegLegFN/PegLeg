
using Godot;

public partial class OpenCosmeticIconCtx : AbstractContextComponent
{
	//todo: make this a setting
	const string exportPath = "user://cosmetic_exports";
	public override string Id => "OpenCosmeticIcon";
	CosmeticShopOfferEntry currentCosmetic;
	CosmeticOfferEntryNew currentCosmeticNew;
	public override void Update(ContextMenuHook hook)
	{
		currentCosmetic = hook?.cosmeticSource;
		currentCosmeticNew = hook?.newCosmeticSource;
		SetDisabled(currentCosmetic?.imageUrl is null && currentCosmeticNew?.currentOffer?.CosmeticDAV2Image is null);
	}

	public void Copy()
	{
		if (currentCosmetic is not null)
		{
			if (Input.IsKeyPressed(Key.Shift) && Input.IsKeyPressed(Key.Ctrl))
			{
				OS.ShellOpen(ProjectSettings.GlobalizePath(CatalogRequests.LocalCosmeticResourcePath(currentCosmetic.imageUrl)));
			}
			else if (Input.IsKeyPressed(Key.Shift))
			{
				OS.ShellOpen(currentCosmetic.imageUrl);
			}
			else
			{
				var file = CatalogRequests.LocalCosmeticResourcePath(currentCosmetic.imageUrl);
				var extension = file.Split(".")[^1];
				if (!DirAccess.DirExistsAbsolute(exportPath))
					DirAccess.MakeDirAbsolute(exportPath);
				DirAccess.CopyAbsolute(file, $"{exportPath}/{currentCosmetic.displayType ?? "<???>"} - {currentCosmetic.displayName ?? "<???>"}.{extension}");
				OS.ShellOpen(ProjectSettings.GlobalizePath(exportPath));
			}
		}
		else if (currentCosmeticNew?.currentOffer?.CosmeticDAV2Image is { } imageData)
		{
			if (Input.IsKeyPressed(Key.Shift) && Input.IsKeyPressed(Key.Ctrl))
			{
				imageData.ShellOpenLocal();
			}
			else if (Input.IsKeyPressed(Key.Shift))
			{
				imageData.ShellOpenRemote();
			}
			else
			{
				var file = CatalogRequests.LocalCosmeticResourcePathFromId(imageData.uniqueName);
				var extension = file.Split(".")[^1];
				if (!DirAccess.DirExistsAbsolute(exportPath))
					DirAccess.MakeDirAbsolute(exportPath);
				DirAccess.CopyAbsolute(file, $"{exportPath}/{imageData.uniqueName}.{extension}");
				OS.ShellOpen(ProjectSettings.GlobalizePath(exportPath));
			}
		}
		menu.CloseMenu();
	}
}
