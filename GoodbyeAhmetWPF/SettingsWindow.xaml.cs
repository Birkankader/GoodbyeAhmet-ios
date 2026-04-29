using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using GoodbyeAhmetWPF.Services;

namespace GoodbyeAhmetWPF
{
    public partial class SettingsWindow : Window
    {
        public SettingsWindow()
        {
            InitializeComponent();
        }

        private void PresetLabel_Click(object sender, MouseButtonEventArgs e)
        {
            PresetComboBox.IsDropDownOpen = !PresetComboBox.IsDropDownOpen;
        }

        // Allow only digits in TTL/Port TextBoxes (PreviewTextInput).
        private void NumericOnly_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !InputValidator.IsDigitsOnly(e.Text);
        }

        // Block paste of non-numeric text.
        private void NumericOnly_Pasting(object sender, DataObjectPastingEventArgs e)
        {
            if (e.DataObject.GetDataPresent(typeof(string)))
            {
                var text = (string)e.DataObject.GetData(typeof(string))!;
                if (!InputValidator.IsDigitsOnly(text)) e.CancelCommand();
            }
            else
            {
                e.CancelCommand();
            }
        }
    }
}
