namespace ParkingTracker.Domain.Geometry;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Decides which parking.brussels street surfaces nobody can park on: tunnels, viaducts, underpasses and
///              bridges run under or over other streets and would otherwise hide the street above or below them.
/// </summary>
public static class SurfaceFilter
{
	#region Static Instances
	/// <summary>
	/// The words a French street name starts with when the surface is a tunnel, viaduct, underpass or bridge. Both
	/// spellings of trémie are listed because the comparison ignores case but not accents.
	/// </summary>
	private static readonly string[] NotParkableWords = ["tunnel", "viaduc", "trémie", "tremie", "pont"];
	#endregion

	#region Public Methods
	/// <summary>
	/// Determines whether a street surface can be parked on, from its French name. The match ignores case with the
	/// invariant culture, so it gives the same answer on a phone set to Turkish, where a culture-sensitive comparison
	/// would not match <c>VIADUC</c>.
	/// </summary>
	/// <param name="frenchName">The French street name of the surface; <see langword="null"/> is read as empty.</param>
	/// <returns><see langword="false"/> for a tunnel, viaduct, underpass or bridge; otherwise <see langword="true"/>.</returns>
	public static bool IsParkable(string? frenchName)
	{
		var name = (frenchName ?? string.Empty).Trim();
		return !NotParkableWords.Any(word => StartsWithWord(name, word));
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Checks that a name starts with a word and that the word ends there. Only an ASCII letter, digit or underscore
	/// continues a word, like the word boundary of the web app, so an accented letter right after the word still ends it.
	/// </summary>
	private static bool StartsWithWord(string name, string word)
	{
		return name.StartsWith(word, StringComparison.OrdinalIgnoreCase)
			&& (name.Length == word.Length || !IsAsciiWordCharacter(name[word.Length]));
	}

	private static bool IsAsciiWordCharacter(char character) => char.IsAsciiLetterOrDigit(character) || character == '_';
	#endregion
}
