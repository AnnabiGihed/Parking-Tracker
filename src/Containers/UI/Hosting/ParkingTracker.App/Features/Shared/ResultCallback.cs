using System.Text.Json;
using Android.Webkit;

namespace ParkingTracker.App.Features.Shared;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Receives the result of <c>WebView.EvaluateJavascript</c>, which Android gives as the JSON encoding of the script's
///              return value: a string result arrives as a quoted, escaped JSON string. It unwraps that string and hands it to a
///              <see cref="TaskCompletionSource{TResult}"/>; a <c>null</c> result is passed on as <see langword="null"/>, and a result longer than <see cref="MaxResultLength"/> is
///              passed on as an error.
/// </summary>
/// <remarks>
/// This class depends on Android and cannot be unit-tested on a PC; the upgrade test on an emulator covers it.
/// </remarks>
internal sealed class ResultCallback : Java.Lang.Object, IValueCallback
{
	#region Constants
	/// <summary>
	/// The longest JSON text accepted, in characters: the saved settings are a few hundred bytes, so a larger result is not the old
	/// storage and must not be parsed.
	/// </summary>
	public const int MaxResultLength = 256 * 1024;
	#endregion

	#region Fields
	/// <summary>
	/// Receives the text of the script's result, or an error.
	/// </summary>
	private readonly TaskCompletionSource<string?> answer;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="ResultCallback"/> class.
	/// </summary>
	/// <param name="answer">Receives the text of the script's result, or an error.</param>
	public ResultCallback(TaskCompletionSource<string?> answer)
	{
		this.answer = answer;
	}
	#endregion

	#region IValueCallback Implementation
	/// <inheritdoc />
	public void OnReceiveValue(Java.Lang.Object? value)
	{
		try
		{
			var encoded = value?.ToString();
			if (encoded is { Length: > MaxResultLength })
			{
				answer.TrySetException(new InvalidOperationException($"the hidden web view result is too large ({encoded.Length} characters)"));
				return;
			}

			answer.TrySetResult(string.IsNullOrEmpty(encoded) || string.Equals(encoded, "null", StringComparison.Ordinal) ? null : JsonSerializer.Deserialize<string>(encoded));
		}
		catch (JsonException exception)
		{
			answer.TrySetException(exception);
		}
	}
	#endregion
}
