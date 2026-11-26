using AndroidX.Core.Content;
using AndroidX.Credentials;
using AndroidX.Credentials.Exceptions;
using Google.Android.Libraries.Identity.GoogleId;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.ApplicationModel;
using ParkingTracker.Application.Features.Accounts.Abstractions;
using Pivot.Framework.Domain.Shared;
using AndroidActivity = Android.App.Activity;
using AndroidCancellationSignal = Android.OS.CancellationSignal;

namespace ParkingTracker.App.Features.Accounts;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The <see cref="IGoogleSignIn"/> of the phone (ADR-0014): the Android Credential Manager asks for a Google ID token for the Web
///              client ID of the build, bound to the nonce of the server, and the callback API is wrapped in a task. The chooser is
///              shown on the current activity and lists every Google account (it is not limited to accounts that signed in before, and does
///              not choose one by itself). A cancel, a phone without a Google account and every other error are three distinct failures. The ID
///              token is passed on and never logged. Without a Web client ID (a build that overrides it empty), sign-in is unavailable.
/// </summary>
/// <remarks>This class depends on Android and Google Play services, so it cannot be unit-tested on a PC; the session client that uses it is tested over a substitute, and an emulator check covers the chooser.</remarks>
public sealed partial class AndroidGoogleSignIn : IGoogleSignIn
{
	#region Fields
	/// <summary>
	/// The Web client ID of the Google project of this build, or empty when the build has none.
	/// </summary>
	private readonly string webClientId;

	/// <summary>
	/// The log, where the kind of a failure is reported (never a token).
	/// </summary>
	private readonly ILogger<AndroidGoogleSignIn> logger;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="AndroidGoogleSignIn"/> class.
	/// </summary>
	/// <param name="webClientId">The Web client ID of the Google project of this build, or empty when the build has none.</param>
	/// <param name="logger">The log, where the kind of a failure is reported.</param>
	/// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null"/>.</exception>
	public AndroidGoogleSignIn(string webClientId, ILogger<AndroidGoogleSignIn> logger)
	{
		ArgumentNullException.ThrowIfNull(webClientId);
		ArgumentNullException.ThrowIfNull(logger);
		this.webClientId = webClientId;
		this.logger = logger;
	}
	#endregion

	#region IGoogleSignIn Implementation
	/// <inheritdoc />
	public bool IsAvailable => webClientId.Length > 0;

	/// <inheritdoc />
	public async Task<Result<string>> SignInAsync(string nonce, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(nonce);
		var activity = Platform.CurrentActivity;
		if (!IsAvailable || activity is null)
		{
			return Result.Failure<string>(AccountErrors.Unavailable());
		}

		var completion = new TaskCompletionSource<Result<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
		using var cancellation = new AndroidCancellationSignal();
		using var registration = cancellationToken.Register(cancellation.Cancel);
		var callback = new CredentialCallback(
			result => completion.TrySetResult(ReadIdToken(result)),
			error => completion.TrySetResult(Result.Failure<string>(FailureOf(error))));
		await MainThread.InvokeOnMainThreadAsync(() => StartChooser(activity, nonce, cancellation, callback)).ConfigureAwait(false);
		return await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
	}

	/// <inheritdoc />
	public async Task ClearStateAsync(CancellationToken cancellationToken = default)
	{
		var activity = Platform.CurrentActivity;
		if (!IsAvailable || activity is null)
		{
			return;
		}

		var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		using var cancellation = new AndroidCancellationSignal();
		using var registration = cancellationToken.Register(cancellation.Cancel);
		var callback = new CredentialCallback(_ => completion.TrySetResult(), error =>
		{
			LogClearFailed(logger, error.Class.Name);
			completion.TrySetResult();
		});
		var executor = ContextCompat.GetMainExecutor(activity) ?? throw new InvalidOperationException("The Android main executor is unavailable.");
		CredentialManager.Create(activity).ClearCredentialStateAsync(new ClearCredentialStateRequest(), cancellation, executor, callback);
		await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Asks Credential Manager for a Google ID token. The chooser lists every Google account and never selects one by itself.
	/// </summary>
	/// <param name="activity">The activity that hosts the chooser.</param>
	/// <param name="nonce">The nonce of the server.</param>
	/// <param name="cancellation">Closes the chooser.</param>
	/// <param name="callback">Receives the answer.</param>
	private void StartChooser(AndroidActivity activity, string nonce, AndroidCancellationSignal cancellation, CredentialCallback callback)
	{
		var option = new GetGoogleIdOption.Builder()
			.SetServerClientId(webClientId)
			.SetNonce(nonce)
			.SetFilterByAuthorizedAccounts(false)
			.SetAutoSelectEnabled(false)
			.Build();
		var request = new GetCredentialRequest.Builder().AddCredentialOption(option).Build();
		var executor = ContextCompat.GetMainExecutor(activity) ?? throw new InvalidOperationException("The Android main executor is unavailable.");
		CredentialManager.Create(activity).GetCredentialAsync(activity, request, cancellation, executor, callback);
	}

	/// <summary>
	/// Reads the Google ID token out of a Credential Manager result.
	/// </summary>
	/// <param name="result">The result.</param>
	/// <returns>The ID token, or the unavailable failure when the result holds no Google credential.</returns>
	private Result<string> ReadIdToken(Java.Lang.Object? result)
	{
		if (result is not GetCredentialResponse response
			|| response.Credential is not CustomCredential custom
			|| !string.Equals(custom.Type, GoogleIdTokenCredential.TypeGoogleIdTokenCredential, StringComparison.Ordinal))
		{
			LogUnexpectedResult(logger);
			return Result.Failure<string>(AccountErrors.Unavailable());
		}

		try
		{
			return Result.Success(GoogleIdTokenCredential.CreateFrom(custom.Data).IdToken);
		}
		catch (Java.Lang.Exception exception)
		{
			LogUnreadableCredential(logger, exception.GetType().Name);
			return Result.Failure<string>(AccountErrors.Unavailable());
		}
	}

	/// <summary>
	/// Maps a Credential Manager error to a failure.
	/// </summary>
	/// <param name="error">The error.</param>
	/// <returns>Cancelled when the user left, no account when the phone has none, unavailable otherwise.</returns>
	private Error FailureOf(Java.Lang.Object error)
	{
		if (IsInstance<GetCredentialCancellationException>(error))
		{
			return AccountErrors.Cancelled();
		}

		if (IsInstance<NoCredentialException>(error))
		{
			return AccountErrors.NoAccount();
		}

		LogSignInFailed(logger, error.Class.Name);
		return AccountErrors.Unavailable();
	}

	/// <summary>
	/// Tells whether a Java object is an instance of a bound Java class. A C# type test does not work here, because the error arrives as a plain Java object.
	/// </summary>
	/// <typeparam name="T">The bound Java class.</typeparam>
	/// <param name="value">The Java object.</param>
	/// <returns><see langword="true"/> when the object is an instance of <typeparamref name="T"/>.</returns>
	private static bool IsInstance<T>(Java.Lang.Object value)
	{
		return Java.Lang.Class.FromType(typeof(T)).IsInstance(value);
	}
	#endregion

	#region Logging
	/// <summary>
	/// Logs the kind of an error that is neither a cancel nor a missing account; only the class name, never a message that could hold a credential.
	/// </summary>
	/// <param name="logger">The log.</param>
	/// <param name="errorType">The class name of the error.</param>
	[LoggerMessage(Level = LogLevel.Warning, Message = "Google sign-in failed with {ErrorType}")]
	private static partial void LogSignInFailed(ILogger logger, string errorType);

	/// <summary>
	/// Logs that Credential Manager answered with something that is not a Google credential.
	/// </summary>
	/// <param name="logger">The log.</param>
	[LoggerMessage(Level = LogLevel.Warning, Message = "Credential Manager answered with a credential that is not a Google ID token")]
	private static partial void LogUnexpectedResult(ILogger logger);

	/// <summary>
	/// Logs that the Google credential could not be read.
	/// </summary>
	/// <param name="logger">The log.</param>
	/// <param name="errorType">The class name of the error.</param>
	[LoggerMessage(Level = LogLevel.Warning, Message = "The Google credential could not be read: {ErrorType}")]
	private static partial void LogUnreadableCredential(ILogger logger, string errorType);

	/// <summary>
	/// Logs that the credential state could not be cleared at sign-out.
	/// </summary>
	/// <param name="logger">The log.</param>
	/// <param name="errorType">The class name of the error.</param>
	[LoggerMessage(Level = LogLevel.Information, Message = "Clearing the Google credential state failed with {ErrorType}")]
	private static partial void LogClearFailed(ILogger logger, string errorType);
	#endregion
}
