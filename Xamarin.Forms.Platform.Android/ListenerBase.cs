using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Android.App;
using Android.Content;
using Android.OS;
using Android.Runtime;
using Android.Views;
using Android.Widget;
using AView = Android.Views.View;
using Object = Java.Lang.Object;

namespace Xamarin.Forms.Platform.Android
{
	internal class ListenerBase<T> : Object where T : class
	{
		Action<T, AView> _attach;
		Action<T, AView> _detach;
		Lazy<T> _instance;
		static object _lock = new object();
		public static T GetOrCreate(
			AView targetView,
			Func<T> realCreate,
			Action<T, AView> attach,
			Action<T, AView> detach,
			Action<T, AView> designerAttach,
			Action<T, AView> designerDetach,
			ref T Instance)
		{
			ListenerBase<T> tracer = Instance as ListenerBase<T>;
			if (tracer?._instance != null)
				return tracer._instance.Value;

			lock (_lock)
			{
				if (tracer?._instance != null)
					return tracer._instance.Value;

				Instance = realCreate();
				tracer = Instance as ListenerBase<T>;
				tracer._instance = new Lazy<T>(() =>
				{
					if (tracer is ListenerBase<T> listenerBase)
					{
						if (targetView.IsDesignerContext())
						{
							listenerBase._attach = designerAttach;
							listenerBase._detach = designerDetach;
						}
						else
						{
							listenerBase._attach = attach;
							listenerBase._detach = detach;
						}
					}

					return tracer as T;
				});
			}

			return tracer._instance.Value;
		}

		public void Add(AView attachedView)
		{
			if (_attach == null)
				throw new ArgumentNullException(nameof(_attach));
			if (_instance == null)
				throw new ArgumentNullException(nameof(_instance));

			_attach(_instance.Value, attachedView);
		}

		public void Remove(AView attachedView)
		{
			_detach(_instance.Value, attachedView);
		}
	}
}