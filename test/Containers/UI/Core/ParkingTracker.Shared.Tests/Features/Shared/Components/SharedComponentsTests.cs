using Bunit;
using NSubstitute;
using ParkingTracker.Application.Features.Shared.Abstractions;
using ParkingTracker.Shared.Features.Shared.Components;
using ParkingTracker.Shared.Features.Shared.Layout;
using ParkingTracker.Shared.Tests.Features.Shared.TestDoubles;
using ParkingTracker.ViewModels.Features.Shared;
using ParkingTracker.ViewModels.Features.Shared.Settings.Legacy;

namespace ParkingTracker.Shared.Tests.Features.Shared.Components;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Component tests for the shared building blocks: the icon, the toast, the dialog shell and the layout.
/// </summary>
public sealed class SharedComponentsTests : UiTestContext
{
	#region Icon Tests
	/// <summary>
	/// Verifies that an icon draws the outline of its name and that an unknown name draws the help icon.
	/// </summary>
	[Fact]
	public void Icon_Name_ShouldDrawItsOutlineOrTheHelpIcon()
	{
		var known = Render<Icon>(parameters => parameters.Add(icon => icon.Name, "pin"));
		var unknown = Render<Icon>(parameters => parameters.Add(icon => icon.Name, "does-not-exist"));
		var help = Render<Icon>(parameters => parameters.Add(icon => icon.Name, "help"));

		known.Find("svg.ic path").GetAttribute("d").Should().StartWith("M12 2C8.13 2 5 5.13 5 9");
		unknown.Find("path").GetAttribute("d").Should().Be(help.Find("path").GetAttribute("d"));
	}
	#endregion

	#region Toast Tests
	/// <summary>
	/// Verifies that the toast shows the message of the view model and hides after its time.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Toast_Message_ShouldShowThenHide()
	{
		var cut = Render<Toast>();
		cut.Find("#toast").ClassList.Should().Contain("hidden");

		await cut.InvokeAsync(() => Resolve<ToastViewModel>().Show("Car position saved"));
		cut.Find("#toast").ClassList.Should().NotContain("hidden");
		cut.Find("#toast").TextContent.Should().Be("Car position saved");
		await cut.InvokeAsync(() => Clock.Advance(ToastViewModel.DefaultDuration));

		await cut.WaitForAssertionAsync(() => cut.Find("#toast").ClassList.Should().Contain("hidden"));
	}

	/// <summary>
	/// Verifies that a toast text is shown as text, not as markup.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Toast_Markup_ShouldBeEscaped()
	{
		var cut = Render<Toast>();

		await cut.InvokeAsync(() => Resolve<ToastViewModel>().Show("<script>x</script>"));

		cut.Find("#toast").InnerHtml.Should().Contain("&lt;script&gt;");
	}
	#endregion

	#region Dialog Shell Tests
	/// <summary>
	/// Verifies that the shell draws its content only while open.
	/// </summary>
	[Fact]
	public void DialogShell_OpenAndClosed_ShouldDrawContentOnlyWhenOpen()
	{
		var closed = Render<DialogShell>(parameters => parameters.Add(shell => shell.IsOpen, false).AddChildContent("<p>inside</p>"));
		var open = Render<DialogShell>(parameters => parameters.Add(shell => shell.IsOpen, true).Add(shell => shell.Label, "Test").AddChildContent("<p>inside</p>"));

		closed.FindAll("p").Should().BeEmpty();
		open.Find(".sheet-dialog p").TextContent.Should().Be("inside");
		open.Find("[role=dialog]").GetAttribute("aria-label").Should().Be("Test");
	}

	/// <summary>
	/// Verifies that the layout draws the page and the toast.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task MainLayout_Body_ShouldDrawThePageAndTheToast()
	{
		var cut = Render<MainLayout>(parameters => parameters.Add(layout => layout.Body, builder => builder.AddMarkupContent(0, "<main>page</main>")));

		await cut.WaitForAssertionAsync(() => cut.Find("main").TextContent.Should().Be("page"));
		cut.Find("#toast").Should().NotBeNull();
	}

	/// <summary>
	/// Verifies that the layout shows the page only after the migration of the previous version's data finished, and always shows
	/// the toast.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task MainLayout_MigrationRunning_ShouldShowThePageOnlyWhenItFinished()
	{
		var release = new TaskCompletionSource<LegacyStorageReading>();
		LegacyPlain.ReadAsync(LegacyDataMigration.MarkerKey).Returns((string?)null);
		LegacyReader.ReadAsync(Arg.Any<CancellationToken>()).Returns(release.Task);

		var cut = Render<MainLayout>(parameters => parameters.Add(layout => layout.Body, builder => builder.AddMarkupContent(0, "<main>page</main>")));

		cut.FindAll("main").Should().BeEmpty();
		cut.Find("#toast").Should().NotBeNull();
		release.SetResult(LegacyStorageReading.Success(new Dictionary<string, string>(StringComparer.Ordinal), LegacyStorageReading.LevelDbReader));
		await cut.WaitForAssertionAsync(() => cut.Find("main").TextContent.Should().Be("page"));
	}

	/// <summary>
	/// Verifies that the installer registers the map script and rejects missing arguments.
	/// </summary>
	[Fact]
	public void Installer_ShouldRegisterTheMapScriptAndRejectNullArguments()
	{
		Services.GetService<ParkingTracker.Shared.Features.MapLayers.Services.MapScript>().Should().NotBeNull();

		var noServices = () => new ParkingTracker.Shared.UiServiceInstaller().Install(null!, new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build()); // null on purpose: proves the guard clause rejects it
		var noConfiguration = () => new ParkingTracker.Shared.UiServiceInstaller().Install(new Microsoft.Extensions.DependencyInjection.ServiceCollection(), null!); // null on purpose: proves the guard clause rejects it

		noServices.Should().Throw<ArgumentNullException>();
		noConfiguration.Should().Throw<ArgumentNullException>();
		ParkingTracker.Shared.AssemblyReference.Assembly.GetName().Name.Should().Be("ParkingTracker.Shared");
	}
	#endregion
}
