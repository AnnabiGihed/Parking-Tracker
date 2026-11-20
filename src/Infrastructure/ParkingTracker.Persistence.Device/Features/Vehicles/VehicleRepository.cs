using System.Text.Json;
using Microsoft.Extensions.Logging;
using ParkingTracker.Application.Features.Shared.Abstractions;
using ParkingTracker.Application.Features.Vehicles.Abstractions;
using ParkingTracker.Domain.Features.Vehicles.Aggregates;
using ParkingTracker.Domain.Features.Vehicles.ValueObjects;
using ParkingTracker.Persistence.Device.Features.Shared.Database;
using ParkingTracker.Persistence.Device.Features.Shared.Encryption;
using Pivot.Framework.Domain.Shared;
using SQLite;

namespace ParkingTracker.Persistence.Device.Features.Vehicles;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The hand-written repository of the vehicle profile over sqlite-net (ADR-0026, ADR-0031). It maps the aggregate to a
///              <see cref="VehicleRow"/> and back, encrypting the plate with <see cref="FieldCipher"/> bound to its column and to the
///              identifier of the profile, and rebuilds the profile through <see cref="Vehicle.Restore"/>, so that a row that breaks a rule gives a failed <see cref="Result"/> and never an invalid aggregate. A plate that
///              cannot be decrypted fails the read, so that a profile is never returned with its plate silently dropped; a key that
///              cannot be read now is the failure <see cref="DeviceDataErrors.KeyUnavailable"/>, not "no profile". Changes are only
///              staged in the scope of the operation; the unit of work applies them. The phone holds one profile: an add does nothing
///              when a profile exists, an update replaces it.
/// </summary>
public sealed partial class VehicleRepository : IVehicleRepository
{
	#region Constants
	/// <summary>
	/// The name of the table, for the errors and the binding of the encrypted field.
	/// </summary>
	private const string Table = "vehicles";

	/// <summary>
	/// The name of the column of the encrypted plate.
	/// </summary>
	private const string PlateColumn = "plate";
	#endregion

	#region Fields
	/// <summary>
	/// The database that holds the table.
	/// </summary>
	private readonly DeviceDatabase database;

	/// <summary>
	/// Encrypts the plate.
	/// </summary>
	private readonly FieldCipher cipher;

	/// <summary>
	/// The log.
	/// </summary>
	private readonly ILogger<VehicleRepository> logger;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="VehicleRepository"/> class.
	/// </summary>
	/// <param name="database">The database that holds the table.</param>
	/// <param name="cipher">Encrypts the plate.</param>
	/// <param name="logger">The log.</param>
	/// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
	public VehicleRepository(DeviceDatabase database, FieldCipher cipher, ILogger<VehicleRepository> logger)
	{
		ArgumentNullException.ThrowIfNull(database);
		ArgumentNullException.ThrowIfNull(cipher);
		ArgumentNullException.ThrowIfNull(logger);
		this.database = database;
		this.cipher = cipher;
		this.logger = logger;
	}
	#endregion

	#region Logging
	/// <summary>
	/// Logs a plate that could not be read, which fails the read of the profile.
	/// </summary>
	/// <param name="logger">The log.</param>
	/// <param name="code">The code of the error.</param>
	[LoggerMessage(Level = LogLevel.Warning, Message = "The plate of the vehicle profile cannot be decrypted ({Code}); the profile is not read")]
	private static partial void LogPlateUnreadable(ILogger logger, string code);

	/// <summary>
	/// Logs a read that failed.
	/// </summary>
	/// <param name="logger">The log.</param>
	/// <param name="exception">What the database reported.</param>
	[LoggerMessage(Level = LogLevel.Error, Message = "Reading the vehicle profile failed")]
	private static partial void LogReadFailed(ILogger logger, Exception exception);
	#endregion

	#region IVehicleRepository Implementation
	/// <inheritdoc />
	public async Task<Result<Vehicle>> GetAsync(CancellationToken cancellationToken = default)
	{
		List<VehicleRow> rows;
		try
		{
			rows = await database.Connection.QueryAsync<VehicleRow>("SELECT * FROM vehicles LIMIT 1").ConfigureAwait(false);
		}
		catch (SQLiteException exception)
		{
			LogReadFailed(logger, exception);
			return Result.Failure<Vehicle>(DeviceDatabaseErrors.ReadFailed);
		}

		if (rows.Count == 0)
		{
			return Result.Failure<Vehicle>(DeviceDataErrors.NotStored, ResultExceptionType.NotFound);
		}

		return await RebuildAsync(rows[0], cancellationToken).ConfigureAwait(false);
	}

	/// <inheritdoc />
	public async Task<Result> AddAsync(IDeviceChangeScope scope, Vehicle vehicle, CancellationToken cancellationToken = default)
	{
		var target = DeviceChangeScope.From(scope);
		ArgumentNullException.ThrowIfNull(vehicle);
		var row = await ToRowAsync(vehicle, cancellationToken).ConfigureAwait(false);
		if (row.IsFailure)
		{
			return Result.Failure(row.Error, row.ResultExceptionType);
		}

		target.Stage(connection =>
		{
			if (connection.ExecuteScalar<int>("SELECT COUNT(*) FROM vehicles") == 0)
			{
				_ = connection.Insert(row.Value);
			}
		});
		return Result.Success();
	}

	/// <inheritdoc />
	public async Task<Result> UpdateAsync(IDeviceChangeScope scope, Vehicle vehicle, CancellationToken cancellationToken = default)
	{
		var target = DeviceChangeScope.From(scope);
		ArgumentNullException.ThrowIfNull(vehicle);
		var row = await ToRowAsync(vehicle, cancellationToken).ConfigureAwait(false);
		if (row.IsFailure)
		{
			return Result.Failure(row.Error, row.ResultExceptionType);
		}

		target.Stage(connection =>
		{
			_ = connection.Execute("DELETE FROM vehicles");
			_ = connection.Insert(row.Value);
		});
		return Result.Success();
	}

	/// <inheritdoc />
	public void Remove(IDeviceChangeScope scope) => DeviceChangeScope.From(scope).Stage(connection => connection.Execute("DELETE FROM vehicles"));

	/// <inheritdoc />
	public void ClearPlate(IDeviceChangeScope scope) => DeviceChangeScope.From(scope).Stage(connection => connection.Execute("UPDATE vehicles SET plate = NULL WHERE plate IS NOT NULL"));
	#endregion

	#region Private Helpers
	/// <summary>
	/// Maps a profile to its row, encrypting the plate.
	/// </summary>
	/// <param name="vehicle">The profile.</param>
	/// <param name="cancellationToken">Cancels the encryption.</param>
	/// <returns>A task with the row, or the failure of the encryption.</returns>
	private async Task<Result<VehicleRow>> ToRowAsync(Vehicle vehicle, CancellationToken cancellationToken)
	{
		var id = vehicle.Id.Value.ToString("D");
		string? plate = null;
		if (vehicle.Plate is { } licencePlate)
		{
			var sealedPlate = await cipher.EncryptAsync(licencePlate.Value, new FieldBinding(Table, PlateColumn, id), cancellationToken).ConfigureAwait(false);
			if (sealedPlate.IsFailure)
			{
				return Result.Failure<VehicleRow>(sealedPlate.Error, sealedPlate.ResultExceptionType);
			}

			plate = sealedPlate.Value;
		}

		return new VehicleRow
		{
			Id = id,
			Make = vehicle.Make,
			Model = vehicle.Model,
			ModelYear = vehicle.Year,
			Fuel = (int)vehicle.Fuel,
			EuroNorm = (int)vehicle.EuroNorm,
			Connectors = JsonSerializer.Serialize(vehicle.Connectors.Select(connector => (int)connector)),
			BatteryKwh = vehicle.BatteryKwh,
			RangeKm = vehicle.RangeKm,
			Plate = plate,
		};
	}

	/// <summary>
	/// Rebuilds the aggregate from a row through its restore factory.
	/// </summary>
	/// <param name="row">The stored row.</param>
	/// <param name="cancellationToken">Cancels the decryption.</param>
	/// <returns>A task with the profile, or the rule or the failure of the decryption that stops the read.</returns>
	private async Task<Result<Vehicle>> RebuildAsync(VehicleRow row, CancellationToken cancellationToken)
	{
		if (!Guid.TryParse(row.Id, out var id) || id == Guid.Empty)
		{
			return Result.Failure<Vehicle>(DeviceDatabaseErrors.RowInvalid(Table, "the identifier cannot be read"));
		}

		if (!TryParseConnectors(row.Connectors, out var connectors))
		{
			return Result.Failure<Vehicle>(DeviceDatabaseErrors.RowInvalid(Table, "the list of plugs cannot be read"));
		}

		var plate = await DecryptPlateAsync(row, cancellationToken).ConfigureAwait(false);
		if (plate.IsFailure)
		{
			return Result.Failure<Vehicle>(plate.Error, plate.ResultExceptionType);
		}

		var charging = connectors.Length == 0 && row.BatteryKwh is null && row.RangeKm is null ? null : new ChargingDetails(connectors, row.BatteryKwh, row.RangeKm);
		var details = new VehicleDetails(row.Make, row.Model, row.ModelYear, (FuelType)row.Fuel, (EuroNorm)row.EuroNorm, charging, plate.Value.Length == 0 ? null : plate.Value);
		return Vehicle.Restore(new VehicleId(id), details);
	}

	/// <summary>
	/// Decrypts the plate of a row; a row without a plate gives an empty text, a plate that cannot be decrypted fails.
	/// </summary>
	/// <param name="row">The stored row.</param>
	/// <param name="cancellationToken">Cancels the decryption.</param>
	/// <returns>A task with the plate, an empty text when none is stored, or the failure of the decryption.</returns>
	private async Task<Result<string>> DecryptPlateAsync(VehicleRow row, CancellationToken cancellationToken)
	{
		if (row.Plate is null)
		{
			return Result.Success(string.Empty);
		}

		var decrypted = await cipher.DecryptAsync(row.Plate, new FieldBinding(Table, PlateColumn, row.Id), cancellationToken).ConfigureAwait(false);
		if (decrypted.IsFailure)
		{
			LogPlateUnreadable(logger, decrypted.Error.Code);
		}

		return decrypted;
	}

	/// <summary>
	/// Reads the JSON array of plug numbers; the numbers are not checked here, the aggregate refuses one it does not know.
	/// </summary>
	/// <param name="json">The stored text.</param>
	/// <param name="connectors">The plugs when the text is valid.</param>
	/// <returns><see langword="true"/> when the text is an array of whole numbers.</returns>
	private static bool TryParseConnectors(string json, out ChargingConnector[] connectors)
	{
		connectors = [];
		try
		{
			connectors = [.. (JsonSerializer.Deserialize<int[]>(json) ?? []).Select(number => (ChargingConnector)number)];
			return true;
		}
		catch (JsonException)
		{
			return false;
		}
	}
	#endregion
}
