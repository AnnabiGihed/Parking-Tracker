namespace ParkingTracker.ViewModels.Features.Shared.Settings.Legacy;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The answer of an <see cref="ILegacyStorageReader"/>: either the values of the old storage (possibly none) or the
///              reason why the old storage could not be read. The two outcomes differ for the migration: "nothing there" is final,
///              "could not read" is tried again at the next launch.
/// </summary>
public sealed class LegacyStorageReading
{
	#region Fields
	/// <summary>
	/// The values of a successful read.
	/// </summary>
	private readonly Dictionary<string, string> values;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="LegacyStorageReading"/> class.
	/// </summary>
	/// <param name="values">The values by key.</param>
	/// <param name="problem">Why the read failed, or <see langword="null"/> when it succeeded.</param>
	private LegacyStorageReading(Dictionary<string, string> values, string? problem)
	{
		this.values = values;
		Problem = problem;
	}
	#endregion

	#region Properties
	/// <summary>
	/// Gets a value indicating whether the old storage was read, even when it held nothing.
	/// </summary>
	public bool Succeeded => Problem is null;

	/// <summary>
	/// Gets the values of the old storage by key, as the JSON text the old version wrote; empty after a failure.
	/// </summary>
	public IReadOnlyDictionary<string, string> Values => values;

	/// <summary>
	/// Gets why the read failed, or <see langword="null"/> when it succeeded.
	/// </summary>
	public string? Problem { get; }
	#endregion

	#region Factory Methods
	/// <summary>
	/// Creates the answer of a read that worked.
	/// </summary>
	/// <param name="values">The values by key; may be empty.</param>
	/// <returns>The successful answer.</returns>
	/// <exception cref="ArgumentNullException">Thrown when <paramref name="values"/> is <see langword="null"/>.</exception>
	public static LegacyStorageReading Success(IReadOnlyDictionary<string, string> values)
	{
		ArgumentNullException.ThrowIfNull(values);
		return new LegacyStorageReading(new Dictionary<string, string>(values, StringComparer.Ordinal), null);
	}

	/// <summary>
	/// Creates the answer of a read that failed.
	/// </summary>
	/// <param name="problem">What went wrong, for the log and the migration marker.</param>
	/// <returns>The failed answer.</returns>
	/// <exception cref="ArgumentException">Thrown when <paramref name="problem"/> is empty.</exception>
	public static LegacyStorageReading Failure(string problem)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(problem);
		return new LegacyStorageReading(new Dictionary<string, string>(StringComparer.Ordinal), problem);
	}
	#endregion
}
