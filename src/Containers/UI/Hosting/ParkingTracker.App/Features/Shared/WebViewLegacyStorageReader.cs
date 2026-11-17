using System.Text.Json;
using Android.Webkit;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.ApplicationModel;
using ParkingTracker.Application.Features.Shared.Abstractions;
using AndroidWebView = Android.Webkit.WebView;

namespace ParkingTracker.App.Features.Shared;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The fallback <see cref="ILegacyStorageReader"/> of the app (ADR-0008): when the LevelDB files of the old web view cannot
///              be parsed, a hidden Android web view that is never attached to a window loads <c>https://localhost/</c>, the origin of
///              the Capacitor app, with <see cref="EmptyPageClient"/> answering that request with a tiny empty page (no server and no
///              network), and a script returns every <c>pt.*</c> key of its <c>localStorage</c> as JSON. Chromium itself then decodes
///              its own files, whatever their format. The script only reads, so nothing in the old storage changes. The web view is
///              created and destroyed on the main thread and is given <see cref="Timeout"/> to answer; a time-out or an error is a
///              failed reading, so that the migration tries again at the next launch.
/// </summary>
/// <remarks>
/// This class depends on Android, so it cannot be unit-tested on a PC; the composite that calls it is tested in Infrastructure, and the
/// upgrade test on an emulator covers the files (docs/architecture.md, section 6.3).
/// </remarks>
public sealed partial class WebViewLegacyStorageReader : ILegacyStorageReader
{
	#region Constants
	/// <summary>
	/// The page address of the Capacitor origin; the request for it is answered by <see cref="EmptyPageClient"/>.
	/// </summary>
	private const string PageAddress = "https://localhost/";

	/// <summary>
	/// The script that gives every <c>pt.*</c> key of the <c>localStorage</c> as one JSON text; it does not write.
	/// </summary>
	private const string ReadScript = "(function(){var o={};for(var i=0;i<localStorage.length;i++){var k=localStorage.key(i);"
		+ "if(k&&k.indexOf('pt.')===0){o[k]=localStorage.getItem(k);}}return JSON.stringify(o);})()";
	#endregion

	#region Static Instances
	/// <summary>
	/// How long the web view may take to load the page and answer.
	/// </summary>
	public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
	#endregion

	#region Fields
	/// <summary>
	/// Clock for the time limit.
	/// </summary>
	private readonly TimeProvider clock;

	/// <summary>
	/// The log.
	/// </summary>
	private readonly ILogger<WebViewLegacyStorageReader> logger;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="WebViewLegacyStorageReader"/> class.
	/// </summary>
	/// <param name="clock">Clock for the time limit.</param>
	/// <param name="logger">The log.</param>
	/// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null"/>.</exception>
	public WebViewLegacyStorageReader(TimeProvider clock, ILogger<WebViewLegacyStorageReader> logger)
	{
		ArgumentNullException.ThrowIfNull(clock);
		ArgumentNullException.ThrowIfNull(logger);
		this.clock = clock;
		this.logger = logger;
	}
	#endregion

	#region Logging
	/// <summary>
	/// Logs that the hidden web view could not give the old values.
	/// </summary>
	/// <param name="logger">The log.</param>
	/// <param name="exception">What went wrong.</param>
	[LoggerMessage(Level = LogLevel.Warning, Message = "Reading the old values through a hidden web view failed")]
	private static partial void LogReadFailed(ILogger logger, Exception exception);
	#endregion

	#region ILegacyStorageReader Implementation
	/// <inheritdoc />
	public async Task<LegacyStorageReading> ReadAsync(CancellationToken cancellationToken)
	{
		var answer = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
		AndroidWebView? view = null;
		try
		{
			await MainThread.InvokeOnMainThreadAsync(() => view = Start(answer)).ConfigureAwait(false);
			var json = await answer.Task.WaitAsync(Timeout, clock, cancellationToken).ConfigureAwait(false);
			return Parse(json);
		}
		catch (TimeoutException)
		{
			return LegacyStorageReading.Failure($"the hidden web view gave no answer within {Timeout.TotalSeconds} seconds");
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			LogReadFailed(logger, exception);
			return LegacyStorageReading.Failure($"{exception.GetType().Name}: {exception.Message}");
		}
		finally
		{
			await DestroyAsync(view).ConfigureAwait(false);
		}
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Creates the web view on the main thread and starts loading the empty page; the answer arrives through <paramref name="answer"/>.
	/// </summary>
	/// <param name="answer">Receives the JSON of the values, or an error.</param>
	/// <returns>The web view, to be destroyed by the caller.</returns>
	private static AndroidWebView Start(TaskCompletionSource<string?> answer)
	{
		var view = new AndroidWebView(Platform.AppContext);
		var settings = view.Settings;
		settings.JavaScriptEnabled = true;
		settings.DomStorageEnabled = true;
		settings.AllowFileAccess = false;
		settings.AllowContentAccess = false;
		view.SetWebViewClient(new EmptyPageClient(ReadScript, answer));
		view.LoadUrl(PageAddress);
		return view;
	}

	/// <summary>
	/// Destroys the web view on the main thread; it never fails the read.
	/// </summary>
	/// <param name="view">The web view, or <see langword="null"/> when it was never created.</param>
	/// <returns>A task that completes when the web view is gone.</returns>
	private static async Task DestroyAsync(AndroidWebView? view)
	{
		if (view is null)
		{
			return;
		}

		try
		{
			await MainThread.InvokeOnMainThreadAsync(() =>
			{
				view.StopLoading();
				view.Destroy();
			}).ConfigureAwait(false);
		}
		catch (Exception exception) when (exception is Java.Lang.RuntimeException or InvalidOperationException)
		{
			// Nothing left to do: the web view is only a temporary reader and the process is going on.
			System.Diagnostics.Debug.WriteLine(exception);
		}
	}

	/// <summary>
	/// Turns the JSON of the script into a reading.
	/// </summary>
	/// <param name="json">The JSON object of the values by key, or <see langword="null"/>.</param>
	/// <returns>The reading; a text that is not a JSON object of strings is a failure.</returns>
	private static LegacyStorageReading Parse(string? json)
	{
		if (string.IsNullOrWhiteSpace(json))
		{
			return LegacyStorageReading.Failure("the hidden web view returned no value");
		}

		var values = new Dictionary<string, string>(StringComparer.Ordinal);
		try
		{
			using var document = JsonDocument.Parse(json);
			if (document.RootElement.ValueKind != JsonValueKind.Object)
			{
				return LegacyStorageReading.Failure("the hidden web view returned something other than an object");
			}

			foreach (var property in document.RootElement.EnumerateObject())
			{
				if (property.Value.ValueKind == JsonValueKind.String)
				{
					values[property.Name] = property.Value.GetString()!;
				}
			}
		}
		catch (JsonException exception)
		{
			return LegacyStorageReading.Failure($"{exception.GetType().Name}: {exception.Message}");
		}

		return LegacyStorageReading.Success(values, LegacyStorageReading.WebViewReader);
	}
	#endregion
}
