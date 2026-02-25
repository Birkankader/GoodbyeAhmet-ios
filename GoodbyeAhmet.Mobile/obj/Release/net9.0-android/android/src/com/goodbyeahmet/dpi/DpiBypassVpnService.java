package com.goodbyeahmet.dpi;


public class DpiBypassVpnService
	extends android.net.VpnService
	implements
		mono.android.IGCUserPeer
{
/** @hide */
	public static final String __md_methods;
	static {
		__md_methods = 
			"n_onStartCommand:(Landroid/content/Intent;II)I:GetOnStartCommand_Landroid_content_Intent_IIHandler\n" +
			"n_onDestroy:()V:GetOnDestroyHandler\n" +
			"";
		mono.android.Runtime.register ("GoodbyeAhmet.Mobile.Platforms.Android.DpiBypassVpnService, GoodbyeAhmet.Mobile", DpiBypassVpnService.class, __md_methods);
	}

	public DpiBypassVpnService ()
	{
		super ();
		if (getClass () == DpiBypassVpnService.class) {
			mono.android.TypeManager.Activate ("GoodbyeAhmet.Mobile.Platforms.Android.DpiBypassVpnService, GoodbyeAhmet.Mobile", "", this, new java.lang.Object[] {  });
		}
	}

	public int onStartCommand (android.content.Intent p0, int p1, int p2)
	{
		return n_onStartCommand (p0, p1, p2);
	}

	private native int n_onStartCommand (android.content.Intent p0, int p1, int p2);

	public void onDestroy ()
	{
		n_onDestroy ();
	}

	private native void n_onDestroy ();

	private java.util.ArrayList refList;
	public void monodroidAddReference (java.lang.Object obj)
	{
		if (refList == null)
			refList = new java.util.ArrayList ();
		refList.add (obj);
	}

	public void monodroidClearReferences ()
	{
		if (refList != null)
			refList.clear ();
	}
}
