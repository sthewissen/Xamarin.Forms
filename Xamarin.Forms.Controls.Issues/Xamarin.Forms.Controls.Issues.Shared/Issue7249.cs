using System;
using Xamarin.Forms.CustomAttributes;
using Xamarin.Forms.Internals;

#if UITEST
using NUnit.Framework;
using Xamarin.UITest;
using Xamarin.Forms.Core.UITests;
#endif

namespace Xamarin.Forms.Controls.Issues
{
#if UITEST
	[Category(UITestCategories.ManualReview)]
#endif
	[Preserve(AllMembers = true)]
	[Issue(IssueTracker.Github, 7249, "(Android) Wrong color on Slider", PlatformAffected.Android)]
	public class Issue7249 : TestContentPage
	{
		public Issue7249()
		{
			Title = "Issue 7249";
		}

		protected override void Init()
		{
			var layout = new StackLayout
			{
				Padding = new Thickness(12)
			};

			var instructions = new Label
			{
				Text = "Toggle the first Switch and verify that the color of the Thumb is equal to the Thumb color of the second Switch."
			};

			var switch1 = new Switch
			{
				HorizontalOptions = LayoutOptions.Start,
				IsToggled = false
			};

			var switch2 = new Switch
			{
				HorizontalOptions = LayoutOptions.Start,
				IsToggled = true
			};

			layout.Children.Add(instructions);
			layout.Children.Add(switch1);
			layout.Children.Add(switch2);

			Content = layout;
		}
	}
}