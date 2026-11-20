using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using ParkingTracker.Shared.Features.Shared.Dialogs;
using ParkingTracker.Shared.Tests.Features.Shared.TestDoubles;
using ParkingTracker.ViewModels.Features.Shared;
using Radzen;
using Radzen.Blazor;

namespace ParkingTracker.Shared.Tests.Features.Shared.Dialogs;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Component tests for <see cref="DialogBridge"/>: a view model that opens gets a Radzen dialog with the content of the
///              bridge, a view model that closes closes it, a close by the user (the service closes the dialog) reaches the view model,
///              and the back button closes the dialog through the view model.
/// </summary>
public sealed class DialogBridgeTests : UiTestContext
{
	#region Test Infrastructure
	/// <summary>
	/// Renders the Radzen host and a bridge over a fake view model.
	/// </summary>
	/// <param name="model">The view model the bridge drives.</param>
	/// <returns>The Radzen dialog host, which draws the dialog.</returns>
	private IRenderedComponent<RadzenDialog> RenderBridge(FakeDialogViewModel model)
	{
		var host = Render<RadzenDialog>();
		Render<DialogBridge>(parameters => parameters
			.Add(bridge => bridge.ViewModel, model)
			.Add(bridge => bridge.Title, "Fake")
			.Add(bridge => bridge.ChildContent, (RenderFragment)(builder => builder.AddMarkupContent(0, "<p id=\"fakeBody\">Hello</p>"))));
		return host;
	}
	#endregion

	#region Open Tests
	/// <summary>
	/// Verifies that nothing is drawn while the view model is closed and that the dialog opens with the content when it opens.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task ViewModel_Opens_ShouldShowTheDialogWithTheContent()
	{
		var model = new FakeDialogViewModel();
		var host = RenderBridge(model);
		host.FindAll("#fakeBody").Should().BeEmpty();

		await host.InvokeAsync(model.Open);

		await host.WaitForAssertionAsync(() => host.Find("#fakeBody").TextContent.Should().Be("Hello"));
		host.Find(".rz-dialog-title").TextContent.Should().Be("Fake");
		Resolve<IBackNavigation>().CanGoBack.Should().BeTrue();
	}

	/// <summary>
	/// Verifies that a view model that is already open when the bridge renders gets its dialog at once.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Render_ViewModelAlreadyOpen_ShouldShowTheDialog()
	{
		var model = new FakeDialogViewModel();
		model.Open();

		var host = RenderBridge(model);

		await host.WaitForAssertionAsync(() => host.FindAll("#fakeBody").Should().HaveCount(1));
	}
	#endregion

	#region Close Tests
	/// <summary>
	/// Verifies that the dialog closes when the view model closes, without the view model being closed a second time.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task ViewModel_Closes_ShouldCloseTheDialog()
	{
		var model = new FakeDialogViewModel();
		var host = RenderBridge(model);
		await host.InvokeAsync(model.Open);
		await host.WaitForAssertionAsync(() => host.FindAll("#fakeBody").Should().HaveCount(1));

		await host.InvokeAsync(model.Close);

		await host.WaitForAssertionAsync(() => host.FindAll("#fakeBody").Should().BeEmpty());
		model.CloseCalls.Should().Be(1);
		Resolve<IBackNavigation>().CanGoBack.Should().BeFalse();
	}

	/// <summary>
	/// Verifies that a close by the user reaches the view model.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Dialog_ClosedByTheUser_ShouldCloseTheViewModel()
	{
		var model = new FakeDialogViewModel();
		var host = RenderBridge(model);
		await host.InvokeAsync(model.Open);
		await host.WaitForAssertionAsync(() => host.FindAll("#fakeBody").Should().HaveCount(1));

		await host.InvokeAsync(() => Services.GetRequiredService<DialogService>().Close());

		model.IsOpen.Should().BeFalse();
		model.CloseCalls.Should().Be(1);
		await host.WaitForAssertionAsync(() => host.FindAll("#fakeBody").Should().BeEmpty());
	}

	/// <summary>
	/// Verifies that the back button closes the dialog through the view model.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task BackButton_DialogOpen_ShouldCloseTheViewModel()
	{
		var model = new FakeDialogViewModel();
		var host = RenderBridge(model);
		await host.InvokeAsync(model.Open);
		await host.WaitForAssertionAsync(() => host.FindAll("#fakeBody").Should().HaveCount(1));

		var closed = await host.InvokeAsync(Resolve<IBackNavigation>().TryGoBack);

		closed.Should().BeTrue();
		model.IsOpen.Should().BeFalse();
		await host.WaitForAssertionAsync(() => host.FindAll("#fakeBody").Should().BeEmpty());
	}
	#endregion
}
