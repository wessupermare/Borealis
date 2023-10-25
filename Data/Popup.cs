using CommunityToolkit.Maui.Views;

namespace Borealis.Data;

internal static class Popups
{
	public static Task<object?> DisplayPopupAsync(this Page page, Popup popup)
	{
		if (MainThread.IsMainThread)
			return page.ShowPopupAsync(popup);
		else
			return MainThread.InvokeOnMainThreadAsync(async () => await page.ShowPopupAsync(popup));
	}
}
