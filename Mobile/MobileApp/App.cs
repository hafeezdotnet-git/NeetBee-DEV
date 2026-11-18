namespace NeetBee.Mobile;

public class App : Application
{
	readonly MainPage _mainPage;

	public App(MainPage mainPage)
	{
		_mainPage = mainPage;
		UserAppTheme = AppTheme.Light;
	}

	protected override Window CreateWindow(IActivationState? activationState)
		=> new(_mainPage) { Title = "NEET BEE" };
}
