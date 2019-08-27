using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using Android.App;
using Android.Content;
using Android.Util;
using Android.Views;
using Xamarin.Forms.Internals;
using AView = Android.Views.View;

namespace Xamarin.Forms.Platform.Android
{
	interface IResourceInflator
	{
		AView Inflate(LayoutInflater inflator, int resourceId);
	}

	internal static class AnticipatorExtensions {

		public static AView Inflate(this Context context, int resourceId)
		{
			Profile.FrameBegin();

			Profile.FramePartition("FromContext");
			var inflator = LayoutInflater.FromContext(context);

			AView result;
			var formsInflator = context as IResourceInflator;
			if (formsInflator != null)
			{
				Profile.FramePartition("Anticipator.Inflate");
				result = formsInflator.Inflate(inflator, resourceId);
			}
			else
			{
				Profile.FramePartition("Unanticipated.Inflate");
				result = inflator.Inflate(resourceId, null);
			}

			Profile.FrameEnd();
			return result;
		}
	}

	public class Anticipator : IResourceInflator
	{
		class Collector
		{
			readonly Anticipator _inflator;

			internal Collector(Anticipator inflator)
			{
				_inflator = inflator;
			}

			~Collector()
			{
				if (GC.GetGeneration(this) == GC.MaxGeneration)
					_inflator._inflationHeap.Clear();

				_inflator._collector.SetTarget(this);
			}
		}

		struct InflateSlot : IEquatable<InflateSlot>
		{
			class EqualityComparer : IEqualityComparer<InflateSlot>
			{

				public bool Equals(InflateSlot x, InflateSlot y)
				{
					return x.Equals(y);
				}

				public int GetHashCode(InflateSlot slot)
				{
					return slot.GetHashCode();
				}
			}

			internal readonly static IEqualityComparer<InflateSlot> Comparer = new EqualityComparer();

			internal readonly int ResourceId;
			internal readonly LayoutInflater Inflator;

			internal InflateSlot(LayoutInflater inflator, int resourceId)
			{
				ResourceId = resourceId;
				Inflator = inflator;
			}

			public override bool Equals(object other)
			{
				if (other is InflateSlot == false)
					return false;

				return Equals((InflateSlot)other);
			}

			public bool Equals(InflateSlot other)
			{
				if (ResourceId != other.ResourceId)
					return false;

				if (Inflator != other.Inflator)
					return false;

				return true;
			}

			public override int GetHashCode()
			{
				return ResourceId.GetHashCode() ^ Inflator.GetHashCode();
			}
		}

		const int ThreadLifeTimeSeconds = 5;
		readonly static TimeSpan LoopTimeOut = TimeSpan.FromSeconds(ThreadLifeTimeSeconds);
		readonly static Func<InflateSlot, AView, AView> Update = (l, r) => r;

		readonly Thread[] _threads;
		readonly AutoResetEvent[] _signals;
		readonly WeakReference<Collector> _collector;
		readonly ConcurrentDictionary<InflateSlot, AView> _inflationHeap;
		readonly ConcurrentQueue<Action> _actions;

		internal Anticipator()
		{
			_collector = new WeakReference<Collector>(new Collector(this));
			_inflationHeap = new ConcurrentDictionary<InflateSlot, AView>(InflateSlot.Comparer);
			_actions = new ConcurrentQueue<Action>();

			_threads = new Thread[Environment.ProcessorCount];
			_signals = new AutoResetEvent[Environment.ProcessorCount];

			_signals[0] = new AutoResetEvent(true);
			_threads[0] = new Thread(Loop);
			_threads[0].Start(_signals[0]);

			Anticipate(() =>
			{
				for (var i = 1; i < _threads.Length; i++)
				{
					_signals[i] = new AutoResetEvent(true);
					_threads[i] = new Thread(Loop);
					_threads[i].Start(_signals[i]);
				}
			});
		}

		void Loop(object argument)
		{
			var signal = (AutoResetEvent)argument;

			while (signal.WaitOne(LoopTimeOut))
			{
				// process actions
				while (_actions.Count > 0)
				{
					if (!_actions.TryDequeue(out Action action))
						continue; // lost race to dequeue

					action();
				}

				// proccess inflations
				foreach (var keyValue in _inflationHeap)
				{
					if (keyValue.Value != null)
						continue;

					if (!_inflationHeap.TryRemove(keyValue.Key, out AView view))
						continue; // lost race to own inflation of this resorce

					var slot = keyValue.Key;
					view = slot.Inflator.Inflate(slot.ResourceId, null);
					_inflationHeap.AddOrUpdate(slot, view, Update);
				}
			}
		}

		void Signal()
		{
			for (var i = 0; i < _signals.Length; i++)
			{
				if (_signals[i] != null)
					_signals[i].Set();
			}
		}

		internal void Anticipate(Action action)
		{
			if (action == null)
				throw new ArgumentNullException(nameof(action));

			_actions.Enqueue(action);

			Signal();
		}

		internal void AnticipateClassConstruction(Type type)
		{
			Anticipate(() => RuntimeHelpers.RunClassConstructor(type.TypeHandle));
		}

		public void AnticipateGetter<T>(Func<T> getter)
		{
			Anticipate(() => getter());
		}

		public void Anticipate(LayoutInflater inflator, int resourceId)
		{
			if (inflator == null)
				throw new ArgumentNullException(nameof(inflator));

			var slot = new InflateSlot(inflator, resourceId);

			_inflationHeap.AddOrUpdate(slot, default(AView), Update);

			Signal();
		}

		internal AView Inflate(LayoutInflater inflator, int resourceId)
		{
			Profile.FrameBegin();
			var result = InflateHelper(inflator, resourceId);
			Profile.FrameEnd();
			return result;
		}
		AView InflateHelper(LayoutInflater inflator, int resourceId)
		{

			if (inflator == null)
				throw new ArgumentNullException(nameof(inflator));

			var slot = new InflateSlot(inflator, resourceId);

			Profile.FramePartition("TryRemove");
			if (_inflationHeap.TryRemove(slot, out AView view))
			{
				if (view != null)
				{
					Profile.FramePartition("Hit");
					//Log.Info("Anticipator", $"Won race to inflate resource: ${resourceId}");
					return view;
				}

				Profile.FramePartition("Miss");
				//Log.Info("Anticipator", $"Lost race to inflate resource: ${resourceId}");
			}
			else
			{
				Profile.FramePartition("Unanticipated");
				//Log.Info("Anticipator", $"Unanticipated inflation of resource: ${resourceId}");
			}

			return inflator.Inflate(resourceId, null);
		}

		AView IResourceInflator.Inflate(LayoutInflater inflator, int resourceId)
		{
			return Inflate(inflator, resourceId);
		}
	}
}