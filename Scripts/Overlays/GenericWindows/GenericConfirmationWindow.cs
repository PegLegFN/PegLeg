using Godot;
using System.Threading;
using System.Threading.Tasks;

public partial class GenericConfirmationWindow : ModalWindow
{
	[Export]
	Label header;
	[Export]
	Label content;
	[Export]
	Label warningLabel;
	[Export]
	Button cancelButton;
	[Export]
	Button negativeButton;
	[Export]
	Button positiveButton;

	static GenericConfirmationWindow instance;

	public override void _Ready()
	{
		base._Ready();
		instance = this;
	}

	bool isSelecting = false;
	bool allowCancel = false;
	bool? result = null;

	//public static async Task ShowErrorForWebResult(JsonObject errorResult)
	//{
	//    errorResult ??= [];
	//    if (!errorResult.ContainsKey("errorMessage"))
	//        errorResult["errorMessage"] = "(No Message Provided)";
	//    await instance.ShowConfirmationInst("Uh oh! Something Goofed", "Continue", "", errorResult["errorMessage"].ToString(), errorResult["errorCode"].ToString(), false, 8);
	//}

	public static async Task ShowError(string description, string header = "Error            ")
	{
		if (instance is not null)
			await instance.ShowConfirmationInst(header, null, "Close", description, "", false, 8, true);
	}

	public static async Task<bool?> ShowConfirmation(string headerText, string postiveText = "Confirm", string negativeText = "", string contextText = "", string warningText = "", bool allowCancel = true, int headerSpace = 8, bool highlightConfirm = true) =>
		instance is null ? null : await instance.ShowConfirmationInst(headerText, postiveText, negativeText, contextText, warningText, allowCancel, headerSpace, highlightConfirm);

	SemaphoreSlim msgSemaphone = new(1);

	public async Task<bool?> ShowConfirmationInst(string headerText, string positiveText, string negativeText, string contextText, string warningText, bool allowCancel, int headerSpace, bool highlightConfirm)
	{
		await msgSemaphone.WaitAsync();
		try
		{
			for (int i = 0; i < headerSpace; i++)
			{
				headerText += " ";
			}
			header.Text = headerText;
			header.SetVisibleIfHasContent();

			content.Text = contextText;
			content.SetVisibleIfHasContent();

			warningLabel.Text = warningText;
			warningLabel.SetVisibleIfHasContent();

			this.allowCancel = allowCancel;
			cancelButton.Visible = allowCancel;

			positiveButton.Text = positiveText;
			positiveButton.Visible = !string.IsNullOrWhiteSpace(positiveText);
			positiveButton.ThemeTypeVariation = highlightConfirm ? "ActionButton" : "LargeButton";

			negativeButton.Text = negativeText;
			negativeButton.Visible = !string.IsNullOrWhiteSpace(negativeText);

			isSelecting = true;
			result = null;
			SetWindowOpen(true);
			while (isSelecting)
				await Helpers.WaitForFrame();
			SetWindowOpen(false);

			return result;
		}
		finally
		{
			msgSemaphone.Release();
		}
	}

	protected override void CloseWindowViaInput() => Cancel();

	private void Cancel()
	{
		if (allowCancel)
			isSelecting = false;
	}

	public void SetResult(bool newResult)
	{
		isSelecting = false;
		result = newResult;
	}
}
