namespace GoodbyeAhmet.Mobile;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();
        Routing.RegisterRoute("SettingsPage", typeof(SettingsPage));
    }
}
