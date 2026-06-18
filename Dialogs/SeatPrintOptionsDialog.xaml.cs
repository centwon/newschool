using Microsoft.UI.Xaml.Controls;
using NewSchool.Services;

namespace NewSchool.Dialogs
{
    public sealed partial class SeatPrintOptionsDialog : ContentDialog
    {
        public SeatPrintOptionsDialog()
        {
            this.InitializeComponent();
        }

        public PrintOrientation Orientation
        {
            get
            {
                if (RbPortrait.IsChecked == true) return PrintOrientation.Portrait;
                if (RbLandscape.IsChecked == true) return PrintOrientation.Landscape;
                return PrintOrientation.Auto;
            }
        }

        public bool IncludeRoster => ChkIncludeRoster.IsChecked == true;
    }
}
