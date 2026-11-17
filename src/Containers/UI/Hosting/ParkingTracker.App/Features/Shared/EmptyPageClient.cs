using Android.Webkit;
using AndroidWebView = Android.Webkit.WebView;

namespace ParkingTracker.App.Features.Shared;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The web view client of <see cref="WebViewLegacyStorageReader"/>: it answers every request of the hidden web view,
///              including the one for <c>https://localhost/</c>, with a tiny empty HTML page, so that no server and no network is
///              involved but the page still has the origin of the Capacitor app and with it its <c>localStorage</c>. When the page
///              has loaded it runs the read script once and hands the result to a <see cref="TaskCompletionSource{TResult}"/>.
/// </summary>
/// <remarks>
/// This class depends on Android and cannot be unit-tested on a PC; the upgrade test on an emulator covers it.
/// </remarks>
internal sealed class EmptyPageClient : WebViewClient
{
	#region Constants
	/// <summary>
	/// The page that answers every request.
	/// </summary>
	private const string EmptyPage = "<!DOCTYPE html><html><head><meta charset=\"utf-8\"></head><body></body></html>";
	#endregion

	#region Fields
	/// <summary>
	/// The script that gives the values as JSON.
	/// </summary>
	private readonly string script;

	/// <summary>
	/// Receives the JSON of the values, or an error.
	/// </summary>
	private readonly TaskCompletionSource<string?> answer;

	/// <summary>
	/// Whether the script was started already; a page can finish more than once.
	/// </summary>
	private bool started;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="EmptyPageClient"/> class.
	/// </summary>
	/// <param name="script">The script that gives the values as JSON.</param>
	/// <param name="answer">Receives the JSON of the values, or an error.</param>
	public EmptyPageClient(string script, TaskCompletionSource<string?> answer)
	{
		this.script = script;
		this.answer = answer;
	}
	#endregion

	#region WebViewClient Overrides
	/// <inheritdoc />
	public override WebResourceResponse? ShouldInterceptRequest(AndroidWebView? view, IWebResourceRequest? request)
	{
		return new WebResourceResponse("text/html", "utf-8", new MemoryStream(System.Text.Encoding.UTF8.GetBytes(EmptyPage)));
	}

	/// <inheritdoc />
	public override void OnPageFinished(AndroidWebView? view, string? url)
	{
		if (view is null || started)
		{
			return;
		}

		started = true;
		view.EvaluateJavascript(script, new ResultCallback(answer));
	}

	/// <inheritdoc />
	public override void OnReceivedError(AndroidWebView? view, IWebResourceRequest? request, WebResourceError? error)
	{
		if (request?.IsForMainFrame == true)
		{
			answer.TrySetException(new InvalidOperationException($"the hidden web view could not load its page: {error?.Description}"));
		}
	}

	/// <summary>
	/// Handles the end of the renderer process: returning <see langword="true"/> keeps the app alive (the default is to crash it), and
	/// the read fails so that the migration tries again at the next launch; the caller destroys the web view.
	/// </summary>
	/// <param name="view">The web view whose renderer is gone.</param>
	/// <param name="detail">Why the renderer went away.</param>
	/// <returns><see langword="true"/>, because the failure is handled.</returns>
	public override bool OnRenderProcessGone(AndroidWebView? view, RenderProcessGoneDetail? detail)
	{
		answer.TrySetException(new InvalidOperationException("the renderer of the hidden web view stopped"));
		return true;
	}
	#endregion
}
