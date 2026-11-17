using ParkingTracker.Shared.Features.Shared.Components;
using ParkingTracker.ViewModels.Features.Car;

namespace ParkingTracker.Shared.Features.Car.Components;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The line above the status block that says how much parking time is left and until when, or that the time is over.
///              It is drawn only while a parking timer runs.
/// </summary>
public sealed partial class TimerLine : ViewModelComponent<CarViewModel>
{
}
