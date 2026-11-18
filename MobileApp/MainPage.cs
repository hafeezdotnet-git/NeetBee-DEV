namespace NeetBee.Mobile;

/// <summary>
/// Full-screen WebView showing the NEET BEE website, with a simple "no internet" screen and retry.
/// </summary>
public class MainPage : ContentPage
{
	readonly Uri _startUrl;
	readonly WebView _webView;
	readonly ActivityIndicator _loading;
	readonly View _offlinePanel;
	bool _hasLoadedOnce;

	public MainPage(AppSettings settings)
	{
		_startUrl = settings.StartUrl;
		BackgroundColor = Colors.White;

		_webView = new WebView
		{
			HorizontalOptions = LayoutOptions.Fill,
			VerticalOptions = LayoutOptions.Fill,
		};
		_webView.Navigating += OnNavigating;
		_webView.Navigated += OnNavigated;

		_loading = new ActivityIndicator
		{
			Color = Color.FromArgb("#E91E63"),
			IsRunning = false,
			IsVisible = false,
			HorizontalOptions = LayoutOptions.Center,
			VerticalOptions = LayoutOptions.Center,
		};

		_offlinePanel = BuildOfflinePanel();

		Content = new Grid { Children = { _webView, _loading, _offlinePanel } };
	}

	View BuildOfflinePanel()
	{
		var retry = new Button
		{
			Text = "Retry",
			BackgroundColor = Color.FromArgb("#1B2A6B"),
			TextColor = Colors.White,
			CornerRadius = 22,
			Padding = new Thickness(32, 10),
			HorizontalOptions = LayoutOptions.Center,
		};
		retry.Clicked += (_, _) => LoadStartPage();

		return new VerticalStackLayout
		{
			IsVisible = false,
			BackgroundColor = Colors.White,
			Spacing = 16,
			Padding = new Thickness(24),
			VerticalOptions = LayoutOptions.Center,
			Children =
			{
				new Image { Source = "neetbee_logo.png", HeightRequest = 120, Aspect = Aspect.AspectFit },
				new Label
				{
					Text = "No internet connection",
					FontSize = 20,
					FontAttributes = FontAttributes.Bold,
					TextColor = Color.FromArgb("#1B2A6B"),
					HorizontalTextAlignment = TextAlignment.Center,
				},
				new Label
				{
					Text = "NEET BEE needs the internet. Check your Wi-Fi or mobile data and tap Retry.",
					FontSize = 15,
					TextColor = Color.FromArgb("#555555"),
					HorizontalTextAlignment = TextAlignment.Center,
				},
				retry,
			},
		};
	}

	protected override void OnAppearing()
	{
		base.OnAppearing();
		Connectivity.Current.ConnectivityChanged += OnConnectivityChanged;
		if (!_hasLoadedOnce)
			LoadStartPage();
	}

	protected override void OnDisappearing()
	{
		Connectivity.Current.ConnectivityChanged -= OnConnectivityChanged;
		base.OnDisappearing();
	}

	void LoadStartPage()
	{
		if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet)
		{
			ShowOffline(true);
			return;
		}

		ShowOffline(false);
		_hasLoadedOnce = true;
		_webView.Source = new UrlWebViewSource { Url = _startUrl.AbsoluteUri };
	}

	void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs e)
	{
		// Reload automatically when the connection comes back while the offline screen is showing.
		if (e.NetworkAccess == NetworkAccess.Internet && _offlinePanel.IsVisible)
			MainThread.BeginInvokeOnMainThread(LoadStartPage);
	}

	async void OnNavigating(object? sender, WebNavigatingEventArgs e)
	{
		// Links such as tel:, mailto:, whatsapp: or intent: go to the phone's own apps.
		if (Uri.TryCreate(e.Url, UriKind.Absolute, out var uri)
			&& uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps
			&& uri.Scheme != "about" && uri.Scheme != "data" && uri.Scheme != "blob")
		{
			e.Cancel = true;
			try { await Launcher.Default.TryOpenAsync(uri); }
			catch { /* no app can handle this link; ignore */ }
			return;
		}

		_loading.IsVisible = _loading.IsRunning = true;
	}

	void OnNavigated(object? sender, WebNavigatedEventArgs e)
	{
		_loading.IsVisible = _loading.IsRunning = false;

		if (e.Result is WebNavigationResult.Failure or WebNavigationResult.Timeout
			&& Connectivity.Current.NetworkAccess != NetworkAccess.Internet)
		{
			ShowOffline(true);
		}
	}

	void ShowOffline(bool offline)
	{
		_offlinePanel.IsVisible = offline;
		_webView.IsVisible = !offline;
		if (offline)
			_loading.IsVisible = _loading.IsRunning = false;
	}

	/// <summary>Android back button: go back in the website's history before leaving the app.</summary>
	protected override bool OnBackButtonPressed()
	{
		if (!_offlinePanel.IsVisible && _webView.CanGoBack)
		{
			_webView.GoBack();
			return true;
		}
		return base.OnBackButtonPressed();
	}
}
