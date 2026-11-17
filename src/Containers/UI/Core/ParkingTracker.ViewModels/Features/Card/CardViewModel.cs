using System.Globalization;
using ParkingTracker.Application.Features.Sectors.Queries.GetMailles;
using ParkingTracker.Application.Features.Sectors.Queries.GetOfficialListStatus;
using ParkingTracker.Application.Features.Shared.Abstractions;
using ParkingTracker.Application.Features.SpotDecisions.Abstractions;
using ParkingTracker.Domain.Features.Cards.ValueObjects;
using ParkingTracker.Domain.Features.Sectors.ValueObjects;
using ParkingTracker.ViewModels.Features.Shared;
using Pivot.Framework.Application.Abstractions.Messaging;

namespace ParkingTracker.ViewModels.Features.Card;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The residents' card: the line in the bottom sheet and the dialog where the user enters the card by hand. The
///              dialog adapts to the municipality (a City sector with tolerance codes, or the areas printed on the card), loads
///              the list of areas through the Application and saves the card through the settings store. The scan of a photo is
///              a later stage and is not part of this form.
/// </summary>
public sealed class CardViewModel : ViewModelBase
{
	#region Constants
	/// <summary>
	/// The municipality assumed when none is chosen: the City of Brussels.
	/// </summary>
	public const string DefaultMunicipalityId = "bruxelles";

	/// <summary>
	/// The City sector chosen first.
	/// </summary>
	public const string DefaultSectorId = "louise";

	/// <summary>
	/// The format of the value of a date input.
	/// </summary>
	private const string DateInputFormat = "yyyy-MM-dd";
	#endregion

	#region Fields
	/// <summary>
	/// Sends queries to the Application.
	/// </summary>
	private readonly ISender sender;

	/// <summary>
	/// Port to the saved card.
	/// </summary>
	private readonly ISettingsStore settings;

	/// <summary>
	/// Port to the municipalities and sectors.
	/// </summary>
	private readonly IReferenceData referenceData;

	/// <summary>
	/// Opens the renewal e-mail.
	/// </summary>
	private readonly ILinkLauncher launcher;

	/// <summary>
	/// Clock for the validity of the card.
	/// </summary>
	private readonly TimeProvider clock;

	/// <summary>
	/// The lists of areas already loaded, by municipality, for the life of the app.
	/// </summary>
	private readonly Dictionary<string, GetMaillesResponse> mailleCache = new(StringComparer.Ordinal);

	/// <summary>
	/// The state of the official City document as last read.
	/// </summary>
	private OfficialListStatus? officialList;

	/// <summary>
	/// Counts the loads of the list of areas, so that an answer for a municipality the user left is dropped.
	/// </summary>
	private int mailleSequence;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="CardViewModel"/> class.
	/// </summary>
	/// <param name="sender">Sends queries to the Application.</param>
	/// <param name="settings">Keeps the card.</param>
	/// <param name="referenceData">Gives the municipalities and sectors.</param>
	/// <param name="launcher">Opens the renewal e-mail.</param>
	/// <param name="clock">Clock for the validity of the card.</param>
	public CardViewModel(ISender sender, ISettingsStore settings, IReferenceData referenceData, ILinkLauncher launcher, TimeProvider clock)
	{
		this.sender = sender;
		this.settings = settings;
		this.referenceData = referenceData;
		this.launcher = launcher;
		this.clock = clock;
		Line = CardLinePresenter.Present(null, null, null, null, Today());
		RefreshLine();
	}
	#endregion

	#region Properties
	/// <summary>
	/// Gets what the card line shows.
	/// </summary>
	public CardLineView Line { get; private set; }

	/// <summary>
	/// Gets a value indicating whether the card dialog is open.
	/// </summary>
	public bool IsOpen { get; private set; }

	/// <summary>
	/// Gets the municipalities the card can come from.
	/// </summary>
	public IReadOnlyList<Municipality> Municipalities => Data?.Municipalities ?? [];

	/// <summary>
	/// Gets the City of Brussels sectors the card can be for.
	/// </summary>
	public IReadOnlyList<SectorDefinition> Sectors => Data?.CitySectors ?? [];

	/// <summary>
	/// Gets or sets the card number.
	/// </summary>
	public string Number { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets the number plate as typed.
	/// </summary>
	public string Plate { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets the first day of validity as the value of a date input (<c>yyyy-MM-dd</c>), or empty.
	/// </summary>
	public string ValidFrom { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets the last day of validity as the value of a date input (<c>yyyy-MM-dd</c>), or empty.
	/// </summary>
	public string ValidTo { get; set; } = string.Empty;

	/// <summary>
	/// Gets the identifier of the chosen municipality.
	/// </summary>
	public string MunicipalityId { get; private set; } = DefaultMunicipalityId;

	/// <summary>
	/// Gets the identifier of the chosen City sector, or <see langword="null"/> when the municipality has none.
	/// </summary>
	public string? SectorId { get; private set; }

	/// <summary>
	/// Gets the chosen municipality, or <see langword="null"/> when the reference data does not list it.
	/// </summary>
	public Municipality? SelectedMunicipality => Municipalities.FirstOrDefault(m => string.Equals(m.Id, MunicipalityId, StringComparison.Ordinal));

	/// <summary>
	/// Gets the tolerance agreements the user can tick for the chosen City sector; empty for other municipalities.
	/// </summary>
	public IReadOnlyList<ToleranceAgreement> Tolerances => SelectedMunicipality?.Kind == MunicipalityKind.Streets && SectorId is not null
		? Data?.ToleranceSectors.FirstOrDefault(s => string.Equals(s.Id, SectorId, StringComparison.Ordinal))?.Tolerances ?? []
		: [];

	/// <summary>
	/// Gets the codes of the tolerance agreements that are ticked.
	/// </summary>
	public ISet<string> SelectedTolerances { get; } = new HashSet<string>(StringComparer.Ordinal);

	/// <summary>
	/// Gets the zone codes of the areas that are ticked.
	/// </summary>
	public ISet<string> SelectedMailles { get; } = new HashSet<string>(StringComparer.Ordinal);

	/// <summary>
	/// Gets the zone codes of the border zones that are ticked.
	/// </summary>
	public ISet<string> SelectedLims { get; } = new HashSet<string>(StringComparer.Ordinal);

	/// <summary>
	/// Gets the state of the list of areas.
	/// </summary>
	public MailleLoadState MailleState { get; private set; }

	/// <summary>
	/// Gets the areas and border zones of the chosen municipality, when <see cref="MailleState"/> is <see cref="MailleLoadState.Loaded"/>.
	/// </summary>
	public GetMaillesResponse? Mailles { get; private set; }

	/// <summary>
	/// Gets why the list of areas could not be loaded.
	/// </summary>
	public string MailleError { get; private set; } = string.Empty;

	/// <summary>
	/// Gets what is wrong with the form after a failed save; empty when nothing is.
	/// </summary>
	public string FormError { get; private set; } = string.Empty;

	/// <summary>
	/// Gets the reference data, or <see langword="null"/> when it cannot be read.
	/// </summary>
	private ReferenceDataSet? Data
	{
		get
		{
			var data = referenceData.GetReferenceData();
			return data.IsSuccess ? data.Value : null;
		}
	}
	#endregion

	#region Events
	/// <summary>
	/// Occurs after the card was saved; the argument tells whether it is the first card of the user, so that the tracking can start.
	/// </summary>
	public event EventHandler<ValueEventArgs<bool>>? CardSaved;
	#endregion

	#region Public Methods
	/// <summary>
	/// Opens the card dialog with the saved card, or with the defaults when there is none.
	/// </summary>
	/// <param name="cancellationToken">Cancels the loading of the areas.</param>
	/// <returns>A task that completes when the areas, if the municipality needs them, were loaded.</returns>
	public Task OpenAsync(CancellationToken cancellationToken = default)
	{
		var saved = settings.GetCard();
		Number = saved?.Number ?? string.Empty;
		Plate = saved?.Plate ?? string.Empty;
		ValidFrom = saved?.Period.ValidFrom?.ToString(DateInputFormat, CultureInfo.InvariantCulture) ?? string.Empty;
		ValidTo = saved?.Period.ValidTo?.ToString(DateInputFormat, CultureInfo.InvariantCulture) ?? string.Empty;
		MunicipalityId = saved?.Municipality ?? DefaultMunicipalityId;
		Select(SelectedMunicipality?.Kind == MunicipalityKind.Streets ? saved?.Sector ?? DefaultSectorId : null, saved?.Tolerances, saved?.Mailles, saved?.Lims);
		FormError = string.Empty;
		IsOpen = true;
		NotifyChanged();
		return LoadMaillesAsync(cancellationToken);
	}

	/// <summary>
	/// Closes the dialog without saving.
	/// </summary>
	public void Close()
	{
		IsOpen = false;
		NotifyChanged();
	}

	/// <summary>
	/// Handles a change of the municipality: forgets what was ticked for the old one and loads the areas of the new one.
	/// </summary>
	/// <param name="municipalityId">The identifier of the chosen municipality.</param>
	/// <param name="cancellationToken">Cancels the loading of the areas.</param>
	/// <returns>A task that completes when the areas, if the municipality needs them, were loaded.</returns>
	public Task SelectMunicipalityAsync(string municipalityId, CancellationToken cancellationToken = default)
	{
		MunicipalityId = municipalityId;
		Select(SelectedMunicipality?.Kind == MunicipalityKind.Streets ? DefaultSectorId : null, null, null, null);
		NotifyChanged();
		return LoadMaillesAsync(cancellationToken);
	}

	/// <summary>
	/// Handles a change of the City sector: forgets the ticked tolerance codes, which belong to one sector.
	/// </summary>
	/// <param name="sectorId">The identifier of the chosen sector.</param>
	public void SelectSector(string sectorId)
	{
		SectorId = sectorId;
		SelectedTolerances.Clear();
		NotifyChanged();
	}

	/// <summary>
	/// Ticks or clears one box of the form.
	/// </summary>
	/// <param name="selection">The set that holds the ticked codes: tolerances, areas or border zones.</param>
	/// <param name="code">The code of the box.</param>
	/// <param name="isChecked">Whether the box is ticked.</param>
	public void Toggle(ISet<string> selection, string code, bool isChecked)
	{
		ArgumentNullException.ThrowIfNull(selection);
		_ = isChecked ? selection.Add(code) : selection.Remove(code);
		NotifyChanged();
	}

	/// <summary>
	/// Saves the card through the settings store and closes the dialog. The dates are required; the plate is stored in capitals
	/// without spaces or dashes.
	/// </summary>
	/// <returns><see langword="true"/> when the card was saved; <see langword="false"/> when the form is incomplete.</returns>
	public bool Save()
	{
		if (!TryReadDate(ValidFrom, out var from) || !TryReadDate(ValidTo, out var to))
		{
			FormError = "Enter the dates the card is valid from and until.";
			NotifyChanged();
			return false;
		}

		var first = settings.GetCard() is null;
		settings.SaveCard(new ParkingCard(
			MunicipalityId,
			SectorId,
			new CardPeriod(from, to),
			[.. SelectedTolerances],
			[.. SelectedMailles],
			[.. SelectedLims],
			Number.Trim(),
			NormalisePlate(Plate)));
		FormError = string.Empty;
		IsOpen = false;
		RefreshLine();
		CardSaved?.Invoke(this, new ValueEventArgs<bool>(first));
		return true;
	}

	/// <summary>
	/// Builds the card line again from the saved card and the last known state of the official list.
	/// </summary>
	public void RefreshLine()
	{
		var saved = settings.GetCard();
		var municipality = saved is null ? null : Municipalities.FirstOrDefault(m => string.Equals(m.Id, saved.Municipality, StringComparison.Ordinal))
			?? Municipalities.FirstOrDefault(m => string.Equals(m.Id, DefaultMunicipalityId, StringComparison.Ordinal));
		var sectorName = Sectors.FirstOrDefault(s => string.Equals(s.Id, saved?.Sector ?? DefaultSectorId, StringComparison.Ordinal))?.Name;
		Line = CardLinePresenter.Present(saved, municipality, sectorName, officialList, Today());
		NotifyChanged();
	}

	/// <summary>
	/// Reads the state of the official City document again when the last answer is older than its recheck interval (6 hours,
	/// or 1 minute when it was not confirmed), then rebuilds the card line.
	/// </summary>
	/// <param name="cancellationToken">Cancels the request.</param>
	/// <returns>A task that completes when the line was rebuilt.</returns>
	public async Task RefreshListIfStaleAsync(CancellationToken cancellationToken = default)
	{
		if (settings.GetCard() is null || !OfficialListStatus.IsStale(officialList, clock.GetUtcNow()))
		{
			return;
		}

		var result = await sender.Send(new GetOfficialListStatusQuery(), cancellationToken).ConfigureAwait(false);
		officialList = result.IsSuccess ? result.Value.Status : officialList;
		RefreshLine();
	}

	/// <summary>
	/// Opens the e-mail that asks the City to renew the card.
	/// </summary>
	/// <param name="cancellationToken">Cancels the request.</param>
	/// <returns>A task that completes when the link was handed to the launcher.</returns>
	public Task OpenRenewalAsync(CancellationToken cancellationToken = default)
	{
		return Line.RenewalLink is { } link ? launcher.OpenAsync(link, cancellationToken) : Task.CompletedTask;
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Sets the sector and the ticked codes of the form.
	/// </summary>
	/// <param name="sectorId">The sector, or <see langword="null"/>.</param>
	/// <param name="tolerances">The tolerance codes to tick, or <see langword="null"/> for none.</param>
	/// <param name="mailles">The areas to tick, or <see langword="null"/> for none.</param>
	/// <param name="lims">The border zones to tick, or <see langword="null"/> for none.</param>
	private void Select(string? sectorId, IEnumerable<string>? tolerances, IEnumerable<string>? mailles, IEnumerable<string>? lims)
	{
		SectorId = sectorId;
		Replace(SelectedTolerances, tolerances);
		Replace(SelectedMailles, mailles);
		Replace(SelectedLims, lims);
	}

	/// <summary>
	/// Replaces the content of a set.
	/// </summary>
	/// <param name="target">The set to fill.</param>
	/// <param name="values">The new content, or <see langword="null"/> for an empty set.</param>
	private static void Replace(ISet<string> target, IEnumerable<string>? values)
	{
		target.Clear();
		foreach (var value in values ?? [])
		{
			target.Add(value);
		}
	}

	/// <summary>
	/// Loads the areas of the chosen municipality when it issues cards per area; a municipality that was loaded before is taken from memory.
	/// </summary>
	/// <param name="cancellationToken">Cancels the request.</param>
	/// <returns>A task that completes when the state was updated.</returns>
	private async Task LoadMaillesAsync(CancellationToken cancellationToken)
	{
		var mine = ++mailleSequence;
		Mailles = null;
		if (SelectedMunicipality?.Kind != MunicipalityKind.Mailles)
		{
			MailleState = MailleLoadState.NotNeeded;
			NotifyChanged();
			return;
		}

		var id = MunicipalityId;
		if (!mailleCache.TryGetValue(id, out var response))
		{
			MailleState = MailleLoadState.Loading;
			NotifyChanged();
			var result = await sender.Send(new GetMaillesQuery(id), cancellationToken).ConfigureAwait(false);
			if (mine != mailleSequence)
			{
				return;
			}

			if (result.IsFailure)
			{
				MailleState = MailleLoadState.Failed;
				MailleError = $"Could not load the areas ({result.Error.Message}). Try again with internet.";
				NotifyChanged();
				return;
			}

			response = result.Value;
			mailleCache[id] = response;
		}

		Mailles = response;
		MailleState = MailleLoadState.Loaded;
		if (response.Areas.Count == 1)
		{
			SelectedMailles.Add(response.Areas[0].ZoneCode);
		}

		NotifyChanged();
	}

	/// <summary>
	/// Reads the value of a date input.
	/// </summary>
	/// <param name="text">The value, <c>yyyy-MM-dd</c>.</param>
	/// <param name="date">The date that was read.</param>
	/// <returns><see langword="true"/> when the text is a date.</returns>
	private static bool TryReadDate(string text, out DateOnly date)
	{
		return DateOnly.TryParseExact(text, DateInputFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
	}

	/// <summary>
	/// Writes a plate in capitals without spaces or dashes.
	/// </summary>
	/// <param name="plate">The plate as typed.</param>
	/// <returns>The plate, for example <c>1ABC234</c>.</returns>
	private static string NormalisePlate(string plate)
	{
		return new string([.. plate.Trim().ToUpperInvariant().Where(character => !char.IsWhiteSpace(character) && character != '-')]);
	}

	/// <summary>
	/// Gets today's date on the clock of the device.
	/// </summary>
	/// <returns>Today's date.</returns>
	private DateOnly Today() => DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
	#endregion
}
