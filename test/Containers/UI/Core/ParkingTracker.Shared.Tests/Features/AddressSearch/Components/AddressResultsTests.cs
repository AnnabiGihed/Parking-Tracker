using Bunit;
using ParkingTracker.Domain.Features.AddressSearch.ValueObjects;
using ParkingTracker.Domain.Features.Shared.ValueObjects;
using ParkingTracker.Shared.Features.AddressSearch.Components;
using ParkingTracker.Shared.Tests.Features.Shared.TestDoubles;
using ParkingTracker.ViewModels.Features.Shared;

namespace ParkingTracker.Shared.Tests.Features.AddressSearch.Components;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Component tests for <see cref="AddressResults"/>: one row per match with its label and locality, a tap picks the
///              match, a match without a label shows its full name, and the back button closes the list and only while it is shown.
/// </summary>
public sealed class AddressResultsTests : UiTestContext
{
	#region Static Instances
	/// <summary>
	/// A match with a label and a locality.
	/// </summary>
	private static readonly AddressMatch Loi = new(new GeoPoint(50.8467, 4.3525), "Rue de la Loi, Bruxelles", "Rue de la Loi", "1000 Bruxelles");

	/// <summary>
	/// A match that only has a full name.
	/// </summary>
	private static readonly AddressMatch Forest = new(new GeoPoint(50.8333, 4.3333), "Parc de Forest, Belgique");
	#endregion

	#region Rendering Tests
	/// <summary>
	/// Verifies that every match is a row with the label in bold and the locality below, and that a match without a label shows its
	/// full name and no locality line.
	/// </summary>
	[Fact]
	public void Render_Matches_ShouldShowARowForEach()
	{
		var cut = Render<AddressResults>(parameters => parameters.Add(component => component.Matches, [Loi, Forest]));

		var rows = cut.FindAll("#addressResults .address-result");
		rows.Should().HaveCount(2);
		rows[0].QuerySelector(".address-result-label")!.TextContent.Should().Be("Rue de la Loi");
		rows[0].QuerySelector(".address-result-locality")!.TextContent.Should().Be("1000 Bruxelles");
		rows[1].QuerySelector(".address-result-label")!.TextContent.Should().Be("Parc de Forest, Belgique");
		rows[1].QuerySelector(".address-result-locality").Should().BeNull();
		cut.Find("#addressResults").GetAttribute("aria-label").Should().Be("Matching addresses");
	}
	#endregion

	#region Pick Tests
	/// <summary>
	/// Verifies that a tap on a row gives the match to the owner.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Click_Row_ShouldPickThatMatch()
	{
		AddressMatch? picked = null;
		var cut = Render<AddressResults>(parameters => parameters
			.Add(component => component.Matches, [Loi, Forest])
			.Add(component => component.OnPick, match => picked = match));

		await cut.FindAll(".address-result")[1].ClickAsync();

		picked.Should().Be(Forest);
	}
	#endregion

	#region Back Button Tests
	/// <summary>
	/// Verifies that the back button asks the owner to close the list, and that the registration is gone afterwards.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Back_ListOpen_ShouldAskToClose()
	{
		var closed = 0;
		var cut = Render<AddressResults>(parameters => parameters
			.Add(component => component.Matches, [Loi, Forest])
			.Add(component => component.OnClose, () => closed++));
		var back = Resolve<IBackNavigation>();
		back.CanGoBack.Should().BeTrue();

		var handled = await cut.InvokeAsync(back.TryGoBack);

		handled.Should().BeTrue();
		await cut.WaitForAssertionAsync(() => closed.Should().Be(1));
		back.CanGoBack.Should().BeFalse();
	}

	/// <summary>
	/// Verifies that the back button no longer closes anything once the list was disposed.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Dispose_List_ShouldReleaseTheBackButton()
	{
		Render<AddressResults>(parameters => parameters.Add(component => component.Matches, [Loi]));

		await DisposeComponentsAsync();

		Resolve<IBackNavigation>().CanGoBack.Should().BeFalse();
	}
	#endregion
}
