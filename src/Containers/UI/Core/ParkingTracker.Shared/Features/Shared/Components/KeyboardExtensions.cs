using Microsoft.JSInterop;

namespace ParkingTracker.Shared.Features.Shared.Components;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Closes the on-screen keyboard of the phone, which Blazor cannot do by itself: the screens that submit a search call
///              it so that the keyboard does not cover the map and the list of matches.
/// </summary>
public static class KeyboardExtensions
{
	#region Constants
	/// <summary>
	/// The browser function that takes the focus off the focused element, which closes the keyboard of the phone.
	/// </summary>
	private const string BlurFunction = "document.activeElement.blur";
	#endregion

	#region Extension Methods
	/// <summary>
	/// Takes the focus off the focused element. A failure is ignored: the keyboard staying open is cosmetic, and the search must
	/// still run when the browser function is missing or the connection to the page was lost.
	/// </summary>
	/// <param name="runtime">The JavaScript runtime.</param>
	/// <returns>A task that completes when the call ended.</returns>
	/// <exception cref="ArgumentNullException">Thrown when <paramref name="runtime"/> is <see langword="null"/>.</exception>
	public static async Task CloseKeyboardAsync(this IJSRuntime runtime)
	{
		ArgumentNullException.ThrowIfNull(runtime);
		try
		{
			await runtime.InvokeVoidAsync(BlurFunction).ConfigureAwait(false);
		}
		catch (JSException)
		{
			// Nothing to do: see the summary.
		}
		catch (JSDisconnectedException)
		{
			// Nothing to do: see the summary.
		}
	}
	#endregion
}
