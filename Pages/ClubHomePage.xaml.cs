using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NewSchool.Board.Pages;
using NewSchool.Models;
using NewSchool.Repositories;
using NewSchool.Services;

namespace NewSchool.Pages;

/// <summary>
/// 동아리 홈 페이지
/// 동아리 현황 및 자료실 (PostListPage 내장)
/// </summary>
public sealed partial class ClubHomePage : Page
{
    #region Fields

    private Club? _selectedClub;

    // 자료실 (PostListPage 내장)
    private const string MaterialCategory = "동아리";  // ← "동아리자료"에서 "동아리"로 변경
    private PostListPage? _postListPage;

    public ObservableCollection<Club> Clubs { get; } = new();
    public ObservableCollection<Enrollment> Members { get; } = new();

    #endregion

    #region Constructor

    public ClubHomePage()
    {
        this.InitializeComponent();

        CBoxClub.ItemsSource = Clubs;
        MemberListView.ItemsSource = Members;
    }

    #endregion

    #region Initialization

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        await LoadClubsAsync();
        InitMaterialFrame();
    }

    /// <summary>
    /// 동아리 목록 로드
    /// </summary>
    private async Task LoadClubsAsync()
    {
        try
        {
            string teacherId = Settings.User.Value;
            int year = Settings.WorkYear.Value;

            using var repo = new ClubRepository(SchoolDatabase.DbPath);
            var clubs = await repo.GetByTeacherAsync(teacherId, year);

            Clubs.Clear();
            foreach (var club in clubs)
            {
                Clubs.Add(club);
            }

            if (Clubs.Count > 0)
            {
                CBoxClub.SelectedIndex = 0;
            }
            else
            {
                TxtSubtitle.Text = "등록된 동아리가 없습니다. [동아리 관리]에서 추가해주세요.";
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ClubHomePage] 동아리 로드 실패: {ex.Message}");
        }
    }

    /// <summary>
    /// 자료실 Frame 초기화 (PostListPage 내장)
    /// </summary>
    private void InitMaterialFrame()
    {
        MaterialFrame.Navigate(typeof(PostListPage), new PostListPageParameter
        {
            Category = MaterialCategory,
            Subject = _selectedClub?.ClubName ?? string.Empty,
            ViewMode = Board.Models.BoardViewMode.Card,
            AllowCategoryChange = false,
            AllowViewModeChange = true,
            IsEmbedded = true
        });
        _postListPage = MaterialFrame.Content as PostListPage;
    }

    #endregion

    #region Event Handlers

    /// <summary>
    /// 동아리 선택 변경
    /// </summary>
    private async void CBoxClub_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CBoxClub.SelectedItem is Club club)
        {
            _selectedClub = club;
            await LoadClubDataAsync();
        }
    }

    /// <summary>
    /// 동아리 데이터 로드
    /// </summary>
    private async Task LoadClubDataAsync()
    {
        if (_selectedClub == null) return;

        // 동아리 정보 표시
        TxtClubInfo.Text = string.IsNullOrEmpty(_selectedClub.ActivityRoom)
            ? ""
            : $"📍 {_selectedClub.ActivityRoom}";

        // 부원 로드
        await LoadMembersAsync();

        // 자료실 Subject 변경
        await UpdateMaterialSubjectAsync();
    }

    /// <summary>
    /// 자료실 Subject 변경
    /// </summary>
    private async Task UpdateMaterialSubjectAsync()
    {
        if (_postListPage != null && _selectedClub != null)
        {
            await _postListPage.SetSubjectAsync(_selectedClub.ClubName);
        }
    }

    /// <summary>
    /// 부원 목록 로드
    /// </summary>
    private async Task LoadMembersAsync()
    {
        Members.Clear();

        if (_selectedClub == null)
        {
            TxtMemberCount.Text = "";
            EmptyMemberState.Visibility = Visibility.Visible;
            return;
        }

        try
        {
            using var enrollmentRepo = new ClubEnrollmentRepository(SchoolDatabase.DbPath);
            var clubEnrollments = await enrollmentRepo.GetByClubAsync(_selectedClub.No);

            if (clubEnrollments.Count == 0)
            {
                TxtMemberCount.Text = "부원: 0명";
                EmptyMemberState.Visibility = Visibility.Visible;
                return;
            }

            using var enrollmentService = new EnrollmentService();

            foreach (var ce in clubEnrollments)
            {
                var enrollment = await enrollmentService.GetCurrentEnrollmentAsync(ce.StudentID);
                if (enrollment != null)
                {
                    Members.Add(enrollment);
                }
            }

            // 정렬
            var sorted = Members.OrderBy(m => m.Grade).ThenBy(m => m.Class).ThenBy(m => m.Number).ToList();
            Members.Clear();
            foreach (var m in sorted)
            {
                Members.Add(m);
            }

            TxtMemberCount.Text = $"부원: {Members.Count}명";
            EmptyMemberState.Visibility = Members.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ClubHomePage] 부원 로드 실패: {ex.Message}");
        }
    }

    /// <summary>
    /// 활동 기록 버튼 클릭
    /// </summary>
    private void OnActivityClick(object sender, RoutedEventArgs e)
    {
        // ClubActivityPage로 네비게이션
        if (this.Frame != null)
        {
            this.Frame.Navigate(typeof(ClubActivityPage), _selectedClub);
        }
    }

    #endregion
}
