using System;
using System.Collections.ObjectModel;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NewSchool.Controls;
using NewSchool.Models;
using NewSchool.Repositories;
using NewSchool.Dialogs;

namespace NewSchool.Pages;

/// <summary>
/// 동아리 관리 페이지
/// Club 목록 조회, 추가, 수정, 삭제
/// </summary>
public sealed partial class ClubManagementPage : Page
{
    private ObservableCollection<Club> _clubs = new();
    private bool _isInitialized = false;

    public ClubManagementPage()
    {
        this.InitializeComponent();
        this.Loaded += ClubManagementPage_Loaded;
    }

    private void ClubManagementPage_Loaded(object sender, RoutedEventArgs e)
    {
        InitializeFilters();
        _isInitialized = true;
        LoadClubsAsync();
    }

    /// <summary>
    /// 필터 초기화
    /// </summary>
    private void InitializeFilters()
    {
        // 학년도 (최근 5년)
        var currentYear = DateTime.Today.Year;
        for (var i = 0; i < 5; i++)
        {
            var year = currentYear - i;
            var item = new ComboBoxItem { Content = $"{year}학년도", Tag = year };
            CBoxYear.Items.Add(item);

            if (year == Settings.WorkYear.Value)
            {
                CBoxYear.SelectedItem = item;
            }
        }

        // 선택된 항목이 없으면 첫 번째 선택
        if (CBoxYear.SelectedItem == null && CBoxYear.Items.Count > 0)
        {
            CBoxYear.SelectedIndex = 0;
        }
    }

    /// <summary>
    /// 학년도 변경 이벤트
    /// </summary>
    private void OnYearChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isInitialized) return;
        LoadClubsAsync();
    }

    /// <summary>
    /// 동아리 목록 로드
    /// </summary>
    private async void LoadClubsAsync()
    {
        if (CBoxYear.SelectedItem == null) return;

        try
        {
            ShowLoadingState();

            int year = (int)((ComboBoxItem)CBoxYear.SelectedItem).Tag;
            string teacherId = Settings.User.Value;

            if (string.IsNullOrEmpty(teacherId))
            {
                await MessageBox.ShowAsync("오류", "교사 정보를 찾을 수 없습니다.");
                ShowEmptyState();
                return;
            }

            // 동아리 목록 조회
            using var repo = new ClubRepository(SchoolDatabase.DbPath);
            var clubs = await repo.GetByTeacherAsync(teacherId, year);

            _clubs.Clear();
            foreach (var club in clubs)
            {
                _clubs.Add(club);
            }

            ClubListView.ItemsSource = _clubs;

            // UI 업데이트
            UpdateUI();
        }
        catch (Exception ex)
        {
            await MessageBox.ShowAsync("오류", $"동아리 목록 조회 중 오류가 발생했습니다.\n{ex.Message}");
            ShowEmptyState();
        }
    }

    /// <summary>
    /// 동아리 추가 버튼 클릭
    /// </summary>
    private async void OnAddClick(object sender, RoutedEventArgs e)
    {
        if (CBoxYear.SelectedItem == null)
        {
            await MessageBox.ShowAsync("알림", "학년도를 먼저 선택해주세요.");
            return;
        }

        int year = (int)((ComboBoxItem)CBoxYear.SelectedItem).Tag;
        string teacherId = Settings.User.Value;
        string schoolCode = Settings.SchoolCode.Value;

        var dialog = new ClubEditDialog(schoolCode, teacherId, year);
        dialog.XamlRoot = this.XamlRoot;

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            LoadClubsAsync();
        }
    }

    /// <summary>
    /// 동아리 수정 버튼 클릭
    /// </summary>
    private async void OnEditClick(object sender, RoutedEventArgs e)
    {
        var button = sender as Button;
        var club = button?.Tag as Club;
        if (club == null) return;

        var dialog = new ClubEditDialog(club);
        dialog.XamlRoot = this.XamlRoot;

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            LoadClubsAsync();
        }
    }

    /// <summary>
    /// 부원 관리 버튼 클릭
    /// </summary>
    private async void OnEnrollClick(object sender, RoutedEventArgs e)
    {
        var button = sender as Button;
        var club = button?.Tag as Club;
        if (club == null) return;

        var dialog = new ClubEnrollmentDialog(club)
        {
            XamlRoot = this.XamlRoot
        };

        await dialog.ShowAsync();
    }

    /// <summary>
    /// 동아리 삭제 버튼 클릭
    /// </summary>
    private async void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        var button = sender as Button;
        var club = button?.Tag as Club;
        if (club == null) return;

        // 확인 다이얼로그
        var confirmed = await MessageBox.ShowConfirmAsync(
            $"'{club.ClubName}' 동아리를 삭제하시겠습니까?\n등록된 부원 정보도 함께 삭제됩니다.",
            "동아리 삭제", "삭제", "취소");
        if (!confirmed) return;

        try
        {
            using var repo = new ClubRepository(SchoolDatabase.DbPath);
            bool success = await repo.DeleteAsync(club.No);

            if (success)
            {
                await MessageBox.ShowAsync("완료", "동아리가 삭제되었습니다.");
                LoadClubsAsync();
            }
            else
            {
                await MessageBox.ShowAsync("오류", "동아리 삭제에 실패했습니다.");
            }
        }
        catch (Exception ex)
        {
            await MessageBox.ShowAsync("오류", $"동아리 삭제 중 오류가 발생했습니다.\n{ex.Message}");
        }
    }

    #region UI 상태 관리

    private void UpdateUI()
    {
        bool hasClubs = _clubs.Count > 0;

        LoadingState.Visibility = Visibility.Collapsed;
        EmptyState.Visibility = hasClubs ? Visibility.Collapsed : Visibility.Visible;
        ClubListContainer.Visibility = hasClubs ? Visibility.Visible : Visibility.Collapsed;

        TxtClubCount.Text = $"총 {_clubs.Count}개 동아리";
    }

    private void ShowLoadingState()
    {
        LoadingState.Visibility = Visibility.Visible;
        EmptyState.Visibility = Visibility.Collapsed;
        ClubListContainer.Visibility = Visibility.Collapsed;
    }

    private void ShowEmptyState()
    {
        LoadingState.Visibility = Visibility.Collapsed;
        EmptyState.Visibility = Visibility.Visible;
        ClubListContainer.Visibility = Visibility.Collapsed;
    }

    #endregion
}
