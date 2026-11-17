using ParkingTracker.Application.Features.Cards.Services;
using ParkingTracker.Application.Features.SpotDecisions.Abstractions;
using Pivot.Framework.Application.Abstractions.Messaging.Queries;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Application.Features.Cards.Queries.ParseCardText;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Handles <see cref="ParseCardTextQuery"/>: reads the card fields of the text with <see cref="CardTextParser"/> against
///              the reference data. Reference data that cannot be read, or a text that makes a pattern time out, is a failure.
/// </summary>
public sealed class ParseCardTextQueryHandler : IQueryHandler<ParseCardTextQuery, ParsedCard>
{
	#region Fields
	/// <summary>
	/// Port to the municipalities, sectors and tolerance codes.
	/// </summary>
	private readonly IReferenceData referenceData;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="ParseCardTextQueryHandler"/> class.
	/// </summary>
	/// <param name="referenceData">Gives the municipalities, sectors and tolerance codes.</param>
	public ParseCardTextQueryHandler(IReferenceData referenceData)
	{
		this.referenceData = referenceData;
	}
	#endregion

	#region Public Methods
	/// <inheritdoc />
	public Task<Result<ParsedCard>> Handle(ParseCardTextQuery request, CancellationToken cancellationToken)
	{
		return Task.FromResult(CardTextReading.Read(referenceData, request.Text));
	}
	#endregion
}
