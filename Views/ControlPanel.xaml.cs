using HACS.Components;
using HACS.Core;
using HACS.WPF.Behaviors;
//using HACS.WPF.Views;
using Microsoft.Xaml.Behaviors;
using System;
using System.Collections.ObjectModel;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using System.Windows;
using HACS.WPF.Views;

namespace AeonCegs.Views
{
	/// <summary>
	/// Interaction logic for ControlPanel.xaml
	/// </summary>
	public partial class ControlPanel : HACS.WPF.Views.ControlPanel<AeonHacs>
	{
		ResourceDictionary Preferences = (ResourceDictionary)Application.Current.Resources["PreferencesDictionary"];

		HacsBase Hacs => Bridge?.GetHacs();

		// Empty constructor required for the designer to work.
		public ControlPanel()
		{
			InitializeComponent();
		}

		// Parameterized constructor called by the application on startup.
		public ControlPanel(Action closeAction) : base(closeAction)
		{
			InitializeComponent();
			RestorePreferences();
			PopulateProcessSelector();
			StartUpdateCycle();
		}

		void RestorePreferences()
		{
			var obj = Preferences["ControlPanel.GraphiteReactorData.Visibility"];
			if (obj is Visibility visibility)
				GraphiteReactorData.Visibility = visibility;
		}

		void PopulateProcessSelector()
		{
			if (Hacs is ProcessManager pm)
			{
				var items = new ObservableCollection<object>();
				foreach (var process in pm.ProcessDictionary)
				{
					if (process.Value == null)
						items.Add(new Separator());
					else
						items.Add(process.Key);
				}
				ProcessSelector.ItemsSource = items;
			}
		}

		void StartUpdateCycle()
		{
			// 100 millisecond Update() timer
			new DispatcherTimer(new TimeSpan(10000 * 100), DispatcherPriority.Background, (sender, e) => Update(), Dispatcher.CurrentDispatcher);
		}

		void UpdateContent(ContentControl control) =>
			BindingOperations.GetBindingExpression(control, ContentProperty)?.UpdateTarget();

		void Update()
		{
			UpdateContent(Uptime);
			UpdateContent(ProcessTime);
			UpdateContent(ProcessStepTime);
			UpdateContent(ProcessSubstepTime);
		}

		private void StartButton_Click(object sender, System.Windows.RoutedEventArgs e)
		{
			if (Hacs is ProcessManager pm && !pm.Busy && ProcessSelector.SelectedItem is string processName)
				pm.RunProcess(processName);
		}

		private void GraphiteManifold_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
		{
			GraphiteReactorData.Visibility = GraphiteReactorData.Visibility == Visibility.Visible ? 
				Visibility.Hidden : Visibility.Visible;
			Preferences["ControlPanel.GraphiteReactorData.Visibility"] = GraphiteReactorData.Visibility;
		}
	}
}
