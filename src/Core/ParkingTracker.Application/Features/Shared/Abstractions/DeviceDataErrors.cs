using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Application.Features.Shared.Abstractions;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The errors that the device repositories answer when they cannot give a value. A <see cref="Result{TValue}"/> of Pivot
///              cannot hold <see langword="null"/>, so "no card" and "no parking session" are a failure with the kind
///              <see cref="ResultExceptionType.NotFound"/>, which callers test with <see cref="IsNotStored"/> instead of treating it as a fault.
///              <see cref="KeyUnavailable"/> is the opposite case: the data may well be there, but the key that opens it cannot be read
///              right now, so callers must say "cannot verify, try later" and never read it as "nothing is stored". <see cref="KeyLost"/>
///              is the permanent counterpart: the phone holds encrypted rows but the key is gone (the protected storage dropped it), so the
///              rows can never be read again; the card reads as unreadable until the user enters it again.
/// </summary>
public static class DeviceDataErrors
{
	#region Static Instances
	/// <summary>
	/// Nothing of this kind is stored on the device (yet, or any more).
	/// </summary>
	public static readonly Error NotStored = new("DeviceData.NotStored", "Nothing of this kind is stored on the device.");

	/// <summary>
	/// The key that protects the stored fields cannot be read right now (the protected storage of the phone failed); the stored data
	/// is neither lost nor empty, so the caller tries again later and writes nothing meanwhile.
	/// </summary>
	public static readonly Error KeyUnavailable = new("DeviceData.Key.Unavailable", "The key of the device database is not available right now.");

	/// <summary>
	/// The phone holds encrypted rows, but the protected storage answered that it holds no key: it dropped the key after it failed to
	/// decrypt it. A new key is not created silently, because the rows would stay unreadable behind it; the data is replaced only when
	/// the user enters it again.
	/// </summary>
	public static readonly Error KeyLost = new("DeviceData.Key.Lost", "The key of the saved data was lost; the data cannot be read again.");
	#endregion

	#region Public Methods
	/// <summary>
	/// Tells whether a result is the plain "nothing is stored" answer and not a fault.
	/// </summary>
	/// <param name="result">The result of a repository read.</param>
	/// <returns><see langword="true"/> when the result is the failure <see cref="NotStored"/>.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="result"/> is <see langword="null"/>.</exception>
	public static bool IsNotStored(Result result)
	{
		ArgumentNullException.ThrowIfNull(result);
		return result.IsFailure && result.Error == NotStored;
	}

	/// <summary>
	/// Tells whether a result failed because the key of the stored fields cannot be read right now.
	/// </summary>
	/// <param name="result">The result of a repository call.</param>
	/// <returns><see langword="true"/> when the result is the failure <see cref="KeyUnavailable"/>.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="result"/> is <see langword="null"/>.</exception>
	public static bool IsKeyUnavailable(Result result)
	{
		ArgumentNullException.ThrowIfNull(result);
		return result.IsFailure && result.Error == KeyUnavailable;
	}

	/// <summary>
	/// Tells whether a result failed because the key of the stored fields is gone for good while encrypted rows exist.
	/// </summary>
	/// <param name="result">The result of a repository call.</param>
	/// <returns><see langword="true"/> when the result is the failure <see cref="KeyLost"/>.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="result"/> is <see langword="null"/>.</exception>
	public static bool IsKeyLost(Result result)
	{
		ArgumentNullException.ThrowIfNull(result);
		return result.IsFailure && result.Error == KeyLost;
	}
	#endregion
}
