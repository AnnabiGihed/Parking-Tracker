using ParkingTracker.Application.Features.Cards.Abstractions;
using ParkingTracker.Application.Features.Cards.Queries.ParseCardText;
using ParkingTracker.Application.Features.Cards.Services;
using ParkingTracker.Application.Features.SpotDecisions.Abstractions;
using Pivot.Framework.Application.Abstractions.Messaging.Queries;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Application.Features.Cards.Queries.ReadCard;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Handles <see cref="ReadCardQuery"/>: asks the text reader for the text of a photo and reads the card fields in it.
///              A reader failure (including the user walking away, <see cref="CardScanErrors.CancelledCode"/>) is returned as it
///              is; a photo without any field is a success with nothing set.
/// </summary>
public sealed class ReadCardQueryHandler : IQueryHandler<ReadCardQuery, ParsedCard>
{
	#region Fields
	/// <summary>
	/// Port that takes or picks the photo and recognises its text.
	/// </summary>
	private readonly ICardTextReader reader;

	/// <summary>
	/// Port to the municipalities, sectors and tolerance codes.
	/// </summary>
	private readonly IReferenceData referenceData;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="ReadCardQueryHandler"/> class.
	/// </summary>
	/// <param name="reader">Takes or picks the photo and recognises its text.</param>
	/// <param name="referenceData">Gives the municipalities, sectors and tolerance codes.</param>
	public ReadCardQueryHandler(ICardTextReader reader, IReferenceData referenceData)
	{
		this.reader = reader;
		this.referenceData = referenceData;
	}
	#endregion

	#region Public Methods
	/// <inheritdoc />
	public async Task<Result<ParsedCard>> Handle(ReadCardQuery request, CancellationToken cancellationToken)
	{
		var text = await reader.ReadTextAsync(cancellationToken).ConfigureAwait(false);
		return text.IsFailure
			? Result.Failure<ParsedCard>(text.Error)
			: ParseCardTextQueryHandler.Read(referenceData, text.Value);
	}
	#endregion
}
