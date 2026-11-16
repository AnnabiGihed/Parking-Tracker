using System.Reflection;
using ParkingTracker.Application.ReferenceData;
using ParkingTracker.Domain.ReferenceData;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Infrastructure.ReferenceData;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Loads the reference data JSON files embedded in the assembly once and serves them through
///              <see cref="IReferenceData"/>; a missing or invalid file is reported as a failure, not an exception.
/// </summary>
public sealed class ReferenceDataProvider : IReferenceData
{
	#region Constants
	/// <summary>
	/// File name of the City of Brussels sectors resource.
	/// </summary>
	private const string CitySectorsFile = "city-sectors.json";
	/// <summary>
	/// File name of the resource with the sectors that carry tolerance agreements.
	/// </summary>
	private const string ToleranceSectorsFile = "tolerance-sectors.json";
	/// <summary>
	/// File name of the municipalities resource.
	/// </summary>
	private const string MunicipalitiesFile = "municipalities.json";
	/// <summary>
	/// Prefix of the embedded resource names; it must match the <c>LogicalName</c> of the Data files in the Infrastructure csproj.
	/// </summary>
	private const string ResourcePrefix = "ParkingTracker.Infrastructure.Data.";
	#endregion

	#region Fields
	private readonly Lazy<Result<ReferenceDataSet>> data;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="ReferenceDataProvider"/> class that reads the files embedded in the Infrastructure assembly.
	/// </summary>
	public ReferenceDataProvider()
		: this(AssemblyReference.Assembly)
	{
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="ReferenceDataProvider"/> class that reads the files embedded in the given assembly; tests use it to exercise the failure path.
	/// </summary>
	/// <param name="assembly">The assembly that embeds the files.</param>
	/// <exception cref="ArgumentNullException"><paramref name="assembly"/> is <see langword="null"/>.</exception>
	internal ReferenceDataProvider(Assembly assembly)
	{
		ArgumentNullException.ThrowIfNull(assembly);
		data = new Lazy<Result<ReferenceDataSet>>(() => Load(assembly));
	}
	#endregion

	#region IReferenceData Implementation
	/// <inheritdoc />
	public Result<ReferenceDataSet> GetReferenceData() => data.Value;
	#endregion

	#region Private Helpers
	private static Result<ReferenceDataSet> Load(Assembly assembly)
	{
		var municipalities = ReadResource(assembly, MunicipalitiesFile, ReferenceDataReader.ReadMunicipalities);
		if (municipalities.IsFailure)
		{
			return Result.Failure<ReferenceDataSet>(municipalities.Error);
		}

		var citySectors = ReadResource(assembly, CitySectorsFile, json => ReferenceDataReader.ReadSectors(json, CitySectorsFile));
		if (citySectors.IsFailure)
		{
			return Result.Failure<ReferenceDataSet>(citySectors.Error);
		}

		var toleranceSectors = ReadResource(assembly, ToleranceSectorsFile, json => ReferenceDataReader.ReadSectors(json, ToleranceSectorsFile));
		if (toleranceSectors.IsFailure)
		{
			return Result.Failure<ReferenceDataSet>(toleranceSectors.Error);
		}

		return Result.Success(new ReferenceDataSet(municipalities.Value, citySectors.Value, toleranceSectors.Value));
	}

	private static Result<T> ReadResource<T>(Assembly assembly, string fileName, Func<string, Result<T>> read)
	{
		using var stream = assembly.GetManifestResourceStream(ResourcePrefix + fileName);
		if (stream is null)
		{
			return Result.Failure<T>(ReferenceDataReader.Missing(fileName));
		}

		using var reader = new StreamReader(stream);
		return read(reader.ReadToEnd());
	}
	#endregion
}
