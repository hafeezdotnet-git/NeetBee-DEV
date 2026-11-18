using Microsoft.Extensions.Logging;
using Microsoft.Maui.Handlers;

namespace NeetBee.Mobile;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder.UseMauiApp<App>();

		builder.Services.AddSingleton(AppSettings.Load());
		builder.Services.AddTransient<MainPage>();

		ConfigureWebView();

#if DEBUG
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}

	static void ConfigureWebView()
	{
		WebViewHandler.Mapper.AppendToMapping("NeetBeeWebView", (handler, view) =>
		{
#if ANDROID
			var settings = handler.PlatformView.Settings;
			settings.JavaScriptEnabled = true;
			settings.DomStorageEnabled = true;   // the site uses localStorage
			settings.DatabaseEnabled = true;
			settings.MediaPlaybackRequiresUserGesture = false; // audio-learning pages
			settings.SetSupportZoom(false);

			// Page-load progress for the progress bar. Subclasses MAUI's own chrome client,
			// so file pickers, permissions and the rest keep working.
			if (view is ProgressWebView progressView && handler is WebViewHandler webViewHandler)
				handler.PlatformView.SetWebChromeClient(new ProgressChromeClient(webViewHandler, progressView));
#elif IOS
			// iOS: swipe from the left/right edge to go back/forward in WebView history.
			handler.PlatformView.AllowsBackForwardNavigationGestures = true;
			handler.PlatformView.ScrollView.ContentInsetAdjustmentBehavior =
				UIKit.UIScrollViewContentInsetAdjustmentBehavior.Automatic;

			// Page-load progress for the progress bar, from WKWebView.EstimatedProgress.
			if (view is ProgressWebView progressView)
			{
				var webView = handler.PlatformView;
				progressView.ProgressObserver?.Dispose();
				progressView.ProgressObserver = webView.AddObserver(
					new Foundation.NSString("estimatedProgress"),
					Foundation.NSKeyValueObservingOptions.New,
					_ => progressView.ReportProgress(webView.EstimatedProgress));
			}
#endif
		});
	}

#if ANDROID
	sealed class ProgressChromeClient(WebViewHandler handler, ProgressWebView view)
		: Microsoft.Maui.Platform.MauiWebChromeClient(handler)
	{
		readonly WeakReference<ProgressWebView> _view = new(view);

		public override void OnProgressChanged(Android.Webkit.WebView? webView, int newProgress)
		{
			base.OnProgressChanged(webView, newProgress);
			if (_view.TryGetTarget(out var target))
				target.ReportProgress(newProgress / 100.0);
		}
	}
#endif
}
