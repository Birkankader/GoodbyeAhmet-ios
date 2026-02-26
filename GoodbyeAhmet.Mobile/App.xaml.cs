using GoodbyeAhmet.Mobile.Services;

namespace GoodbyeAhmet.Mobile;

public partial class App : Application
{
	public App()
	{
		InitializeComponent();
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		// Sync ad-block state from persisted settings → DnsBlocklistService
		var settings = IPlatformApplication.Current!.Services.GetRequiredService<SettingsService>();
		var blocklist = IPlatformApplication.Current!.Services.GetRequiredService<DnsBlocklistService>();

		// Initialize localization with saved language (synchronous – safe on main thread)
		LocalizationService.Instance.LoadLanguageSync(settings.Language);

		if (settings.AdBlockEnabled)
		{
			blocklist.IsEnabled = true;

			// Auto-load cached blocklist from disk (fire-and-forget on startup)
			if (blocklist.DomainCount == 0)
			{
				_ = Task.Run(async () =>
				{
					await blocklist.LoadFromCacheAsync();
					System.Diagnostics.Debug.WriteLine(
						$"[App] Ad-block restored: {blocklist.DomainCount} domains, enabled={blocklist.IsEnabled}");
				});
			}
		}

		return new Window(new AppShell());
	}
}
