using Godot;
using System;

public partial class AccountRefreshHook : Control
{
    public async void RefreshAccount()
    {
        using var _ = LoadingOverlay.CreateToken();
        await GameAccount.RefreshActiveAccount();
    }
}
