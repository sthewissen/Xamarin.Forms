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
			ListenerBase<T> instance,
			ref T refInstance)
		{
			if (instance?._instance != null)
				return instance._instance.Value;

			lock (_lock)
			{
				if (instance?._instance != null)
					return instance._instance.Value;

				refInstance = realCreate();
				instance = refInstance as ListenerBase<T>;
				instance._instance = new Lazy<T>(() =>
				{
					if (targetView.IsDesignerContext())
					{
						instance._attach = designerAttach;
						instance._detach = designerDetach;
					}
					else
					{
						instance._attach = attach;
						instance._detach = detach;
					}

					return instance as T;
				});
			}

			return instance._instance.Value;
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