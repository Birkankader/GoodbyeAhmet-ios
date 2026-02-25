using GoodbyeAhmet.Mobile.ViewModels;

namespace GoodbyeAhmet.Mobile;

public partial class MainPage : ContentPage
{
	private readonly MainViewModel _vm;

	public MainPage(MainViewModel viewModel)
	{
		InitializeComponent();
		BindingContext = _vm = viewModel;
	}

	protected override void OnAppearing()
	{
		base.OnAppearing();
		// Reload settings that may have changed in SettingsPage
		_vm.ReloadFromSettings();
	}

	private async void OnSettingsTapped(object? sender, TappedEventArgs e)
	{
		await Shell.Current.GoToAsync("SettingsPage");
	}
}

