namespace ParkingTracker.Application.Features.ProfileSync.Abstractions;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The moment each section of the profile last changed on this phone (ADR-0032), or <see langword="null"/> for a section that
///              never changed here (data from before the sync existed, or a section nobody ever set). A section that the user cleared
///              keeps its moment, which is how a clear wins over an older value on the server.
/// </summary>
/// <param name="Home">When the home place last changed.</param>
/// <param name="Work">When the work place last changed.</param>
/// <param name="Favourites">When the list of favourites last changed.</param>
/// <param name="Vehicle">When the vehicle profile last changed.</param>
/// <param name="Card">When the residents' card last changed.</param>
public sealed record SectionTimes(DateTimeOffset? Home, DateTimeOffset? Work, DateTimeOffset? Favourites, DateTimeOffset? Vehicle, DateTimeOffset? Card)
{
	#region Static Instances
	/// <summary>
	/// No section has a moment.
	/// </summary>
	public static readonly SectionTimes None = new(null, null, null, null, null);
	#endregion

	#region Public Methods
	/// <summary>
	/// Reads the moment of one section.
	/// </summary>
	/// <param name="section">The section.</param>
	/// <returns>The moment, or <see langword="null"/> when the section never changed here.</returns>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="section"/> is not a known section.</exception>
	public DateTimeOffset? Get(SyncSection section)
	{
		return section switch
		{
			SyncSection.Home => Home,
			SyncSection.Work => Work,
			SyncSection.Favourites => Favourites,
			SyncSection.Vehicle => Vehicle,
			SyncSection.Card => Card,
			_ => throw new ArgumentOutOfRangeException(nameof(section), section, "Unknown section."),
		};
	}

	/// <summary>
	/// Gives a copy with the moment of one section replaced.
	/// </summary>
	/// <param name="section">The section.</param>
	/// <param name="at">The new moment.</param>
	/// <returns>The copy.</returns>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="section"/> is not a known section.</exception>
	public SectionTimes With(SyncSection section, DateTimeOffset? at)
	{
		return section switch
		{
			SyncSection.Home => this with { Home = at },
			SyncSection.Work => this with { Work = at },
			SyncSection.Favourites => this with { Favourites = at },
			SyncSection.Vehicle => this with { Vehicle = at },
			SyncSection.Card => this with { Card = at },
			_ => throw new ArgumentOutOfRangeException(nameof(section), section, "Unknown section."),
		};
	}

	/// <summary>
	/// Tells whether two sets of moments are the same instants in the precision the database stores, whatever their offsets.
	/// </summary>
	/// <param name="other">The other set.</param>
	/// <returns><see langword="true"/> when every section has the same moment in both.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="other"/> is <see langword="null"/>.</exception>
	public bool SameAs(SectionTimes other)
	{
		ArgumentNullException.ThrowIfNull(other);
		return Enum.GetValues<SyncSection>().All(section => Millis(Get(section)) == Millis(other.Get(section)));
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Gives a moment in the precision the database stores.
	/// </summary>
	/// <param name="at">The moment.</param>
	/// <returns>Milliseconds since 1970, or <see langword="null"/>.</returns>
	private static long? Millis(DateTimeOffset? at) => at?.ToUnixTimeMilliseconds();
	#endregion
}
