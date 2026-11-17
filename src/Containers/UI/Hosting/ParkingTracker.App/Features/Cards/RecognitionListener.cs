using System.Text;
using Android.Gms.Tasks;
using Java.Interop;
using MlText = Xamarin.Google.MLKit.Vision.Text.Text;

namespace ParkingTracker.App.Features.Cards;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Receives the outcome of one ML Kit text recognition, which Google Play services tasks report through listeners, and
///              turns it into a <see cref="System.Threading.Tasks.Task{TResult}"/> of the recognised text: the lines of every block,
///              one per line of text, in the order ML Kit returns them.
/// </summary>
/// <remarks>
/// This class depends on the ML Kit binding, so it cannot be unit-tested on a PC; a check on a phone covers it (docs/architecture.md,
/// section 6.3).
/// </remarks>
internal sealed class RecognitionListener : Java.Lang.Object, IOnSuccessListener, IOnFailureListener
{
	#region Fields
	/// <summary>
	/// Completes with the text, or with the failure.
	/// </summary>
	private readonly TaskCompletionSource<string> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
	#endregion

	#region Properties
	/// <summary>
	/// Gets the task that completes when the recognition ended.
	/// </summary>
	public Task<string> Completion => completion.Task;
	#endregion

	#region IOnSuccessListener Implementation
	/// <inheritdoc />
	public void OnSuccess(Java.Lang.Object? result)
	{
		var recognised = result?.JavaCast<MlText>();
		var text = new StringBuilder();
		if (recognised is not null)
		{
			foreach (var block in recognised.TextBlocks)
			{
				foreach (var line in block.Lines)
				{
					text.AppendLine(line.Text);
				}
			}
		}

		completion.TrySetResult(text.ToString());
	}
	#endregion

	#region IOnFailureListener Implementation
	/// <inheritdoc />
	public void OnFailure(Java.Lang.Exception e)
	{
		completion.TrySetException(new InvalidOperationException(e?.Message ?? "Text recognition failed."));
	}
	#endregion
}
