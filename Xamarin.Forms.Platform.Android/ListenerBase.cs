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
	internal class ListenerBase<T> : Object
	{
		Action<T, AView> _attach;
		Action<T, AView> _detach;
		T _instance;

		public static T GetOrCreate(
			AView targetView,
			Func<T> realCreate,
			Action<T, AView> attach,
			Action<T, AView> detach,
			Action<T, AView> designerAttach,
			Action<T, AView> designerDetach,
			ref T Instance)
		{
			if (Instance != null)
				return Instance;

			Instance = realCreate();

			if (Instance is ListenerBase<T> tracer)
			{
				if (targetView.IsDesignerContext())
				{
					tracer._attach = designerAttach;
					tracer._detach = designerDetach;
				}
				else
				{
					tracer._attach = attach;
					tracer._detach = detach;
				}

				tracer._instance = Instance;
			}

			return Instance;
		}

		public void Add(AView attachedView)
		{
			_attach(_instance, attachedView);
		}

		public void Remove(AView attachedView)
		{
			_detach(_instance, attachedView);
		}
	}
}