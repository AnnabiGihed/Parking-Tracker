using Microsoft.AspNetCore.Components;

namespace ParkingTracker.Shared.Features.Shared.Components;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Draws one of the Material Design icons of the first version of the app as an inline SVG that takes the colour of
///              its text. The icons are the ones of <c>icons.js</c>, by the same names.
/// </summary>
public sealed partial class Icon : ComponentBase
{
	#region Parameters
	/// <summary>
	/// Gets or sets the name of the icon, for example <c>locate</c>; an unknown name draws the help icon.
	/// </summary>
	[Parameter]
	public string Name { get; set; } = IconPaths.Fallback;

	/// <summary>
	/// Gets or sets an extra CSS class of the icon.
	/// </summary>
	[Parameter]
	public string Class { get; set; } = string.Empty;
	#endregion
}
