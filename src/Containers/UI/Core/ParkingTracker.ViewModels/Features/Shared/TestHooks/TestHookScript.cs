using System.Text;

namespace ParkingTracker.ViewModels.Features.Shared.TestHooks;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The script that defines <c>window.ptTest</c> in the web view, built from one list of hook names so that the script and
///              the <c>[JSInvokable]</c> methods of the host cannot drift apart (a unit test compares them). The host evaluates the
///              script, then calls <see cref="InstallFunction"/> with a reference to its bridge object.
/// </summary>
public static class TestHookScript
{
	#region Constants
	/// <summary>
	/// The name of the global function that the script defines and the host calls with the bridge reference.
	/// </summary>
	public const string InstallFunction = "__ptTestInstall";

	/// <summary>
	/// The hooks: the name the test calls on <c>window.ptTest</c> and the name of the <c>[JSInvokable]</c> method behind it.
	/// </summary>
	public static readonly IReadOnlyList<(string JsName, string DotNetName)> Hooks =
	[
		("setCard", "SetCard"),
		("getCard", "GetCard"),
		("decide", "Decide"),
		("freeSpots", "FreeSpots"),
		("officialList", "OfficialList"),
		("platformStatus", "PlatformStatus"),
		("screenState", "ScreenState"),
		("startTimer", "StartTimer"),
		("parseCardText", "ParseCardText"),
	];
	#endregion

	#region Public Methods
	/// <summary>
	/// Builds the script.
	/// </summary>
	/// <returns>JavaScript that defines the global function <see cref="InstallFunction"/>; calling it with a .NET object reference sets <c>window.ptTest</c>.</returns>
	public static string Build()
	{
		var script = new StringBuilder();
		script.Append("window.").Append(InstallFunction).Append(" = function (ref) { window.ptTest = {");
		foreach (var (jsName, dotNetName) in Hooks)
		{
			script.Append(' ').Append(jsName).Append(": (...args) => ref.invokeMethodAsync('").Append(dotNetName).Append("', ...args),");
		}

		script.Append(" }; };");
		return script.ToString();
	}
	#endregion
}
