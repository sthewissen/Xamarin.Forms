#region

using System;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using Android.App;
using Android.Content;
using Android.Content.Res;
using Android.OS;
using Android.Runtime;
using Android.Support.V7.App;
using Android.Views;
using Xamarin.Forms.Platform.Android.AppCompat;
using Xamarin.Forms.PlatformConfiguration.AndroidSpecific;
using Xamarin.Forms.PlatformConfiguration.AndroidSpecific.AppCompat;
using AToolbar = Android.Support.V7.Widget.Toolbar;
using AColor = Android.Graphics.Color;
using AView = Android.Views.View;
using ARelativeLayout = Android.Widget.RelativeLayout;
using Xamarin.Forms.Internals;
using System.Runtime.CompilerServices;

using FLabelRenderer = Xamarin.Forms.Platform.Android.FastRenderers.LabelRenderer;
using FButtonRenderer = Xamarin.Forms.Platform.Android.FastRenderers.ButtonRenderer;
using FImageRenderer = Xamarin.Forms.Platform.Android.FastRenderers.ImageRenderer;
using FFrameRenderer = Xamarin.Forms.Platform.Android.FastRenderers.FrameRenderer;
using Android.Graphics.Drawables;
using Android.Graphics;
using AFragment = Android.Support.V4.App.Fragment;

#endregion

namespace Xamarin.Forms.Platform.Android
{
	[Flags]
	public enum ActivationFlags : long
	{
		DisableSetStatusBarColor = 1 << 0,
	}

	public struct ActivationOptions
	{
		public ActivationOptions(Bundle bundle)
		{
			this = default(ActivationOptions);
			this.Bundle = bundle;
		}
		public Bundle Bundle;
		public ActivationFlags Flags;
	}

	public class FormsAppCompatActivity : AppCompatActivity, IDeviceInfoProvider, 
		IResourceInflator
	{
		public delegate bool BackButtonPressedEventHandler(object sender, EventArgs e);

		Application _application;

		readonly Anticipator _anticipator;
		object _accentColor;
		DeviceInfo _deviceInfo;

		AndroidApplicationLifecycleState _currentState;
		ARelativeLayout _layout;

		internal AppCompat.Platform Platform { get; private set; }

		AndroidApplicationLifecycleState _previousState;

		bool _renderersAdded;
		bool _activityCreated;
		PowerSaveModeBroadcastReceiver _powerSaveModeBroadcastReceiver;

		static readonly ManualResetEventSlim PreviousActivityDestroying = new ManualResetEventSlim(true);

		// Override this if you want to handle the default Android behavior of restoring fragments on an application restart
		protected virtual bool AllowFragmentRestore => false;

		protected FormsAppCompatActivity()
		{
			_previousState = AndroidApplicationLifecycleState.Uninitialized;
			_currentState = AndroidApplicationLifecycleState.Uninitialized;
			PopupManager.Subscribe(this);

			_anticipator = new Anticipator();
			_anticipator.AnticipateGetter(() => Forms.SdkInt);
			_anticipator.AnticipateClassConstruction(typeof(Resource.Layout));
			_anticipator.AnticipateClassConstruction(typeof(Resource.Attribute));
			_anticipator.Anticipate(() => _accentColor = Forms.GetAccentColor(this));
			_anticipator.Anticipate(() => _deviceInfo = new Forms.AndroidDeviceInfo(this));
			_anticipator.Anticipate(() => {
				new PageRenderer(this);
				new FLabelRenderer(this);
				new FButtonRenderer(this);
				new FImageRenderer(this);
				new FFrameRenderer(this);
				new ListViewRenderer(this);
				new AFragment();
				new DummyDrawable();
			});
		}
		class DummyDrawable : Drawable
		{
			public override int Opacity => 0;
			public override void Draw(Canvas canvas) { }
			public override void SetAlpha(int alpha) { }
			public override void SetColorFilter(ColorFilter colorFilter) { }
		}

		internal DeviceInfo DeviceInfo
		{
			get
			{
				if (_deviceInfo == null)
					Interlocked.CompareExchange(ref _deviceInfo, new Forms.AndroidDeviceInfo(this), null);
				return _deviceInfo;
			}
		}

		internal Color AccentColor
		{
			get
			{
				if (_accentColor == null)
					Interlocked.CompareExchange(ref _accentColor, Forms.GetAccentColor(this), null);
				return (Color)_accentColor;
			}
		}

		public event EventHandler ConfigurationChanged;

		public override void OnBackPressed()
		{
			if (BackPressed != null && BackPressed(this, EventArgs.Empty))
				return;
			base.OnBackPressed();
		}

		public override void OnConfigurationChanged(Configuration newConfig)
		{
			base.OnConfigurationChanged(newConfig);
			ConfigurationChanged?.Invoke(this, new EventArgs());
		}

		public override bool OnOptionsItemSelected(IMenuItem item)
		{
			if (item.ItemId == global::Android.Resource.Id.Home)
				BackPressed?.Invoke(this, EventArgs.Empty);

			return base.OnOptionsItemSelected(item);
		}

		public void SetStatusBarColor(AColor color)
		{
			if (Forms.IsLollipopOrNewer)
			{
				Window.SetStatusBarColor(color);
			}
		}

		static void RegisterHandler(Type target, Type handler, Type filter)
		{
			Profile.FrameBegin();

			Profile.FramePartition(target.Name);
			Type current = Registrar.Registered.GetHandlerType(target);

			if (current == filter)
			{
				Profile.FramePartition("Register");
				Registrar.Registered.Register(target, handler);
			}

			Profile.FrameEnd();
		}

		// This is currently being used by the previewer please do not change or remove this
		static void RegisterHandlers()
		{
			RegisterHandler(typeof(NavigationPage), typeof(NavigationPageRenderer), typeof(NavigationRenderer));
			RegisterHandler(typeof(TabbedPage), typeof(TabbedPageRenderer), typeof(TabbedRenderer));
			RegisterHandler(typeof(MasterDetailPage), typeof(MasterDetailPageRenderer), typeof(MasterDetailRenderer));
			RegisterHandler(typeof(Switch), typeof(AppCompat.SwitchRenderer), typeof(SwitchRenderer));
			RegisterHandler(typeof(Picker), typeof(AppCompat.PickerRenderer), typeof(PickerRenderer));
			RegisterHandler(typeof(CarouselPage), typeof(AppCompat.CarouselPageRenderer), typeof(CarouselPageRenderer));
			RegisterHandler(typeof(CheckBox), typeof(CheckBoxRenderer), typeof(CheckBoxDesignerRenderer));

			if (Forms.Flags.Contains(Flags.UseLegacyRenderers))
			{
				RegisterHandler(typeof(Button), typeof(AppCompat.ButtonRenderer), typeof(ButtonRenderer));
				RegisterHandler(typeof(Frame), typeof(AppCompat.FrameRenderer), typeof(FrameRenderer));
			}
			else
			{
				RegisterHandler(typeof(Button), typeof(FastRenderers.ButtonRenderer), typeof(ButtonRenderer));
				RegisterHandler(typeof(Label), typeof(FastRenderers.LabelRenderer), typeof(LabelRenderer));
				RegisterHandler(typeof(Image), typeof(FastRenderers.ImageRenderer), typeof(ImageRenderer));
				RegisterHandler(typeof(Frame), typeof(FastRenderers.FrameRenderer), typeof(FrameRenderer));
			}
		}

		protected void LoadApplication(Application application)
		{
			Profile.FrameBegin();
			if (!_activityCreated)
			{
				throw new InvalidOperationException("Activity OnCreate was not called prior to loading the application. Did you forget a base.OnCreate call?");
			}

			if (!_renderersAdded)
			{
				Profile.FramePartition("RegisterHandlers");
				RegisterHandlers();
				_renderersAdded = true;
			}

			if (_application != null)
				_application.PropertyChanged -= AppOnPropertyChanged;

			Profile.FramePartition("SetAppIndexingProvider");
			_application = application ?? throw new ArgumentNullException(nameof(application));
			((IApplicationController)application).SetAppIndexingProvider(new AndroidAppIndexProvider(this));

			Profile.FramePartition("SetCurrentApplication");
			Xamarin.Forms.Application.SetCurrentApplication(application);

			Profile.FramePartition("SetSoftInputMode");
			if (Xamarin.Forms.Application.Current.OnThisPlatform().GetWindowSoftInputModeAdjust() != WindowSoftInputModeAdjust.Unspecified)
				SetSoftInputMode();

			Profile.FramePartition("CheckForAppLink");
			CheckForAppLink(Intent);

			application.PropertyChanged += AppOnPropertyChanged;

			// Wait if old activity destroying is not finished
			PreviousActivityDestroying.Wait();

			Profile.FramePartition(nameof(SetMainPage));
			SetMainPage();

			Profile.FrameEnd();
		}

		protected override void OnActivityResult(int requestCode, Result resultCode, Intent data)
		{
			base.OnActivityResult(requestCode, resultCode, data);
			ActivityResultCallbackRegistry.InvokeCallback(requestCode, resultCode, data);
		}

		protected void OnCreate(ActivationOptions options)
		{
			OnCreate(options.Bundle, options.Flags);
		}

		protected override void OnCreate(Bundle savedInstanceState)
		{
			OnCreate(savedInstanceState, default(ActivationFlags));
		}

		void OnCreate(
			Bundle savedInstanceState, 
			ActivationFlags flags)
		{
			Profile.FrameBegin();

			Profile.FramePartition("Anticipate");
			var layout = default(ARelativeLayout);
			_anticipator.Anticipate(() => layout = new ARelativeLayout(BaseContext));

			//ToolbarResource = Resource.Layout.Toolbar;
			_activityCreated = true;
			if (!AllowFragmentRestore)
			{
				// Remove the automatically persisted fragment structure; we don't need them
				// because we're rebuilding everything from scratch. This saves a bit of memory
				// and prevents loading errors from child fragment managers
				savedInstanceState?.Remove("android:support:fragments");
			}

			Profile.FramePartition("Xamarin.Android.OnCreate");
			base.OnCreate(savedInstanceState);

			AToolbar bar;
			if (ToolbarResource != 0)
			{
				Profile.FramePartition("Inflate ToolbarResource");
				bar = _anticipator.Inflate(LayoutInflater, ToolbarResource).JavaCast<AToolbar>();
				if (bar == null)
					throw new InvalidOperationException("ToolbarResource must be set to a Android.Support.V7.Widget.Toolbar");
			}
			else 
			{
				Profile.FramePartition("Activate Toolbar");
				bar = new AToolbar(this);
			}

			Profile.FramePartition("Set ActionBar");
			SetSupportActionBar(bar);

			if (layout == null)
			{
				Profile.FramePartition("Create ARelativeLayout");
				_layout = new ARelativeLayout(BaseContext);
			}
			else
			{
				Profile.FramePartition("Anticipated ARelativeLayout");
				_layout = layout;
			}

			Profile.FramePartition("SetContentView");
			_layout = new ARelativeLayout(BaseContext);
			SetContentView(_layout);

			Profile.FramePartition("OnStateChanged");
			Xamarin.Forms.Application.ClearCurrent();

			_previousState = _currentState;
			_currentState = AndroidApplicationLifecycleState.OnCreate;

			if (_application != null)
				OnStateChanged();

			Profile.FramePartition("Forms.IsLollipopOrNewer");
			if (Forms.IsLollipopOrNewer)
			{
				// Allow for the status bar color to be changed
				if ((flags & ActivationFlags.DisableSetStatusBarColor) == 0)
				{
					Profile.FramePartition("Set DrawsSysBarBkgrnds");
					Window.AddFlags(WindowManagerFlags.DrawsSystemBarBackgrounds);
				}
				// Listen for the device going into power save mode so we can handle animations being disabled
				Profile.FramePartition("Allocate PowerSaveModeReceiver");
				_powerSaveModeBroadcastReceiver = new PowerSaveModeBroadcastReceiver();
			}

			Profile.FrameEnd();
		}

		protected override void OnDestroy()
		{
			PreviousActivityDestroying.Reset();

			if (_application != null)
				_application.PropertyChanged -= AppOnPropertyChanged;

			PopupManager.Unsubscribe(this);

			if (Platform != null)
			{
				_layout.RemoveView(Platform);
				Platform.Dispose();
			}

			PreviousActivityDestroying.Set();

			// call at the end to avoid race conditions with Platform dispose
			base.OnDestroy();
		}

		protected override void OnNewIntent(Intent intent)
		{
			base.OnNewIntent(intent);
			CheckForAppLink(intent);
		}

		protected override void OnPause()
		{
			_layout.HideKeyboard(true);

			if (Forms.IsLollipopOrNewer)
			{
				// Don't listen for power save mode changes while we're paused
				UnregisterReceiver(_powerSaveModeBroadcastReceiver);
			}

			// Stop animations or other ongoing actions that could consume CPU
			// Commit unsaved changes, build only if users expect such changes to be permanently saved when thy leave such as a draft email
			// Release system resources, such as broadcast receivers, handles to sensors (like GPS), or any resources that may affect battery life when your activity is paused.
			// Avoid writing to permanent storage and CPU intensive tasks
			base.OnPause();

			_previousState = _currentState;
			_currentState = AndroidApplicationLifecycleState.OnPause;

			OnStateChanged();
		}

		protected override void OnRestart()
		{
			base.OnRestart();

			_previousState = _currentState;
			_currentState = AndroidApplicationLifecycleState.OnRestart;

			OnStateChanged();
		}

		protected override void OnResume()
		{
			Profile.FrameBegin();

			// counterpart to OnPause
			base.OnResume();

			if (_application != null && CurrentFocus != null && _application.OnThisPlatform().GetShouldPreserveKeyboardOnResume())
			{
				CurrentFocus.ShowKeyboard();
			}

			_previousState = _currentState;
			_currentState = AndroidApplicationLifecycleState.OnResume;

			if (Forms.IsLollipopOrNewer)
			{
				// Start listening for power save mode changes
				RegisterReceiver(_powerSaveModeBroadcastReceiver, new IntentFilter(
					PowerManager.ActionPowerSaveModeChanged
				));
			}

			OnStateChanged();

			Profile.FrameEnd();
		}

		protected override void OnStart()
		{
			Profile.FrameBegin();

			Profile.FramePartition("Android OnStart");
			base.OnStart();

			_previousState = _currentState;
			_currentState = AndroidApplicationLifecycleState.OnStart;

			Profile.FramePartition("OnStateChanged");
			OnStateChanged();

			Profile.FrameEnd();
		}

		// Scenarios that stop and restart your app
		// -- Switches from your app to another app, activity restarts when clicking on the app again.
		// -- Action in your app that starts a new Activity, the current activity is stopped and the second is created, pressing back restarts the activity
		// -- The user receives a phone call while using your app on his or her phone
		protected override void OnStop()
		{
			// writing to storage happens here!
			// full UI obstruction
			// users focus in another activity
			// perform heavy load shutdown operations
			// clean up resources
			// clean up everything that may leak memory
			base.OnStop();

			_previousState = _currentState;
			_currentState = AndroidApplicationLifecycleState.OnStop;

			OnStateChanged();
		}

		void AppOnPropertyChanged(object sender, PropertyChangedEventArgs args)
		{
			// Activity in pause must not react to application changes
			if (_currentState >= AndroidApplicationLifecycleState.OnPause)
			{
				return;
			}

			if (args.PropertyName == nameof(_application.MainPage))
				SetMainPage();
			if (args.PropertyName == PlatformConfiguration.AndroidSpecific.Application.WindowSoftInputModeAdjustProperty.PropertyName)
				SetSoftInputMode();
		}

		void CheckForAppLink(Intent intent)
		{
			string action = intent.Action;
			string strLink = intent.DataString;
			if (Intent.ActionView != action || string.IsNullOrWhiteSpace(strLink))
				return;

			var link = new Uri(strLink);
			_application?.SendOnAppLinkRequestReceived(link);
		}

		void InternalSetPage(Page page)
		{
			if (!Forms.IsInitialized)
				throw new InvalidOperationException("Call Forms.Init (Activity, Bundle) before this");

			if (Platform != null)
			{
				Platform.SetPage(page);
				return;
			}

			PopupManager.ResetBusyCount(this);

			Platform = new AppCompat.Platform(this);

			Platform.SetPage(page);
			_layout.AddView(Platform);
			_layout.BringToFront();
		}

		void OnStateChanged()
		{
			if (_application == null)
				return;

			if (_previousState == AndroidApplicationLifecycleState.OnCreate && _currentState == AndroidApplicationLifecycleState.OnStart)
				_application.SendStart();
			else if (_previousState == AndroidApplicationLifecycleState.OnStop && _currentState == AndroidApplicationLifecycleState.OnRestart)
				_application.SendResume();
			else if (_previousState == AndroidApplicationLifecycleState.OnPause && _currentState == AndroidApplicationLifecycleState.OnStop)
				_application.SendSleep();
		}

		// This is currently being used by the previewer please do not change or remove this
		void RegisterHandlerForDefaultRenderer(Type target, Type handler, Type filter)
		{
			RegisterHandler(target, handler, filter);
		}

		void SetMainPage()
		{
			InternalSetPage(_application.MainPage);
		}

		void SetSoftInputMode()
		{
			var adjust = SoftInput.AdjustPan;

			if (Xamarin.Forms.Application.Current != null)
			{
				WindowSoftInputModeAdjust elementValue = Xamarin.Forms.Application.Current.OnThisPlatform().GetWindowSoftInputModeAdjust();
				switch (elementValue)
				{
					case WindowSoftInputModeAdjust.Resize:
						adjust = SoftInput.AdjustResize;
						break;
					case WindowSoftInputModeAdjust.Unspecified:
						adjust = SoftInput.AdjustUnspecified;
						break;
					default:
						adjust = SoftInput.AdjustPan;
						break;
				}
			}

			Window.SetSoftInputMode(adjust);
		}

		AView IResourceInflator.Inflate(LayoutInflater inflator, int resourceId)
		{
			return _anticipator.Inflate(inflator, resourceId);
		}

		internal class DefaultApplication : Application
		{
		}

		#region Statics

		public static event BackButtonPressedEventHandler BackPressed;

		public static int TabLayoutResource { get; set; }
		public static int ToolbarResource { get; set; }
		#endregion
	}
}
