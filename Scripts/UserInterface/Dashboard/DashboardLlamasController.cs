using Godot;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

public partial class DashboardLlamasController : Control
{
    [Export]
    Control loadingIcon;

    [Export]
    Control errorIcon;

    [Export]
    Control llamaEntryContainer;

    GameOfferEntry[] llamaEntries;

    public override void _Ready()
    {
        llamaEntries = [.. llamaEntryContainer
            .GetChildren()
            .Select(c => c is GameOfferEntry offerEntry ? offerEntry : null)
            .Where(oe => oe is not null)];
        VisibilityChanged += LoadShopLlamas;
        RefreshTimerController.OnHourChanged += UpdateShopLlamas;
        GameAccount.ActiveAccountChanged += UpdateShopLlamas;
        UpdateShopLlamas();
    }

    public override void _ExitTree()
    {
        RefreshTimerController.OnHourChanged -= UpdateShopLlamas;
        GameAccount.ActiveAccountChanged -= UpdateShopLlamas;
    }

    public void GoToLlamaTab() => LlamaInterface.SelectLlamaTab();


    CancellationTokenSource llamaShopCTS;
    SemaphoreSlim llamaShopSemaphore = new(1);
    void UpdateShopLlamas()
    {
        llamasDirty = true;
        LoadShopLlamas();
    }
    bool llamasDirty = false;
    async void LoadShopLlamas()
    {
        if (!IsVisibleInTree() || !llamasDirty)
            return;
        llamasDirty = false;
        llamaShopCTS = llamaShopCTS.CancelAndRegenerate(out var ct);

        loadingIcon.Visible = true;
        errorIcon.Visible = false;
        llamaEntryContainer.Visible = false;

        bool success = false;
        try
        {
            await llamaShopSemaphore.WaitAsync(ct);
            if (ct.IsCancellationRequested)
                return;

            await Helpers.WaitForFrame();

            var xrayStorefront = await GameStorefront.GetStorefront(FnStorefrontTypes.XRayLlamaCatalog, RefreshTimeType.Hourly);
            if (ct.IsCancellationRequested)
                return;

            var offers = xrayStorefront?.Offers?.Where(o => o is not null && (o.DailyLimit > 0 || o.EventLimit > 0) && o.OfferId != "B9B0CE758A5049F898773C1A47A69ED4")?.ToArray() ?? [];

            await GameAccount.ActiveAccount.GenerateXRayLlamaResults(offers.Any(o => o.Price.quantity == 0));

            for (int i = 0; i < llamaEntries.Length; i++)
            {
                var thisEntry = llamaEntries[i];
                if (i >= offers.Length)
                {
                    thisEntry.Visible = false;
                    continue;
                }
                thisEntry.Visible = true;
                thisEntry.SetOffer(offers[i]).StartTask();
            }
            success = true;
        }
        finally
        {
            llamaShopSemaphore.Release();
            if (!ct.IsCancellationRequested)
            {
                loadingIcon.Visible = false;
                llamaEntryContainer.Visible = success;
                errorIcon.Visible = !success;
            }
        }
    }
}
