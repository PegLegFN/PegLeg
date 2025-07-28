
using Godot;

public partial class OpenCosmeticIconCtx : BaseContextComponent
{
    //todo: make this a setting
    const string exportPath = "user://cosmetic_exports";
    public override string Id => "OpenCosmeticIcon";
    CosmeticShopOfferEntry currentCosmetic;
    public override void Update(ContextMenuHook hook)
    {
        currentCosmetic = hook?.cosmeticSource;
        SetDisabled(currentCosmetic?.imageUrl is null);
    }

    public void Copy()
    {
        if (currentCosmetic is null)
            return;
        if (Input.IsKeyPressed(Key.Shift) && Input.IsKeyPressed(Key.Ctrl))
        {
            OS.ShellOpen(ProjectSettings.GlobalizePath(CatalogRequests.LocalCosmeticResourcePath(currentCosmetic.imageUrl)));
        }
        if (Input.IsKeyPressed(Key.Shift))
        {
            OS.ShellOpen(currentCosmetic.imageUrl);
        }
        else
        {
            var file = CatalogRequests.LocalCosmeticResourcePath(currentCosmetic.imageUrl);
            var extension = file.Split(".")[^1];
            if(!DirAccess.DirExistsAbsolute(exportPath))
                DirAccess.MakeDirAbsolute(exportPath);
            DirAccess.CopyAbsolute(file, $"{exportPath}/{currentCosmetic.displayType??"<???>"} - {currentCosmetic.displayName ?? "<???>"}.{extension}");
            OS.ShellOpen(ProjectSettings.GlobalizePath(exportPath));
        }
        menu.CloseMenu();
    }
}
