using GoodbyeAhmet.Mobile.Services;
using GoodbyeAhmet.Mobile.ViewModels;

namespace GoodbyeAhmet.Mobile;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});

		// ── Services ────────────────────────────────────────
		builder.Services.AddSingleton<PresetService>();
		builder.Services.AddSingleton<SettingsService>();
		builder.Services.AddSingleton<DnsBlocklistService>();
		builder.Services.AddSingleton(LocalizationService.Instance);

		// ── ViewModels ──────────────────────────────────────
		builder.Services.AddSingleton<MainViewModel>();
		builder.Services.AddTransient<ViewModels.SettingsViewModel>();

		// ── Pages ───────────────────────────────────────────
		builder.Services.AddSingleton<MainPage>();
		builder.Services.AddTransient<SettingsPage>();

		return builder.Build();
	}
}
