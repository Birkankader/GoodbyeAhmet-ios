using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using GoodbyeAhmet.Mobile.Platforms.Android;

namespace GoodbyeAhmet.Mobile;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        // Set status bar and navigation bar to match app background (#121212)
        if (Window != null)
        {
            Window.SetStatusBarColor(global::Android.Graphics.Color.ParseColor("#121212"));
            Window.SetNavigationBarColor(global::Android.Graphics.Color.ParseColor("#121212"));

            // Light status bar = false → white icons on dark bar
            if (Build.VERSION.SdkInt >= BuildVersionCodes.R)
            {
#pragma warning disable CA1416
                var controller = Window.InsetsController;
                if (controller != null)
                {
                    controller.SetSystemBarsAppearance(0, (int)WindowInsetsControllerAppearance.LightStatusBars);
                    controller.SetSystemBarsAppearance(0, (int)WindowInsetsControllerAppearance.LightNavigationBars);
                }
#pragma warning restore CA1416
            }
            else
            {
#pragma warning disable CS0618
                Window.DecorView.SystemUiVisibility &= ~(StatusBarVisibility)SystemUiFlags.LightStatusBar;
#pragma warning restore CS0618
            }
        }
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        VpnHelper.OnActivityResult(requestCode, resultCode);
    }
}
