package mono.android.app;

public class ApplicationRegistration {

	public static void registerApplications ()
	{
				// Application and Instrumentation ACWs must be registered first.
		mono.android.Runtime.register ("Microsoft.Maui.MauiApplication, Microsoft.Maui, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null", crc6488302ad6e9e4df1a.MauiApplication.class, crc6488302ad6e9e4df1a.MauiApplication.__md_methods);
		mono.android.Runtime.register ("GoodbyeAhmet.Mobile.MainApplication, GoodbyeAhmet.Mobile, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null", crc6436fe12645dbc4e36.MainApplication.class, crc6436fe12645dbc4e36.MainApplication.__md_methods);
		
	}
}
