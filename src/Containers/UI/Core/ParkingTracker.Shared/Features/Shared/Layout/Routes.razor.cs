using System.Reflection;
using Microsoft.AspNetCore.Components;

namespace ParkingTracker.Shared.Features.Shared.Layout;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The root component of the app: the router over the pages of this assembly, each shown in <see cref="MainLayout"/>.
///              The host puts it into the web view; there is only one page, the home page at <c>/</c>.
/// </summary>
public sealed partial class Routes : ComponentBase
{
	#region Properties
	/// <summary>
	/// Gets the assembly whose routable pages the router knows.
	/// </summary>
	private static Assembly Application => AssemblyReference.Assembly;
	#endregion
}
