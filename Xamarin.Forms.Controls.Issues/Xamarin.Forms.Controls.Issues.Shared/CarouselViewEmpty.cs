using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows.Input;
using Xamarin.Forms.CustomAttributes;
using Xamarin.Forms.Internals;

#if UITEST
using Xamarin.UITest;
using NUnit.Framework;
using Xamarin.Forms.Core.UITests;
#endif

namespace Xamarin.Forms.Controls.Issues
{
	[Preserve(AllMembers = true)]
	[Issue(IssueTracker.None, 0, "CollectionView EmptyView", PlatformAffected.All)]
#if UITEST
	[NUnit.Framework.Category(UITestCategories.CarouselView)]
#endif
	public class CarouselViewEmpty : TestNavigationPage
	{
		protected override void Init()
		{
#if APP
			Device.SetFlags(new List<string>(Device.Flags ?? new List<string>()) { "CollectionView_Experimental" });

			PushAsync(new GalleryPages.CollectionViewGalleries.CarouselViewGalleries.EmptyCarouselGallery());
#endif
		}

#if UITEST
		[Test]
		public void CarouselEmptyViewVisibleTest()
		{
			RunningApp.WaitForElement("This is the EmptyView");	
			RunningApp.Screenshot("EmptyView is visible");
		}	
			
		[Test]
		public void CarouselHideEmptyViewTest()
		{
			RunningApp.WaitForElement("This is the EmptyView");
			
			RunningApp.Tap(q => q.Marked("Add Items"));

			var exists = RunningApp.QueryUntilPresent(() =>
			{
				var result = RunningApp.WaitForElement("1");

				if (result.Length == 1)
					return result;

				return null;
			});	

			Assert.IsNotNull(exists);
		}
#endif
	}
}