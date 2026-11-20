using System.Globalization;
using System.Text.RegularExpressions;
using Bunit;
using ParkingTracker.Shared.Features.Card.Components;
using ParkingTracker.Shared.Features.Places.Components;
using ParkingTracker.Shared.Tests.Features.Shared.TestDoubles;
using ParkingTracker.ViewModels.Features.Card;

namespace ParkingTracker.Shared.Tests.Features.Shared.Styles;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Tests that tie the rendered components to the shipped stylesheets, for the faults a component test cannot see on its
///              own: a date picker of the card dialog whose calendar opens behind the dialog, and Radzen "Light" buttons (the Home
///              and Work chips) whose fill ignores the colours of the app. Each test renders the component, checks that it carries
///              the class the rule is written for, and then reads the rule in the CSS copied next to the test assembly.
/// </summary>
public sealed partial class StylesheetTests : UiTestContext
{
	#region Constants
	/// <summary>
	/// The z-index Radzen.Blazor.js gives a popup it moves to the end of the page (date picker calendar, drop-down list).
	/// </summary>
	private const int RadzenPopupLayer = 2000;
	#endregion

	#region Static Instances
	/// <summary>
	/// The longest a regular expression may take on a stylesheet.
	/// </summary>
	private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(1);
	#endregion

	#region Test Infrastructure
	/// <summary>
	/// Reads one of the stylesheets of <c>ParkingTracker.Shared</c> that the test project copies to its output.
	/// </summary>
	/// <param name="fileName">The file name in <c>wwwroot/css</c>.</param>
	/// <returns>The content of the stylesheet.</returns>
	private static string ReadStylesheet(string fileName) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Styles", fileName));

	/// <summary>
	/// Returns the declarations of the first rule whose selector list is exactly <paramref name="selector"/>.
	/// </summary>
	/// <param name="css">The stylesheet.</param>
	/// <param name="selector">The selector of the rule.</param>
	/// <returns>The text between the braces of the rule.</returns>
	private static string RuleOf(string css, string selector)
	{
		var match = Regex.Match(css, @"(?m)^" + Regex.Escape(selector) + @"\s*\{(?<body>[^}]*)\}", RegexOptions.None, MatchTimeout);
		match.Success.Should().BeTrue($"the stylesheet should have a rule for '{selector}'");
		return match.Groups["body"].Value;
	}

	/// <summary>
	/// Reads the z-index of the first rule whose selector list is exactly <paramref name="selector"/>.
	/// </summary>
	/// <param name="css">The stylesheet.</param>
	/// <param name="selector">The selector of the rule.</param>
	/// <returns>The z-index.</returns>
	private static int LayerOf(string css, string selector)
	{
		return int.Parse(ZIndexDeclaration().Match(RuleOf(css, selector)).Groups["value"].Value, CultureInfo.InvariantCulture);
	}

	/// <summary>
	/// Matches the z-index declaration of a rule.
	/// </summary>
	/// <returns>The expression, with the value in the group <c>value</c>.</returns>
	[GeneratedRegex(@"z-index:\s*(?<value>\d+)", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
	private static partial Regex ZIndexDeclaration();
	#endregion

	#region Layer Tests
	/// <summary>
	/// Verifies that the date pickers of the card dialog sit in the dialog backdrop and that the backdrop stays under the layer of
	/// the Radzen popups, so that the calendar opens in front of the dialog and a date can be picked.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task DialogBackdrop_CardDialogOpen_ShouldStayUnderTheRadzenPopupLayer()
	{
		var cut = Render<CardDialog>();
		await cut.InvokeAsync(() => Resolve<CardViewModel>().OpenAsync());
		cut.FindAll(".dialog-backdrop .rz-datepicker").Should().HaveCount(2);

		var rule = RuleOf(ReadStylesheet("style.css"), ".dialog-backdrop");

		var zIndex = int.Parse(ZIndexDeclaration().Match(rule).Groups["value"].Value, CultureInfo.InvariantCulture);
		zIndex.Should().BeLessThan(RadzenPopupLayer);
	}

	/// <summary>
	/// Verifies the order of the layers (ADR-0030): the Radzen mask and dialog sit above the search bar and the dialogs of the app, the
	/// toast sits above the Radzen popups, and the map stays at the bottom.
	/// </summary>
	[Fact]
	public void Layers_RadzenDialogsAndToast_ShouldKeepTheirOrder()
	{
		var theme = RuleOf(ReadStylesheet("radzen-theme.css"), ":root");
		var css = ReadStylesheet("style.css");

		theme.Should().MatchRegex(@"--rz-dialog-mask-zindex:\s*1500;").And.MatchRegex(@"--rz-dialog-zindex:\s*1501;");
		LayerOf(css, ".toast").Should().Be(2100).And.BeGreaterThan(RadzenPopupLayer);
		LayerOf(css, "#map").Should().Be(0);
		LayerOf(css, ".top").Should().BeLessThan(LayerOf(css, ".dialog-backdrop"));
	}
	#endregion

	#region Theme Tests
	/// <summary>
	/// Verifies that the fill of the Radzen "Light" buttons comes from a colour of the app, which changes with the dark scheme,
	/// instead of the fixed near-white grey of the Radzen theme.
	/// </summary>
	[Fact]
	public void RadzenTheme_LightFill_ShouldFollowTheColoursOfTheApp()
	{
		var rule = RuleOf(ReadStylesheet("radzen-theme.css"), ":root");

		rule.Should().MatchRegex(@"--rz-base-200:\s*var\(--surface-2\);");
	}

	/// <summary>
	/// Verifies that the Home and Work chips carry the classes the stylesheet styles, and that the rule gives them the surface
	/// and text colours of the app with a shadow, like the car chip, and lets them be tapped inside the top bar.
	/// </summary>
	[Fact]
	public void PlaceChips_Style_ShouldUseTheColoursOfTheApp()
	{
		var chips = Render<PlaceChips>();
		chips.FindAll(".place-chips .rz-chip.place-go").Should().HaveCount(2);
		var css = ReadStylesheet("style.css");

		var chip = RuleOf(css, ".place-chips .rz-chip.place-go, .place-chips .rz-chip.place-go:not(.rz-state-disabled):hover, .place-chips .rz-chip.place-go:not(.rz-state-disabled):active");
		var group = RuleOf(css, ".place-chips");

		chip.Should().Contain("background-color: var(--surface);").And.Contain("color: var(--fg);").And.Contain("box-shadow: var(--shadow);");
		group.Should().Contain("pointer-events: auto;");
	}
	#endregion
}
