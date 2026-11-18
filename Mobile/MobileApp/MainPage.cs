namespace NeetBee.Mobile;

/// <summary>
/// Full-screen WebView showing the NEET BEE website, with:
/// - a progress bar while pages load,
/// - a "No Internet Connection" screen (tap to retry; also reloads when the connection returns),
/// - a "System Under Maintenance" screen when the site is down (HTTP 5xx, or Azure's
///   "Error 403 - This web app is stopped" page), which reloads by itself once the site is back.
/// </summary>
public class MainPage : ContentPage
{
	enum Screen { Web, Offline, SiteDown }

	static readonly Color Pink = Color.FromArgb("#E91E63");
	static readonly Color Navy = Color.FromArgb("#1B2A6B");

	/// <summary>Status codes that mean the site itself is down, not that the user did something wrong.</summary>
	static readonly HashSet<int> SiteDownStatusCodes = [403, 500, 502, 503, 504];

	/// <summary>
	/// Text of the error pages Azure App Service / IIS show when the app isn't running. Used when the
	/// WebView can't tell us the status code (iOS Safari doesn't report it to the page).
	/// </summary>
	static readonly string[] SiteDownPageText =
	[
		"This web app is stopped",
		"Error 403 - This web app",
		"Service Unavailable",
		"502 Bad Gateway",
		"503 Service",
		"HTTP Error 500",
		"HTTP Error 502",
		"HTTP Error 503",
		"500 - Internal server error",
		"Application Error",
		"An error occurred while starting the application",
	];

	/// <summary>How often to check, while the maintenance screen shows, whether the site is back.</summary>
	static readonly TimeSpan SiteDownRecheckInterval = TimeSpan.FromSeconds(60);

	static readonly HttpClient Probe = new() { Timeout = TimeSpan.FromSeconds(15) };

	readonly Uri _startUrl;
	readonly ProgressWebView _webView;
	readonly ProgressBar _progressBar;
	readonly ActivityIndicator _firstLoadSpinner;
	readonly View _offlinePanel;
	readonly View _siteDownPanel;
	readonly IDispatcherTimer _siteDownTimer;
	Screen _screen = Screen.Web;
	bool _hasLoadedOnce;
	bool _firstPageShown;

	// Bumped whenever a new load starts, so a finishing animation from an older load doesn't hide the bar.
	int _loadGeneration;

	public MainPage(AppSettings settings)
	{
		_startUrl = settings.StartUrl;
		BackgroundColor = Colors.White;

		_webView = new ProgressWebView
		{
			HorizontalOptions = LayoutOptions.Fill,
			VerticalOptions = LayoutOptions.Fill,
		};
		_webView.Navigating += OnNavigating;
		_webView.Navigated += OnNavigated;
		_webView.LoadProgressChanged += OnLoadProgressChanged;

		// Thin bar across the top of the screen, shown while a page (or anything in it) is loading.
		_progressBar = new ProgressBar
		{
			ProgressColor = Pink,
			BackgroundColor = Color.FromArgb("#FCE4EC"),
			HeightRequest = 4,
			VerticalOptions = LayoutOptions.Start,
			HorizontalOptions = LayoutOptions.Fill,
			IsVisible = false,
			InputTransparent = true,
		};

		// The screen is blank until the first page arrives, so also show a spinner in the middle for that one.
		_firstLoadSpinner = new ActivityIndicator
		{
			Color = Pink,
			IsRunning = false,
			IsVisible = false,
			HorizontalOptions = LayoutOptions.Center,
			VerticalOptions = LayoutOptions.Center,
			InputTransparent = true,
		};

		_offlinePanel = BuildOfflinePanel();
		_siteDownPanel = BuildSiteDownPanel();

		_siteDownTimer = Dispatcher.CreateTimer();
		_siteDownTimer.Interval = SiteDownRecheckInterval;
		_siteDownTimer.Tick += async (_, _) => await RecheckSiteAsync();

		Content = new Grid { Children = { _webView, _firstLoadSpinner, _progressBar, _offlinePanel, _siteDownPanel } };
	}

	/// <summary>
	/// The "No Internet Connection" image. Its "Try Again" button is part of the picture, so the whole
	/// screen is tappable and retries. It also reloads by itself when the connection comes back.
	/// </summary>
	View BuildOfflinePanel()
	{
		var grid = new Grid
		{
			IsVisible = false,
			Background = BrandBackground(),
		};
		grid.Add(new Image { Source = "no_internet.webp", Aspect = Aspect.AspectFit });

		var tap = new TapGestureRecognizer();
		tap.Tapped += (_, _) => LoadStartPage();
		grid.GestureRecognizers.Add(tap);

		SemanticProperties.SetDescription(grid, "No internet connection. Check your internet or Wi-Fi connection, then tap to try again.");
		return grid;
	}

	/// <summary>Matches the blue/purple background of the offline and maintenance images, so the space around them on tall screens blends in.</summary>
	static LinearGradientBrush BrandBackground() => new(
		[new GradientStop(Color.FromArgb("#4A1FC9"), 0f), new GradientStop(Color.FromArgb("#2433C4"), 0.5f), new GradientStop(Color.FromArgb("#5B1FC9"), 1f)],
		new Point(0, 0), new Point(0, 1));

	/// <summary>The "System Under Maintenance" image (same as the website's maintenance-mobile.webp), with a retry button.</summary>
	View BuildSiteDownPanel()
	{
		var retry = new Button
		{
			Text = "Try again",
			BackgroundColor = Colors.White,
			TextColor = Navy,
			FontAttributes = FontAttributes.Bold,
			CornerRadius = 22,
			Padding = new Thickness(32, 10),
			HorizontalOptions = LayoutOptions.Center,
			Margin = new Thickness(0, 8, 0, 24),
		};
		retry.Clicked += (_, _) => LoadStartPage();

		var grid = new Grid
		{
			IsVisible = false,
			Background = BrandBackground(),
			RowDefinitions = [new RowDefinition(GridLength.Star), new RowDefinition(GridLength.Auto)],
		};
		grid.Add(new Image { Source = "site_down.webp", Aspect = Aspect.AspectFit }, 0, 0);
		grid.Add(retry, 0, 1);
		return grid;
	}

	protected override void OnAppearing()
	{
		base.OnAppearing();
		Connectivity.Current.ConnectivityChanged += OnConnectivityChanged;
		if (!_hasLoadedOnce)
			LoadStartPage();
		else if (_screen == Screen.SiteDown)
			_siteDownTimer.Start();
	}

	protected override void OnDisappearing()
	{
		Connectivity.Current.ConnectivityChanged -= OnConnectivityChanged;
		_siteDownTimer.Stop();
		base.OnDisappearing();
	}

	void LoadStartPage()
	{
		if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet)
		{
			Show(Screen.Offline);
			return;
		}

		Show(Screen.Web);
		_hasLoadedOnce = true;
		StartProgress();
		_webView.Source = new UrlWebViewSource { Url = _startUrl.AbsoluteUri };
	}

	void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs e)
	{
		// Reload automatically when the connection comes back while the offline screen is showing.
		if (e.NetworkAccess == NetworkAccess.Internet && _screen == Screen.Offline)
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

		StartProgress();
	}

	async void OnNavigated(object? sender, WebNavigatedEventArgs e)
	{
		FinishProgress();

		if (e.Result is WebNavigationResult.Failure or WebNavigationResult.Timeout)
		{
			// No network at all: "no internet". Network fine but the site can't be reached: it's down.
			Show(Connectivity.Current.NetworkAccess != NetworkAccess.Internet ? Screen.Offline : Screen.SiteDown);
			return;
		}

		if (e.Result == WebNavigationResult.Success && await PageShowsSiteDownAsync())
			Show(Screen.SiteDown);
	}

	/// <summary>
	/// Asks the loaded page for its HTTP status (Android reports it; iOS doesn't) and, as a fallback,
	/// looks for the text of Azure / IIS "app not running" error pages.
	/// </summary>
	async Task<bool> PageShowsSiteDownAsync()
	{
		const string script =
			"(function(){" +
			"var n=(performance.getEntriesByType&&performance.getEntriesByType('navigation')[0])||{};" +
			"var t=((document.title||'')+' '+((document.body&&document.body.innerText)||'').slice(0,400)).replace(/[\\r\\n|\"\\\\]+/g,' ');" +
			"return (n.responseStatus||0)+'|'+t;" +
			"})()";

		string? result;
		try
		{
			result = await _webView.EvaluateJavaScriptAsync(script);
		}
		catch
		{
			return false; // can't tell; leave the page as it is
		}

		if (string.IsNullOrEmpty(result))
			return false;

		var separator = result.IndexOf('|');
		var statusText = separator >= 0 ? result[..separator] : result;
		var pageText = separator >= 0 ? result[(separator + 1)..] : string.Empty;

		if (int.TryParse(statusText.Trim('"', ' '), out var status) && status != 0)
			return SiteDownStatusCodes.Contains(status);

		return SiteDownPageText.Any(text => pageText.Contains(text, StringComparison.OrdinalIgnoreCase));
	}

	/// <summary>While the maintenance screen shows: quietly check the site, and reload once it answers normally.</summary>
	async Task RecheckSiteAsync()
	{
		if (_screen != Screen.SiteDown || Connectivity.Current.NetworkAccess != NetworkAccess.Internet)
			return;

		try
		{
			using var response = await Probe.GetAsync(_startUrl, HttpCompletionOption.ResponseHeadersRead);
			if (!SiteDownStatusCodes.Contains((int)response.StatusCode) && _screen == Screen.SiteDown)
				LoadStartPage();
		}
		catch
		{
			// Still unreachable; try again on the next tick.
		}
	}

	/// <summary>Real load progress from Android / iOS (see ProgressWebView).</summary>
	void OnLoadProgressChanged(object? sender, double progress)
	{
		if (_screen != Screen.Web)
			return;

		if (progress >= 1)
		{
			FinishProgress();
			return;
		}

		if (!_progressBar.IsVisible)
			StartProgress();

		// Only move forward; the platforms sometimes report a lower value while redirecting.
		if (progress > _progressBar.Progress)
			_ = _progressBar.ProgressTo(progress, 200, Easing.CubicOut);
	}

	/// <summary>Shows the bar at a small value right away, so a tap gets immediate feedback.</summary>
	void StartProgress()
	{
		_loadGeneration++;
		_progressBar.AbortAnimation("Progress");
		if (!_progressBar.IsVisible)
		{
			_progressBar.Progress = 0;
			_progressBar.IsVisible = true;
		}
		if (_progressBar.Progress < 0.1)
			_ = _progressBar.ProgressTo(0.1, 150, Easing.CubicOut);

		if (!_firstPageShown)
			_firstLoadSpinner.IsVisible = _firstLoadSpinner.IsRunning = true;
	}

	/// <summary>Fills the bar, then hides it.</summary>
	async void FinishProgress()
	{
		_firstPageShown = true;
		_firstLoadSpinner.IsVisible = _firstLoadSpinner.IsRunning = false;

		if (!_progressBar.IsVisible)
			return;

		var generation = _loadGeneration;
		await _progressBar.ProgressTo(1, 150, Easing.CubicOut);
		await Task.Delay(150);

		// A new page started loading in the meantime: keep the bar for that one.
		if (generation != _loadGeneration)
			return;

		_progressBar.IsVisible = false;
		_progressBar.Progress = 0;
	}

	void Show(Screen screen)
	{
		_screen = screen;
		_offlinePanel.IsVisible = screen == Screen.Offline;
		_siteDownPanel.IsVisible = screen == Screen.SiteDown;
		_webView.IsVisible = screen == Screen.Web;

		if (screen != Screen.Web)
		{
			_loadGeneration++;
			_progressBar.IsVisible = false;
			_progressBar.Progress = 0;
			_firstLoadSpinner.IsVisible = _firstLoadSpinner.IsRunning = false;
		}

		if (screen == Screen.SiteDown)
			_siteDownTimer.Start();
		else
			_siteDownTimer.Stop();
	}

	/// <summary>Android back button: go back in the website's history before leaving the app.</summary>
	protected override bool OnBackButtonPressed()
	{
		if (_screen == Screen.Web && _webView.CanGoBack)
		{
			_webView.GoBack();
			return true;
		}
		return base.OnBackButtonPressed();
	}
}
