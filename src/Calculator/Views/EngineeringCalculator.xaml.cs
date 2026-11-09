// Licensed under the MIT License.

// Host page for the multi-field calculators (Electric power, Ohm's law, Mechanical power).
// The view model is swapped by MainPage when the user switches between those modes.

using CalculatorApp.ViewModel;

using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace CalculatorApp
{
    [Windows.Foundation.Metadata.WebHostHidden]
    public sealed partial class EngineeringCalculator : UserControl
    {
        public EngineeringCalculatorViewModel ViewModel
        {
            get => (EngineeringCalculatorViewModel)GetValue(ViewModelProperty);
            set => SetValue(ViewModelProperty, value);
        }

        public static readonly DependencyProperty ViewModelProperty =
            DependencyProperty.Register(nameof(ViewModel), typeof(EngineeringCalculatorViewModel), typeof(EngineeringCalculator), new PropertyMetadata(null));

        public EngineeringCalculator()
        {
            InitializeComponent();
        }

        public void SetDefaultFocus()
        {
            // Focus the first editable text box once the items have been generated.
            _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Low, () =>
            {
                var textBox = FindFirstEditableTextBox(FieldList);
                textBox?.Focus(FocusState.Programmatic);
            });
        }

        private static TextBox FindFirstEditableTextBox(DependencyObject root)
        {
            int count = Windows.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = Windows.UI.Xaml.Media.VisualTreeHelper.GetChild(root, i);
                if (child is TextBox textBox && !textBox.IsReadOnly)
                {
                    return textBox;
                }
                var found = FindFirstEditableTextBox(child);
                if (found != null)
                {
                    return found;
                }
            }
            return null;
        }
    }
}
