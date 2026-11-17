using System.Text.RegularExpressions;
using ParkingTracker.Application.Features.Cards.Abstractions;
using ParkingTracker.Application.Features.SpotDecisions.Abstractions;
using ParkingTracker.Domain.Features.Sectors.ValueObjects;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Application.Features.Cards.Services;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The step shared by the handlers that read a card text: it loads the reference data, runs <see cref="CardTextParser"/>
///              on the text and turns a reference-data failure or a pattern that timed out into a <see cref="Result{TValue}"/>, so
///              that neither handler repeats the error handling.
/// </summary>
public static class CardTextReading
{
	#region Public Methods
	/// <summary>
	/// Reads the fields of a text against the reference data with <see cref="CardTextParser"/>.
	/// </summary>
	/// <param name="referenceData">Gives the municipalities, sectors and tolerance codes.</param>
	/// <param name="text">The recognised text.</param>
	/// <returns>The fields that were found, or a failure with the code of the reference data or <see cref="CardScanErrors.FailedCode"/>.</returns>
	public static Result<ParsedCard> Read(IReferenceData referenceData, string text)
	{
		return Read(referenceData, text, CardTextParser.Parse);
	}

	/// <summary>
	/// Reads the fields of a text with the given parser; the seam that lets a test make the parser time out without a pathological text.
	/// </summary>
	/// <param name="referenceData">Gives the municipalities, sectors and tolerance codes.</param>
	/// <param name="text">The recognised text.</param>
	/// <param name="parse">The parser to run on the text and the reference data.</param>
	/// <returns>The fields that were found, or a failure with the code of the reference data or <see cref="CardScanErrors.FailedCode"/>.</returns>
	public static Result<ParsedCard> Read(IReferenceData referenceData, string text, Func<string, ReferenceDataSet, ParsedCard> parse)
	{
		ArgumentNullException.ThrowIfNull(referenceData);
		ArgumentNullException.ThrowIfNull(parse);
		var data = referenceData.GetReferenceData();
		if (data.IsFailure)
		{
			return Result.Failure<ParsedCard>(data.Error);
		}

		try
		{
			return Result.Success(parse(text, data.Value));
		}
		catch (RegexMatchTimeoutException)
		{
			return Result.Failure<ParsedCard>(CardScanErrors.Failed("The text of the card could not be read."));
		}
	}
	#endregion
}
