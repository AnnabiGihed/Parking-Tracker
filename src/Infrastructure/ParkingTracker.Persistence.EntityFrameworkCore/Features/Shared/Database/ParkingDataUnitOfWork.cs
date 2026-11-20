using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pivot.Framework.Domain.Repositories;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Persistence.EntityFrameworkCore.Features.Shared.Database;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The unit of work of the server context: it applies the staged changes in one save and turns a database failure into
///              a failed <see cref="Result"/>, so a handler answers instead of throwing; the type of the exception is logged as a warning, never
///              its message or values, which can hold data of a user. It is deliberately not Pivot's
///              UnitOfWork of the context: that one needs the outbox publisher, and no aggregate of the server raises a domain event
///              yet (ADR-0012). When the first one does, this class is replaced by a subclass of Pivot's unit of work with the outbox.
/// </summary>
internal sealed partial class ParkingDataUnitOfWork : IUnitOfWork
{
	#region Static Instances
	/// <summary>
	/// The error of a save that lost a race with another change of the same row.
	/// </summary>
	public static readonly Error ConcurrencyError = new("Persistence.Concurrency", "The data changed while it was being saved.");

	/// <summary>
	/// The error of a save the database refused, for example a unique index.
	/// </summary>
	public static readonly Error DatabaseError = new("Persistence.Database", "The database refused the change.");
	#endregion

	#region Fields
	/// <summary>
	/// The server database context.
	/// </summary>
	private readonly DbContext dbContext;

	/// <summary>
	/// The log.
	/// </summary>
	private readonly ILogger<ParkingDataUnitOfWork> logger;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="ParkingDataUnitOfWork"/> class.
	/// </summary>
	/// <param name="dbContext">The server database context.</param>
	/// <param name="logger">The log.</param>
	/// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
	public ParkingDataUnitOfWork(DbContext dbContext, ILogger<ParkingDataUnitOfWork> logger)
	{
		ArgumentNullException.ThrowIfNull(dbContext);
		ArgumentNullException.ThrowIfNull(logger);
		this.dbContext = dbContext;
		this.logger = logger;
	}
	#endregion

	#region IUnitOfWork Implementation
	/// <inheritdoc />
	public async Task<Result> SaveChangesAsync(CancellationToken cancellationToken = default)
	{
		try
		{
			_ = await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
			return Result.Success();
		}
		catch (DbUpdateConcurrencyException problem)
		{
			LogRefused(logger, problem.GetType().Name);
			return Result.Failure(ConcurrencyError, ResultExceptionType.Conflict);
		}
		catch (DbUpdateException problem)
		{
			LogRefused(logger, problem.GetType().Name);
			return Result.Failure(DatabaseError, ResultExceptionType.Conflict);
		}
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Writes the log line of a refused save.
	/// </summary>
	/// <param name="logger">The log.</param>
	/// <param name="exceptionType">The type name of the exception, without its message.</param>
	[LoggerMessage(Level = LogLevel.Warning, Message = "A save was refused by the database: {ExceptionType}")]
	private static partial void LogRefused(ILogger logger, string exceptionType);
	#endregion
}
