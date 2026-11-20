using ParkingTracker.Application.Features.Shared.Abstractions;
using ParkingTracker.Application.Features.Vehicles.Abstractions;
using ParkingTracker.Domain.Features.Vehicles.Aggregates;
using ParkingTracker.Domain.Features.Vehicles.ValueObjects;
using ParkingTracker.ViewModels.Features.Card;
using ParkingTracker.ViewModels.Features.Shared;

namespace ParkingTracker.ViewModels.Features.Vehicles;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The My vehicle dialog (ADR-0031): the make, model, year, fuel, emission standard and plate of the one vehicle profile and,
///              only for an electric vehicle or a plug-in hybrid, its plugs, battery and range. Switching to another fuel clears the plug
///              fields, the plate is shown in capitals while it is typed, and a save goes through the domain: the first rule the values break is
///              shown next to the form and nothing is written. A save or a removal shows a toast and closes the dialog. The text for the side
///              menu comes from the saved profile. The dialog is drawn by a <c>DialogBridge</c> through <see cref="IDialogViewModel"/>, so
///              this class never references Radzen.
/// </summary>
public sealed class VehicleViewModel : ViewModelBase, IDialogViewModel
{
	#region Constants
	/// <summary>
	/// The earliest model year, from the domain.
	/// </summary>
	public const int MinimumYear = Vehicle.EarliestYear;

	/// <summary>
	/// What the side menu shows while no vehicle is saved.
	/// </summary>
	public const string NotSetText = "Not set";

	/// <summary>
	/// The toast after a vehicle was saved.
	/// </summary>
	public const string SavedText = "Vehicle saved.";

	/// <summary>
	/// The toast after the vehicle was removed.
	/// </summary>
	public const string RemovedText = "Vehicle removed.";

	/// <summary>
	/// What the user is told when the saved vehicle could not be read for a reason other than unreadable data.
	/// </summary>
	public const string ReadFailedText = "Your vehicle could not be read. Please try again.";

	/// <summary>
	/// What the form shows when the model year was left empty; a year outside the range gets the message of the domain instead.
	/// </summary>
	public const string YearMissingText = "Enter the model year.";

	/// <summary>
	/// What the form shows when the database refused the save.
	/// </summary>
	public const string SaveFailedText = "Your vehicle could not be saved. Please try again.";

	/// <summary>
	/// What the form shows when the database refused the removal.
	/// </summary>
	public const string RemoveFailedText = "Your vehicle could not be removed. Please try again.";
	#endregion

	#region Fields
	/// <summary>
	/// Port to the saved vehicle.
	/// </summary>
	private readonly IVehicleStore store;

	/// <summary>
	/// Shows the messages of the dialog.
	/// </summary>
	private readonly ToastViewModel toast;

	/// <summary>
	/// The clock that tells the current year, which bounds the model year.
	/// </summary>
	private readonly TimeProvider clock;

	/// <summary>
	/// The plugs ticked in the form.
	/// </summary>
	private readonly HashSet<ChargingConnector> connectors = [];

	/// <summary>
	/// The saved values, or <see langword="null"/> when no vehicle is saved.
	/// </summary>
	private VehicleDetails? saved;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="VehicleViewModel"/> class; the saved vehicle is read by <see cref="LoadAsync"/>.
	/// </summary>
	/// <param name="store">Port to the saved vehicle.</param>
	/// <param name="toast">Shows the messages of the dialog.</param>
	/// <param name="clock">The clock that tells the current year.</param>
	/// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null"/>.</exception>
	public VehicleViewModel(IVehicleStore store, ToastViewModel toast, TimeProvider clock)
	{
		ArgumentNullException.ThrowIfNull(store);
		ArgumentNullException.ThrowIfNull(toast);
		ArgumentNullException.ThrowIfNull(clock);
		this.store = store;
		this.toast = toast;
		this.clock = clock;
		ClearForm();
	}
	#endregion

	#region Properties
	/// <inheritdoc />
	public bool IsOpen { get; private set; }

	/// <summary>
	/// Gets a value indicating whether a save or a removal is running, so that a second tap does nothing.
	/// </summary>
	public bool IsBusy { get; private set; }

	/// <summary>
	/// Gets a value indicating whether a vehicle is saved, so that the dialog offers to remove it.
	/// </summary>
	public bool HasVehicle => saved is not null;

	/// <summary>
	/// Gets or sets the code of the make, one of <see cref="VehicleMakes.All"/>; empty until the user picks one.
	/// </summary>
	public string Make { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets the model as typed.
	/// </summary>
	public string Model { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets the model year, or <see langword="null"/> while it is empty.
	/// </summary>
	public int? Year { get; set; }

	/// <summary>
	/// Gets what the vehicle runs on; change it with <see cref="SelectFuel"/>.
	/// </summary>
	public FuelType Fuel { get; private set; }

	/// <summary>
	/// Gets or sets the emission standard.
	/// </summary>
	public EuroNorm EuroNorm { get; set; }

	/// <summary>
	/// Gets the plugs ticked in the form, in ascending order; change them with <see cref="SetConnectors"/>.
	/// </summary>
	public IReadOnlyList<ChargingConnector> Connectors => [.. connectors.Order()];

	/// <summary>
	/// Gets or sets the usable battery in kWh, or <see langword="null"/> while it is empty.
	/// </summary>
	public double? BatteryKwh { get; set; }

	/// <summary>
	/// Gets or sets the range on a full battery in kilometres, or <see langword="null"/> while it is empty.
	/// </summary>
	public int? RangeKm { get; set; }

	/// <summary>
	/// Gets the plate as typed, in capitals; change it with <see cref="SetPlate"/>.
	/// </summary>
	public string Plate { get; private set; } = string.Empty;

	/// <summary>
	/// Gets the first rule the values broke on the last save, in words for the user; empty when there is none.
	/// </summary>
	public string FormError { get; private set; } = string.Empty;

	/// <summary>
	/// Gets a value indicating whether the plugs, the battery and the range are shown: only for an electric vehicle or a plug-in hybrid.
	/// </summary>
	public bool ShowsChargingFields => Fuel is FuelType.Electric or FuelType.PlugInHybrid;

	/// <summary>
	/// Gets a value indicating whether the emission standard is asked: an electric vehicle has none.
	/// </summary>
	public bool ShowsEuroNorm => Fuel != FuelType.Electric;

	/// <summary>
	/// Gets the latest model year: the year after the current one, as the domain allows.
	/// </summary>
	public int MaximumYear => clock.GetLocalNow().Year + 1;

	/// <summary>
	/// Gets the text under "My vehicle" in the side menu: make, model and year, or <see cref="NotSetText"/>.
	/// </summary>
	public string SummaryText => saved is null ? NotSetText : SummaryOf(saved);
	#endregion

	#region Public Methods
	/// <summary>
	/// Reads the saved vehicle. Call it when the screen starts.
	/// </summary>
	/// <param name="cancellationToken">Cancels the read.</param>
	/// <returns>A task that completes when the vehicle was read or the user was told that it cannot be.</returns>
	public async Task LoadAsync(CancellationToken cancellationToken = default)
	{
		try
		{
			saved = await store.GetAsync(cancellationToken).ConfigureAwait(false);
		}
		catch (InvalidOperationException failure)
		{
			// Covers SavedDataUnreadableException, whose message tells the user what to do, and a refused read of the database.
			saved = null;
			toast.Show(failure is SavedDataUnreadableException ? failure.Message : ReadFailedText);
		}

		NotifyChanged();
	}

	/// <summary>
	/// Opens the dialog with the saved values, or with an empty form when no vehicle is saved.
	/// </summary>
	public void Open()
	{
		if (IsOpen)
		{
			return;
		}

		FormError = string.Empty;
		if (saved is null)
		{
			ClearForm();
		}
		else
		{
			FillForm(saved);
		}

		IsOpen = true;
		NotifyChanged();
	}

	/// <inheritdoc />
	public void Close()
	{
		if (!IsOpen)
		{
			return;
		}

		IsOpen = false;
		NotifyChanged();
	}

	/// <summary>
	/// Sets what the vehicle runs on; a fuel that does not plug in clears the plugs, the battery and the range, and an electric
	/// vehicle goes back to an unknown emission standard because it has none.
	/// </summary>
	/// <param name="fuel">The fuel the user picked.</param>
	public void SelectFuel(FuelType fuel)
	{
		Fuel = fuel;
		if (!ShowsChargingFields)
		{
			ClearCharging();
		}

		if (!ShowsEuroNorm)
		{
			EuroNorm = EuroNorm.Unknown;
		}

		NotifyChanged();
	}

	/// <summary>
	/// Sets the ticked plugs.
	/// </summary>
	/// <param name="chosen">The plugs that are ticked now; <see langword="null"/> when none is.</param>
	public void SetConnectors(IEnumerable<ChargingConnector>? chosen)
	{
		connectors.Clear();
		connectors.UnionWith(chosen ?? []);
	}

	/// <summary>
	/// Sets the plate as typed, written in capitals.
	/// </summary>
	/// <param name="typed">The text in the field, or <see langword="null"/>.</param>
	public void SetPlate(string? typed)
	{
		Plate = PlateInput.Normalise(typed);
	}

	/// <summary>
	/// Saves the vehicle through the domain. A broken rule is shown next to the form and nothing is written; a saved vehicle shows a
	/// toast and closes the dialog.
	/// </summary>
	/// <param name="cancellationToken">Cancels the save.</param>
	/// <returns>A task that completes when the vehicle was saved or the reason it was not was shown.</returns>
	public async Task SaveAsync(CancellationToken cancellationToken = default)
	{
		if (IsBusy)
		{
			return;
		}

		if (Year is null)
		{
			FormError = YearMissingText;
			NotifyChanged();
			return;
		}

		BeginWork();
		try
		{
			var details = ToDetails();
			var result = await store.SaveAsync(details, cancellationToken).ConfigureAwait(false);
			if (result.IsFailure)
			{
				FormError = result.Error.Message;
				return;
			}

			saved = details;
			Finish(SavedText);
		}
		catch (InvalidOperationException)
		{
			FormError = SaveFailedText;
		}
		finally
		{
			EndWork();
		}
	}

	/// <summary>
	/// Removes the saved vehicle, shows a toast and closes the dialog; nothing happens while no vehicle is saved.
	/// </summary>
	/// <param name="cancellationToken">Cancels the removal.</param>
	/// <returns>A task that completes when the vehicle was removed or the reason it was not was shown.</returns>
	public async Task RemoveAsync(CancellationToken cancellationToken = default)
	{
		if (IsBusy || !HasVehicle)
		{
			return;
		}

		BeginWork();
		try
		{
			await store.RemoveAsync(cancellationToken).ConfigureAwait(false);
			saved = null;
			Finish(RemovedText);
		}
		catch (InvalidOperationException)
		{
			FormError = RemoveFailedText;
		}
		finally
		{
			EndWork();
		}
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Starts a save or a removal: clears the last error and blocks a second one.
	/// </summary>
	private void BeginWork()
	{
		IsBusy = true;
		FormError = string.Empty;
		NotifyChanged();
	}

	/// <summary>
	/// Ends a save or a removal and draws the result.
	/// </summary>
	private void EndWork()
	{
		IsBusy = false;
		NotifyChanged();
	}

	/// <summary>
	/// Tells the user that the work succeeded and closes the dialog.
	/// </summary>
	/// <param name="message">The toast.</param>
	private void Finish(string message)
	{
		toast.Show(message);
		Close();
	}

	/// <summary>
	/// Builds the values of the form for the domain; only a vehicle that plugs in has charging details.
	/// </summary>
	/// <returns>The values as entered; the year is checked to be filled in before this runs.</returns>
	private VehicleDetails ToDetails()
	{
		var charging = ShowsChargingFields && (connectors.Count > 0 || BatteryKwh is not null || RangeKm is not null)
			? new ChargingDetails(Connectors, BatteryKwh, RangeKm)
			: null;
		return new VehicleDetails(Make, Model.Trim(), Year ?? 0, Fuel, ShowsEuroNorm ? EuroNorm : EuroNorm.Unknown, charging, Plate);
	}

	/// <summary>
	/// Puts the saved values in the form.
	/// </summary>
	/// <param name="details">The saved values.</param>
	private void FillForm(VehicleDetails details)
	{
		Make = details.Make;
		Model = details.Model ?? string.Empty;
		Year = details.Year;
		Fuel = details.Fuel;
		EuroNorm = details.EuroNorm;
		SetConnectors(details.Charging?.Connectors);
		BatteryKwh = details.Charging?.BatteryKwh;
		RangeKm = details.Charging?.RangeKm;
		SetPlate(details.Plate);
	}

	/// <summary>
	/// Empties the form for a new vehicle.
	/// </summary>
	private void ClearForm()
	{
		Make = string.Empty;
		Model = string.Empty;
		Year = null;
		Fuel = FuelType.Petrol;
		EuroNorm = EuroNorm.Unknown;
		Plate = string.Empty;
		ClearCharging();
	}

	/// <summary>
	/// Empties the plugs, the battery and the range.
	/// </summary>
	private void ClearCharging()
	{
		connectors.Clear();
		BatteryKwh = null;
		RangeKm = null;
	}

	/// <summary>
	/// Builds the menu text of a saved vehicle.
	/// </summary>
	/// <param name="details">The saved values.</param>
	/// <returns>"Make Model · Year"; the make is its listed name.</returns>
	private static string SummaryOf(VehicleDetails details)
	{
		var make = VehicleMakes.All.FirstOrDefault(listed => string.Equals(listed.Code, details.Make, StringComparison.Ordinal))?.Name ?? details.Make;
		return $"{make} {details.Model}".Trim() + " · " + details.Year;
	}
	#endregion
}
