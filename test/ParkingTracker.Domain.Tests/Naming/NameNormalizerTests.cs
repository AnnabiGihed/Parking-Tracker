using ParkingTracker.Domain.Naming;

namespace ParkingTracker.Domain.Tests.Naming;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Unit tests for <see cref="NameNormalizer"/>, with the results of <c>normalize</c>, <c>nameParts</c> and <c>titleCase</c> of live.js.
/// </summary>
public sealed class NameNormalizerTests
{
	#region Normalize Tests
	/// <summary>
	/// Verifies that accents, case, apostrophes, hyphens, dots and repeated spaces are removed.
	/// </summary>
	/// <param name="name">The name to normalise.</param>
	/// <param name="expected">The expected result.</param>
	[Theory]
	[InlineData("Rue de l'Étuve", "rue de l etuve")]
	[InlineData("  Avenue   Louise ", "avenue louise")]
	[InlineData("Boulevard Saint-Michel", "boulevard saint michel")]
	[InlineData("Rue d’Argent", "rue d argent")]
	[InlineData("Av. de la Toison d`Or", "av de la toison d or")]
	[InlineData("Chaussée de Wavre", "chaussee de wavre")]
	[InlineData("", "")]
	public void Normalize_Name_ShouldRemoveAccentsCaseAndPunctuation(string name, string expected)
	{
		NameNormalizer.Normalize(name).Should().Be(expected);
	}

	/// <summary>
	/// Verifies that a missing name is read as empty.
	/// </summary>
	[Fact]
	public void Normalize_Null_ShouldReturnEmpty()
	{
		NameNormalizer.Normalize(null).Should().BeEmpty();
	}
	#endregion

	#region NameParts Tests
	/// <summary>
	/// Verifies that a bilingual name is split on " - " into its two normalised halves.
	/// </summary>
	[Fact]
	public void NameParts_BilingualName_ShouldReturnBothLanguages()
	{
		var parts = NameNormalizer.NameParts("Avenue Louise - Louizalaan");

		parts.Should().Equal("avenue louise", "louizalaan");
	}

	/// <summary>
	/// Verifies that a semicolon also separates the parts.
	/// </summary>
	[Fact]
	public void NameParts_SemicolonSeparated_ShouldReturnBothParts()
	{
		NameNormalizer.NameParts("Rue Haute;Hoogstraat").Should().Equal("rue haute", "hoogstraat");
	}

	/// <summary>
	/// Verifies that a hyphen inside a word does not split the name.
	/// </summary>
	[Fact]
	public void NameParts_HyphenWithoutSpaces_ShouldStayOnePart()
	{
		NameNormalizer.NameParts("Rue Sainte-Catherine").Should().Equal("rue sainte catherine");
	}

	/// <summary>
	/// Verifies that empty parts are dropped and a missing name gives no parts.
	/// </summary>
	/// <param name="name">The name.</param>
	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData(" ; ")]
	public void NameParts_EmptyName_ShouldReturnNoParts(string? name)
	{
		NameNormalizer.NameParts(name).Should().BeEmpty();
	}
	#endregion

	#region TitleCase Tests
	/// <summary>
	/// Verifies that each word, also after a hyphen, starts with a capital and the rest is lower case.
	/// </summary>
	/// <param name="name">The name.</param>
	/// <param name="expected">The expected result.</param>
	[Theory]
	[InlineData("SAINT-GILLES", "Saint-Gilles")]
	[InlineData("ixelles", "Ixelles")]
	[InlineData("woluwe-saint-lambert", "Woluwe-Saint-Lambert")]
	[InlineData("ville de bruxelles", "Ville De Bruxelles")]
	[InlineData("", "")]
	public void TitleCase_Name_ShouldCapitaliseEachWord(string name, string expected)
	{
		NameNormalizer.TitleCase(name).Should().Be(expected);
	}

	/// <summary>
	/// Verifies that a missing name gives an empty text.
	/// </summary>
	[Fact]
	public void TitleCase_Null_ShouldReturnEmpty()
	{
		NameNormalizer.TitleCase(null).Should().BeEmpty();
	}
	#endregion
}
