using AndroidX.Credentials;

namespace ParkingTracker.App.Features.Accounts;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Receives the answer of a Credential Manager call, which Android delivers on the executor it was given, and hands the result
///              or the error to the code that started the call. It keeps no state and writes nothing to the log, because a result can hold a credential.
/// </summary>
/// <param name="onResult">Called with the result of a successful call.</param>
/// <param name="onError">Called with the exception of a failed call.</param>
internal sealed class CredentialCallback(Action<Java.Lang.Object?> onResult, Action<Java.Lang.Object> onError) : Java.Lang.Object, ICredentialManagerCallback
{
	#region ICredentialManagerCallback Implementation
	/// <inheritdoc />
	public void OnResult(Java.Lang.Object? result) => onResult(result);

	/// <inheritdoc />
	public void OnError(Java.Lang.Object e) => onError(e);
	#endregion
}
