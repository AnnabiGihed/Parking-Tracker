using ParkingTracker.ViewModels.Features.Shared.TestHooks;

namespace ParkingTracker.ViewModels.Tests.Features.Shared.TestHooks;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Unit tests for <see cref="TestHookScript"/>: the script defines <c>window.ptTest</c> with every hook the emulator test
///              uses, each one calling the <c>[JSInvokable]</c> method of the same list entry.
/// </summary>
public sealed class TestHookScriptTests
{
	#region Build Tests
	/// <summary>
	/// Verifies that the script defines the install function and every hook, calling the matching .NET method.
	/// </summary>
	[Fact]
	public void Build_ShouldDefineEveryHookOnWindowPtTest()
	{
		var script = TestHookScript.Build();

		script.Should().Contain("window.__ptTestInstall = function (ref)").And.Contain("window.ptTest = {");
		foreach (var (jsName, dotNetName) in TestHookScript.Hooks)
		{
			script.Should().Contain($"{jsName}: (...args) => ref.invokeMethodAsync('{dotNetName}', ...args)");
		}
	}

	/// <summary>
	/// Verifies that the hooks are the nine the emulator test calls.
	/// </summary>
	[Fact]
	public void Hooks_ShouldBeTheNineOfTheEmulatorTest()
	{
		TestHookScript.Hooks.Select(h => h.JsName).Should().Equal("setCard", "getCard", "decide", "freeSpots", "officialList", "platformStatus", "screenState", "startTimer", "parseCardText");
	}
	#endregion
}
