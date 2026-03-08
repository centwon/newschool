using System;
using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace NewSchool.Pages;

public sealed partial class HomeroomSettingsPage : Page
{
    private bool _isInitialized = false;

    public HomeroomSettingsPage()
    {
        this.InitializeComponent();
        this.Loaded += HomeroomSettingsPage_Loaded;
    }

    private void HomeroomSettingsPage_Loaded(object sender, RoutedEventArgs e)
    {
        HomeGradeNumberBox.Value = Settings.HomeGrade.Value;
        HomeRoomNumberBox.Value = Settings.HomeRoom.Value;
        UserNameTextBox.Text = Settings.UserName.Value;
        _isInitialized = true;
    }

    private void OnHomeGradeChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (!_isInitialized) return;
        if (!double.IsNaN(args.NewValue))
        {
            Settings.HomeGrade.Set((int)args.NewValue);
        }
    }

    private void OnHomeRoomChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (!_isInitialized) return;
        if (!double.IsNaN(args.NewValue))
        {
            Settings.HomeRoom.Set((int)args.NewValue);
        }
    }

    private void OnUserNameChanged(object sender, RoutedEventArgs e)
    {
        if (!_isInitialized) return;
        if (UserNameTextBox != null)
        {
            Settings.UserName.Set(UserNameTextBox.Text);
        }
    }
}
