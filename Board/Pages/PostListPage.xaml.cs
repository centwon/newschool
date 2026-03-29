using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using NewSchool.Board.Services;
using NewSchool.Board.ViewModels;

namespace NewSchool.Board.Pages;

/// <summary>
/// Post 목록 페이지 (개선 버전)
/// </summary>
public sealed partial class PostListPage : Page
{
    public PostListViewModel ViewModel { get; }

    /// <summary>내장 모드 파라미터</summary>
    private PostListPageParameter? _parameter;

    /// <summary>내장 모드 여부</summary>
    public bool IsEmbedded => _parameter?.IsEmbedded ?? false;

    /// <summary>현재 ViewMode</summary>
    private Models.BoardViewMode _currentViewMode = Models.BoardViewMode.Table;

    /// <summary>
    /// Subject 변경 및 다시 로드 (외부에서 호출)
    /// </summary>
    public async Task SetSubjectAsync(string subject)
    {
        ViewModel.SelectedSubject = subject;
        if (_parameter != null)
        {
            _parameter.Subject = subject;
        }
        await ViewModel.RefreshAsync();
    }

    /// <summary>
    /// 목록 새로고침 (외부에서 호출)
    /// </summary>
    public async Task RefreshAsync()
    {
        await ViewModel.RefreshAsync();
    }

    public PostListPage()
    {
        Debug.WriteLine("PostListPage 생성자 시작");
        this.InitializeComponent();

        // 페이지 캐싱 — 뒤로 돌아올 때 상태 유지
        this.NavigationCacheMode = NavigationCacheMode.Enabled;

        ViewModel = new PostListViewModel();

        // PropertyChanged 이벤트로 수동 UI 업데이트
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;

        this.DataContext = ViewModel;

        Debug.WriteLine("ViewModel 및 이벤트 설정 완료");
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        Debug.WriteLine($"[PropertyChanged] {e.PropertyName}");

        // UI 스레드에서 수동으로 업데이트
        DispatcherQueue.TryEnqueue(() =>
        {
            switch (e.PropertyName)
            {
                case nameof(ViewModel.IsLoading):
                    LoadingRing.IsActive = ViewModel.IsLoading;
                    LoadingRing.Visibility = ViewModel.IsLoading ? Visibility.Visible : Visibility.Collapsed;
                    Debug.WriteLine($"  -> ProgressRing 수동 업데이트: IsActive={LoadingRing.IsActive}");
                    break;

                case nameof(ViewModel.Posts):
                    // ViewMode에 따라 적절한 Repeater에 ItemsSource 설정
                    switch (_currentViewMode)
                    {
                        case Models.BoardViewMode.Table:
                            PostsRepeater.ItemsSource = null;
                            PostsRepeater.ItemsSource = ViewModel.Posts;
                            break;
                        case Models.BoardViewMode.Card:
                            CardViewRepeater.ItemsSource = null;
                            CardViewRepeater.ItemsSource = ViewModel.Posts;
                            break;
                        case Models.BoardViewMode.Gallery:
                            GalleryViewRepeater.ItemsSource = null;
                            GalleryViewRepeater.ItemsSource = ViewModel.Posts;
                            break;
                    }
                    Debug.WriteLine($"  -> ItemsRepeater ItemsSource 수동 설정: Count={ViewModel.Posts.Count}, ViewMode={_currentViewMode}");
                    break;

                case nameof(ViewModel.HasPosts):
                    PostsRepeater.Visibility = ViewModel.HasPosts;
                    Debug.WriteLine($"  -> ItemsRepeater 수동 업데이트: Visibility={PostsRepeater.Visibility}");
                    break;

                case nameof(ViewModel.IsEmpty):
                    EmptyMessage.Visibility = ViewModel.IsEmpty;
                    Debug.WriteLine($"  -> EmptyMessage 수동 업데이트: Visibility={EmptyMessage.Visibility}");
                    break;

                case nameof(ViewModel.PageInfo):
                    PageInfoText.Text = ViewModel.PageInfo;
                    Debug.WriteLine($"  -> PageInfo 수동 업데이트: {ViewModel.PageInfo}");
                    break;

                case nameof(ViewModel.HasPreviousPage):
                    PreviousButton.IsEnabled = ViewModel.HasPreviousPage;
                    break;

                case nameof(ViewModel.HasNextPage):
                    NextButton.IsEnabled = ViewModel.HasNextPage;
                    break;
            }
        });
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        Debug.WriteLine($"OnNavigatedTo 시작 - NavigationMode={e.NavigationMode}");

        // Back 네비게이션: 상태 유지, 데이터만 새로고침
        if (e.NavigationMode == NavigationMode.Back)
        {
            Debug.WriteLine("Back 네비게이션 - 데이터만 새로고침");
            await ViewModel.RefreshAsync();
            return;
        }

        // 새 네비게이션 (New/Forward): 초기 설정
        if (e.Parameter is PostListPageParameter param)
        {
            _parameter = param;
            ApplyEmbeddedMode();
        }

        // 초기 ItemsSource 설정
        PostsRepeater.ItemsSource = ViewModel.Posts;
        Debug.WriteLine($"초기 ItemsSource 설정: Count={ViewModel.Posts.Count}");

        // 초기 카테고리 설정 (카테고리 변경 가능한 경우에만)
        if (!IsEmbedded && (_parameter == null || _parameter.AllowCategoryChange))
        {
            InitializeCategories();
        }

        await ViewModel.LoadPostsAsync();

        Debug.WriteLine($"LoadPostsAsync 완료");
        Debug.WriteLine($"  Posts.Count: {ViewModel.Posts.Count}");
    }

    /// <summary>
    /// 내장 모드 적용
    /// </summary>
    private void ApplyEmbeddedMode()
    {
        if (_parameter == null) return;

        // ViewModel에 카테고리/Subject 설정
        ViewModel.SelectedCategory = _parameter.Category;
        ViewModel.SelectedSubject = _parameter.Subject;

        // ViewMode 처리
        _currentViewMode = DetermineViewMode();
        ApplyViewMode(_currentViewMode);

        // 카테고리 ComboBox 표시 여부
        CategoryComboBox.Visibility = _parameter.AllowCategoryChange
            ? Visibility.Visible
            : Visibility.Collapsed;

        // Subject 필터 표시 여부
        if (_parameter.ShowSubjectFilter)
        {
            SubjectFilterComboBox.Visibility = Visibility.Visible;
            InitializeSubjectFilter();
        }

        if (_parameter.IsEmbedded)
        {
            // 제목 숨김
            TitleText.Visibility = Visibility.Collapsed;
            TitleRow.Height = new GridLength(0);

            // ViewMode 전환 버튼 표시 여부
            if (_parameter.AllowViewModeChange)
            {
                ViewModeToggleButton.Visibility = Visibility.Visible;
            }
            else
            {
                ViewModeToggleButton.Visibility = Visibility.Collapsed;
            }

            // Padding 줄임
            RootGrid.Padding = new Thickness(0);
            //FilterPanel.Margin = new Thickness(0, 0, 0, 4);
        }
        else
        {
            // 제목 설정
            if (!string.IsNullOrEmpty(_parameter.Title))
            {
                TitleText.Text = _parameter.Title;
            }

            // ViewMode 전환 버튼 항상 표시
            ViewModeToggleButton.Visibility = Visibility.Visible;
        }
    }

    /// <summary>
    /// ViewMode 결정 (파라미터 기반)
    /// </summary>
    private Models.BoardViewMode DetermineViewMode()
    {
        if (_parameter == null) return Models.BoardViewMode.Table;

        // 파라미터로 명시적으로 지정된 경우
        if (_parameter.ViewMode != Models.BoardViewMode.Default)
        {
            return _parameter.ViewMode;
        }

        // 기본값은 Table
        return Models.BoardViewMode.Table;
    }

    /// <summary>
    /// ViewMode 적용
    /// </summary>
    private void ApplyViewMode(Models.BoardViewMode mode)
    {
        _currentViewMode = mode;

        switch (mode)
        {
            case Models.BoardViewMode.Table:
                TableViewContainer.Visibility = Visibility.Visible;
                CardViewContainer.Visibility = Visibility.Collapsed;
                GalleryViewContainer.Visibility = Visibility.Collapsed;
                UpdateViewModeIcon("\uE8F2"); // List icon
                break;

            case Models.BoardViewMode.Card:
                TableViewContainer.Visibility = Visibility.Collapsed;
                CardViewContainer.Visibility = Visibility.Visible;
                GalleryViewContainer.Visibility = Visibility.Collapsed;
                CardViewRepeater.ItemsSource = ViewModel.Posts;
                UpdateViewModeIcon("\uF0E2"); // DockLeft icon
                break;

            case Models.BoardViewMode.Gallery:
                TableViewContainer.Visibility = Visibility.Collapsed;
                CardViewContainer.Visibility = Visibility.Collapsed;
                GalleryViewContainer.Visibility = Visibility.Visible;
                GalleryViewRepeater.ItemsSource = ViewModel.Posts;
                UpdateViewModeIcon("\uE158"); // View icon
                break;

            default:
                ApplyViewMode(Models.BoardViewMode.Table);
                break;
        }
    }

    /// <summary>
    /// ViewMode 아이콘 업데이트
    /// </summary>
    private void UpdateViewModeIcon(string glyph)
    {
        if (ViewModeIcon != null)
        {
            ViewModeIcon.Glyph = glyph;
        }
    }

    /// <summary>
    /// ViewMode 전환 버튼 클릭
    /// </summary>
    private void ViewModeToggleButton_Click(object sender, RoutedEventArgs e)
    {
        // 순환: Table → Card → Gallery → Table
        var nextMode = _currentViewMode switch
        {
            Models.BoardViewMode.Table => Models.BoardViewMode.Card,
            Models.BoardViewMode.Card => Models.BoardViewMode.Gallery,
            Models.BoardViewMode.Gallery => Models.BoardViewMode.Table,
            _ => Models.BoardViewMode.Table
        };

        ApplyViewMode(nextMode);
    }

    /// <summary>
    /// 카테고리 초기화
    /// </summary>
    private async void InitializeCategories()
    {
        CategoryComboBox.Items.Clear();
        CategoryComboBox.Items.Add("전체");

        try
        {
            using var service = Board.CreateService();
            var categories = await service.GetCategoriesAsync();
            foreach (var cat in categories)
            {
                CategoryComboBox.Items.Add(cat);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"카테고리 로드 실패: {ex.Message}");
        }

        CategoryComboBox.SelectedIndex = 0;
    }

    /// <summary>
    /// Subject 필터 초기화
    /// </summary>
    // 카테고리별 기본 제안 토픽
    private static readonly Dictionary<string, List<string>> _defaultTopics = new()
    {
        ["학급"] = new() { "통계", "학급 자료", "학생 자료", "학급 안내" },
        ["수업"] = new() { "통계", "수업 자료", "과제" },
        ["동아리"] = new() { "통계", "동아리 자료", "활동 안내" },
    };

    private async void InitializeSubjectFilter()
    {
        SubjectFilterComboBox.Items.Clear();
        SubjectFilterComboBox.Items.Add("전체");

        var addedSubjects = new HashSet<string>();

        try
        {
            var category = _parameter?.Category ?? "";

            // 기본 제안 토픽 먼저 추가
            if (_defaultTopics.TryGetValue(category, out var defaults))
            {
                foreach (var topic in defaults)
                {
                    SubjectFilterComboBox.Items.Add(topic);
                    addedSubjects.Add(topic);
                }
            }

            // DB에서 기존 주제 로드 (중복 제거)
            using var service = Board.CreateService();
            var subjects = await service.GetSubjectsAsync(category);
            foreach (var subject in subjects)
            {
                if (!string.IsNullOrEmpty(subject) && !addedSubjects.Contains(subject))
                {
                    SubjectFilterComboBox.Items.Add(subject);
                    addedSubjects.Add(subject);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Subject 필터 로드 실패: {ex.Message}");
        }

        // 현재 Subject가 있으면 선택, 없으면 "전체"
        if (!string.IsNullOrEmpty(_parameter?.Subject))
        {
            var idx = SubjectFilterComboBox.Items.IndexOf(_parameter.Subject);
            SubjectFilterComboBox.SelectedIndex = idx >= 0 ? idx : 0;
        }
        else
        {
            SubjectFilterComboBox.SelectedIndex = 0;
        }
    }

    /// <summary>
    /// Subject 필터 변경 이벤트
    /// </summary>
    private async void SubjectFilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SubjectFilterComboBox.SelectedIndex < 0) return;

        var subject = SubjectFilterComboBox.SelectedItem?.ToString() ?? string.Empty;
        ViewModel.SelectedSubject = (subject == "전체") ? string.Empty : subject;

        if (_parameter != null)
        {
            _parameter.Subject = ViewModel.SelectedSubject;
        }

        await ViewModel.LoadPostsAsync();
    }

    /// <summary>
    /// 카테고리 변경 이벤트
    /// </summary>
    private async void CategoryComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CategoryComboBox.SelectedIndex < 0) return;

        var category = CategoryComboBox.SelectedItem?.ToString() ?? string.Empty;
        ViewModel.SelectedCategory = (category == "전체") ? string.Empty : category;

        // 카테고리 선택 시 주제 필터 갱신 및 표시
        if (!string.IsNullOrEmpty(ViewModel.SelectedCategory))
        {
            if (_parameter == null)
                _parameter = new PostListPageParameter { Category = ViewModel.SelectedCategory };
            else
                _parameter.Category = ViewModel.SelectedCategory;

            SubjectFilterComboBox.Visibility = Visibility.Visible;
            InitializeSubjectFilter();
        }
        else
        {
            // "전체" 선택 시 주제 필터 숨김
            SubjectFilterComboBox.Visibility = Visibility.Collapsed;
            ViewModel.SelectedSubject = string.Empty;
        }

        await ViewModel.LoadPostsAsync();
    }

    /// <summary>
    /// Post 클릭 이벤트
    /// </summary>
    private void PostItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is ViewModels.PostItemViewModel postItem)
        {
            // 게시판 컨텍스트(카테고리 고정 등)를 함께 전달
            Frame.Navigate(typeof(PostDetailPage), new PostDetailPageParameter
            {
                PostNo = postItem.No,
                BoardParameter = _parameter
            });
        }
    }

    /// <summary>
    /// 새 글 작성 버튼
    /// </summary>
    private void NewPostButton_Click(object sender, RoutedEventArgs e)
    {
        if (_parameter != null)
        {
            // 파라미터가 있으면 카테고리/Subject 전달
            Frame.Navigate(typeof(PostEditPage), new PostEditPageParameter
            {
                DefaultCategory = _parameter.Category,
                DefaultSubject = _parameter.Subject,
                AllowCategoryChange = _parameter.AllowCategoryChange
            });
        }
        else
        {
            Frame.Navigate(typeof(PostEditPage));
        }
    }

    /// <summary>
    /// 검색 버튼
    /// </summary>
    private async void SearchButton_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.SearchPostsAsync();
    }

    /// <summary>
    /// 새로고침 버튼
    /// </summary>
    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.RefreshAsync();
    }

    /// <summary>
    /// 이전 페이지 버튼
    /// </summary>
    private async void PreviousButton_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.PreviousPageAsync();
    }

    /// <summary>
    /// 다음 페이지 버튼
    /// </summary>
    private async void NextButton_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.NextPageAsync();
    }
}
