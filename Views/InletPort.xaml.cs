using HACS.WPF.Views;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace AeonCegs.Views
{
    /// <summary>
    /// Interaction logic for InletPort.xaml
    /// </summary>
    public partial class InletPort : View
    {

        #region PortType

        public static readonly DependencyProperty PortTypeProperty = DependencyProperty.Register(
            nameof(PortType), typeof(HACS.Components.InletPort.Type), typeof(InletPort));

        public HACS.Components.InletPort.Type PortType
        {
            get => (HACS.Components.InletPort.Type)GetValue(PortTypeProperty);
            set => SetValue(PortTypeProperty, value);
        }

        #endregion PortType 

        #region View Visiblity

        public static readonly DependencyProperty CombustionVisibilityProperty = DependencyProperty.Register(
            nameof(CombustionVisibility), typeof(Visibility), typeof(InletPort));
        public bool CombustionVisibility
        {
            get => (bool)GetValue(CombustionVisibilityProperty);
            set => SetValue(CombustionVisibilityProperty, value);
        }


        public static readonly DependencyProperty NeedleVisibilityProperty = DependencyProperty.Register(
            nameof(NeedleVisibility), typeof(Visibility), typeof(InletPort));
        public Visibility NeedleVisibility
        {
            get => (Visibility)GetValue(NeedleVisibilityProperty);
            set => SetValue(NeedleVisibilityProperty, value);
        }


        public static readonly DependencyProperty BreaksealVisibilityProperty = DependencyProperty.Register(
            nameof(BreaksealVisibility), typeof(Visibility), typeof(InletPort));
        public Visibility BreaksealVisibility
        {
            get => (Visibility)GetValue(BreaksealVisibilityProperty);
            set => SetValue(BreaksealVisibilityProperty, value);
        }

        class DefaultVisibilityConverter : IValueConverter
        {
            public static DefaultVisibilityConverter Default = new DefaultVisibilityConverter();

            public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
                Hide(value, parameter) ? Visibility.Hidden : Visibility.Visible;
            public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            {
                throw new NotImplementedException();
            }

            // true iff the given port type is valid and is contained in a valid list of exceptions,
            bool Hide(object value, object parameter) =>
                !(value is HACS.Components.InletPort.Type type &&
                parameter is List<HACS.Components.InletPort.Type> exceptions &&
                !exceptions.Contains(type));
        }


        [ValueConversion(typeof(HACS.Components.InletPort.Type), typeof(Visibility))]
        class SelectedVisibilityConverter : IValueConverter
        {
            public static SelectedVisibilityConverter Default = new SelectedVisibilityConverter();

            public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            {
                var result = 
                    value != null && value.Equals(parameter) ? Visibility.Visible : Visibility.Hidden;
                return result;
            }
            public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            {
                throw new NotImplementedException();
            }
        }
        #endregion View Visiblity

        public InletPort()
        {
            InitializeComponent();

            SetBinding(PortTypeProperty, new Binding("Component.PortType") { Source = this });

            SetBinding(CombustionVisibilityProperty, new Binding(nameof(PortType))
            {
                Source = this,
                Converter = SelectedVisibilityConverter.Default,
                ConverterParameter = HACS.Components.InletPort.Type.Combustion
            });

            SetBinding(NeedleVisibilityProperty, new Binding(nameof(PortType))
            {
                Source = this,
                Converter = SelectedVisibilityConverter.Default,
                ConverterParameter = HACS.Components.InletPort.Type.Needle
            });

            SetBinding(BreaksealVisibilityProperty, new Binding(nameof(PortType))
            {
                Source = this,
                Converter = SelectedVisibilityConverter.Default,
                ConverterParameter = HACS.Components.InletPort.Type.Manual
            });
        }
    }
}
