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
#elif IOS
			// iOS: swipe from the left/right edge to go back/forward in WebView history.
			handler.PlatformView.AllowsBackForwardNavigationGestures = true;
			handler.PlatformView.ScrollView.ContentInsetAdjustmentBehavior =
				UIKit.UIScrollViewContentInsetAdjustmentBehavior.Automatic;
#endif
		});
	}
}
