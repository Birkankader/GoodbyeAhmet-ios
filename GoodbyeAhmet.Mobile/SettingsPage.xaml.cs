namespace GoodbyeAhmet.Mobile;

public partial class SettingsPage : ContentPage
{
    public SettingsPage(ViewModels.SettingsViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }

    private async void OnBackTapped(object? sender, TappedEventArgs e)
    {
        await Shell.Current.GoToAsync("..");
    }
}
