namespace NeetBee.Mobile;

/// <summary>
/// A WebView that reports how far the current page has loaded (0.0 to 1.0).
/// The platform code in MauiProgram.cs feeds it: Android's WebChromeClient.OnProgressChanged
/// and iOS's WKWebView.EstimatedProgress.
/// </summary>
public class ProgressWebView : WebView
{
	/// <summary>Raised on the main thread with the load progress, 0.0 to 1.0. 1.0 means the page has finished loading.</summary>
	public event EventHandler<double>? LoadProgressChanged;

#if IOS
	/// <summary>iOS: keeps the estimatedProgress observer alive for as long as this view lives.</summary>
	internal IDisposable? ProgressObserver { get; set; }
#endif

	/// <summary>Called by the platform code. Safe to call from any thread.</summary>
	public void ReportProgress(double progress)
	{
		progress = Math.Clamp(progress, 0, 1);
		MainThread.BeginInvokeOnMainThread(() => LoadProgressChanged?.Invoke(this, progress));
	}
}
