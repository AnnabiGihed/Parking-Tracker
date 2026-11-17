using Microsoft.Extensions.Logging;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Media;
using Microsoft.Maui.Storage;
using ParkingTracker.Application.Features.Cards.Abstractions;
using Pivot.Framework.Domain.Shared;
using Xamarin.Google.MLKit.Vision.Common;
using Xamarin.Google.MLKit.Vision.Text;
using Xamarin.Google.MLKit.Vision.Text.Latin;
using AndroidUri = Android.Net.Uri;

namespace ParkingTracker.App.Features.Cards;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The <see cref="ICardTextReader"/> of the phone (ADR-0003): the user takes a photo of the card with the camera (MAUI
///              <c>MediaPicker.CapturePhotoAsync</c>), or picks one with <c>PickPhotoAsync</c> when the camera is not supported
///              or its permission is refused, and Google ML Kit recognises the text in the Latin script on the device. The photo
///              never leaves the phone: it is copied to a private cache file for the recognition, and that copy and the picker's own
///              temporary file are deleted afterwards, whatever the outcome (the copy once ML Kit has stopped using it); copies left
///              by an earlier run that was killed are swept at the start of a scan. A recognition that takes longer than
///              <see cref="RecognitionTimeout"/>, or any other failure, is <see cref="CardScanErrors.FailedCode"/>. Leaving the camera or the picker without a photo is the
///              failure <see cref="CardScanErrors.CancelledCode"/>.
/// </summary>
/// <remarks>
/// This class depends on Android and ML Kit, so it cannot be unit-tested on a PC; the parsing of the text it returns is tested in the
/// Application, and a check on a phone covers the camera and the recognition (docs/architecture.md, section 6.3).
/// </remarks>
public sealed partial class MlKitCardTextReader : ICardTextReader
{
	#region Constants
	/// <summary>
	/// The title of the camera and picker screens where the system shows one.
	/// </summary>
	private const string PickerTitle = "Parking card";

	/// <summary>
	/// The prefix of the private cache file that holds the photo while it is read.
	/// </summary>
	private const string CacheFilePrefix = "card-scan-";
	#endregion

	#region Static Instances
	/// <summary>
	/// The longest the recognition may take before the scan is given up as failed.
	/// </summary>
	private static readonly TimeSpan RecognitionTimeout = TimeSpan.FromSeconds(30);
	#endregion

	#region Fields
	/// <summary>
	/// The log, where a recognition that failed is reported.
	/// </summary>
	private readonly ILogger<MlKitCardTextReader> logger;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="MlKitCardTextReader"/> class.
	/// </summary>
	/// <param name="logger">The log, where a recognition that failed is reported.</param>
	/// <exception cref="ArgumentNullException">Thrown when <paramref name="logger"/> is <see langword="null"/>.</exception>
	public MlKitCardTextReader(ILogger<MlKitCardTextReader> logger)
	{
		ArgumentNullException.ThrowIfNull(logger);
		this.logger = logger;
	}
	#endregion

	#region ICardTextReader Implementation
	/// <inheritdoc />
	public async Task<Result<string>> ReadTextAsync(CancellationToken cancellationToken)
	{
		SweepStaleCopies();
		FileResult? photo;
		try
		{
			photo = await TakeOrPickPhotoAsync().ConfigureAwait(false);
		}
		catch (Exception exception) when (exception is PermissionException or FeatureNotSupportedException)
		{
			LogNoPhotoSource(logger, exception);
			return Result.Failure<string>(CardScanErrors.Unavailable("The camera and the photo picker cannot be used. Check the permissions of the app, or type the fields."));
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			LogRecognitionFailed(logger, exception);
			return Result.Failure<string>(CardScanErrors.Failed(exception.Message));
		}

		if (photo is null)
		{
			return Result.Failure<string>(CardScanErrors.Cancelled());
		}

		var copy = Path.Combine(FileSystem.CacheDirectory, CacheFilePrefix + Guid.NewGuid().ToString("N") + ".jpg");
		try
		{
			await CopyAsync(photo, copy, cancellationToken).ConfigureAwait(false);
			return Result.Success(await RecognizeAsync(copy, cancellationToken).ConfigureAwait(false));
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			LogRecognitionFailed(logger, exception);
			return Result.Failure<string>(CardScanErrors.Failed(exception is TimeoutException ? "Reading the photo took too long." : "The photo could not be read."));
		}
		finally
		{
			DeleteQuietly(photo.FullPath);
		}
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Lets the user take a photo, or pick one when the camera cannot be used. The camera permission is asked here, at use; a refusal
	/// sends the user to the picker, which needs no permission.
	/// </summary>
	/// <returns>A task with the photo, or <see langword="null"/> when the user left without one.</returns>
	/// <exception cref="PermissionException">Thrown when the picker is refused.</exception>
	/// <exception cref="FeatureNotSupportedException">Thrown when the device has no picker.</exception>
	private async Task<FileResult?> TakeOrPickPhotoAsync()
	{
		var options = new MediaPickerOptions { Title = PickerTitle };
		if (MediaPicker.Default.IsCaptureSupported)
		{
			try
			{
				var camera = await MainThread.InvokeOnMainThreadAsync(() => Permissions.RequestAsync<Permissions.Camera>()).ConfigureAwait(false);
				if (camera == PermissionStatus.Granted)
				{
					return await MainThread.InvokeOnMainThreadAsync(() => MediaPicker.Default.CapturePhotoAsync(options)).ConfigureAwait(false);
				}
			}
			catch (Exception exception) when (exception is PermissionException or FeatureNotSupportedException)
			{
				LogCameraUnavailable(logger, exception);
			}
		}

		var picked = await MainThread.InvokeOnMainThreadAsync(() => MediaPicker.Default.PickPhotosAsync(options)).ConfigureAwait(false);
		return picked?.FirstOrDefault();
	}

	/// <summary>
	/// Copies the photo to a private cache file, which ML Kit reads by path and which this class deletes.
	/// </summary>
	/// <param name="photo">The photo from the camera or the picker.</param>
	/// <param name="path">The cache file to write.</param>
	/// <param name="cancellationToken">Cancels the copy.</param>
	/// <returns>A task that completes when the file is written; a partly written file is deleted when the copy fails.</returns>
	private static async Task CopyAsync(FileResult photo, string path, CancellationToken cancellationToken)
	{
		try
		{
			var source = await photo.OpenReadAsync().ConfigureAwait(false);
			await using (source.ConfigureAwait(false))
			{
				var target = File.Create(path);
				await using (target.ConfigureAwait(false))
				{
					await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
				}
			}
		}
		catch
		{
			DeleteQuietly(path);
			throw;
		}
	}

	/// <summary>
	/// Recognises the Latin text of a photo with the bundled ML Kit model, on the device. The recognizer, the image and the listener
	/// stay alive until the ML Kit task has completed, even when the wait is cancelled or times out, and the photo file is deleted
	/// only after that.
	/// </summary>
	/// <param name="path">The photo file, which this method deletes once ML Kit is done with it.</param>
	/// <param name="cancellationToken">Stops waiting for the answer; ML Kit itself cannot be interrupted.</param>
	/// <returns>A task with the text, one line per line on the card; empty when the photo holds no text.</returns>
	/// <exception cref="TimeoutException">Thrown when the recognition took longer than <see cref="RecognitionTimeout"/>.</exception>
	private static async Task<string> RecognizeAsync(string path, CancellationToken cancellationToken)
	{
		TextRecognizer? recognizer = null;
		InputImage? image = null;
		RecognitionListener? listener = null;
		try
		{
			recognizer = TextRecognition.GetClient(TextRecognizerOptions.DefaultOptions);
			var uri = AndroidUri.FromFile(new Java.IO.File(path)) ?? throw new InvalidOperationException("The photo file has no address.");
			image = InputImage.FromFilePath(Platform.AppContext, uri);
			listener = new RecognitionListener();
			recognizer.Process(image).AddOnSuccessListener(listener).AddOnFailureListener(listener);
		}
		catch
		{
			Release(recognizer, image, listener, path);
			throw;
		}

		var completion = listener.Completion;
		_ = completion.ContinueWith(
			finished =>
			{
				_ = finished.Exception;
				Release(recognizer, image, listener, path);
			},
			CancellationToken.None,
			TaskContinuationOptions.ExecuteSynchronously,
			TaskScheduler.Default);
		using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeout.CancelAfter(RecognitionTimeout);
		try
		{
			return await completion.WaitAsync(timeout.Token).ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			throw new TimeoutException("The text recognition did not answer in time.");
		}
	}

	/// <summary>
	/// Disposes what a recognition used and deletes its photo file.
	/// </summary>
	/// <param name="recognizer">The ML Kit client, or <see langword="null"/> when it was not created.</param>
	/// <param name="image">The input image, or <see langword="null"/> when it was not created.</param>
	/// <param name="listener">The listener, or <see langword="null"/> when it was not created.</param>
	/// <param name="path">The photo file.</param>
	private static void Release(TextRecognizer? recognizer, InputImage? image, RecognitionListener? listener, string path)
	{
		listener?.Dispose();
		image?.Dispose();
		recognizer?.Dispose();
		DeleteQuietly(path);
	}

	/// <summary>
	/// Deletes the photo copies that an earlier scan left behind, for example because the app was killed during it.
	/// </summary>
	private static void SweepStaleCopies()
	{
		try
		{
			foreach (var stale in Directory.EnumerateFiles(FileSystem.CacheDirectory, CacheFilePrefix + "*"))
			{
				DeleteQuietly(stale);
			}
		}
		catch (IOException)
		{
			// The system clears the cache later.
		}
		catch (UnauthorizedAccessException)
		{
			// The system clears the cache later.
		}
	}

	/// <summary>
	/// Deletes a temporary photo file this class created or was given; a file that is not there or cannot be deleted is left, because
	/// there is nothing more to do.
	/// </summary>
	/// <param name="path">The file, which may be <see langword="null"/>.</param>
	private static void DeleteQuietly(string? path)
	{
		if (string.IsNullOrEmpty(path))
		{
			return;
		}

		try
		{
			File.Delete(path);
		}
		catch (IOException)
		{
			// The system clears the cache later.
		}
		catch (UnauthorizedAccessException)
		{
			// The system clears the cache later.
		}
	}
	#endregion

	#region Logging
	/// <summary>
	/// Logs that neither the camera nor the picker could be used.
	/// </summary>
	/// <param name="logger">The log.</param>
	/// <param name="exception">What went wrong.</param>
	[LoggerMessage(Level = LogLevel.Warning, Message = "Neither the camera nor the photo picker can be used")]
	private static partial void LogNoPhotoSource(ILogger logger, Exception exception);

	/// <summary>
	/// Logs that the camera could not be used and the picker is offered instead.
	/// </summary>
	/// <param name="logger">The log.</param>
	/// <param name="exception">What went wrong.</param>
	[LoggerMessage(Level = LogLevel.Information, Message = "The camera cannot be used; the photo picker is offered instead")]
	private static partial void LogCameraUnavailable(ILogger logger, Exception exception);

	/// <summary>
	/// Logs that reading the text of a card photo failed.
	/// </summary>
	/// <param name="logger">The log.</param>
	/// <param name="exception">What went wrong.</param>
	[LoggerMessage(Level = LogLevel.Error, Message = "Reading the text of a card photo failed")]
	private static partial void LogRecognitionFailed(ILogger logger, Exception exception);
	#endregion
}
