using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using NewSchool.Board.Models;
using NewSchool.Board.Services;
using NewSchool.Board.ViewModels;
using NewSchool.Controls;

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

    // SetSubjectAsync 는 호출부가 없어 지웠다(39차) —
    // 주제는 화면에 들어올 때 넘기는 매개변수로만 정해진다.

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
        this.DataContext = ViewModel;

        Debug.WriteLine("ViewModel 및 이벤트 설정 완료");
    }

    // Ctrl+F → 검색창으로 포커스 + 텍스트 전체 선택
    private void OnCtrlFInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        SearchTextBox.Focus(FocusState.Keyboard);
        SearchTextBox.SelectAll();
        args.Handled = true;
    }

    // Ctrl+PageUp / Ctrl+PageDown → 페이지 이동 (Ctrl 없이는 목록 안 스크롤을 가로챈다)
    private async void OnPreviousPageInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        await ViewModel.PreviousPageAsync();
    }

    private async void OnNextPageInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        await ViewModel.NextPageAsync();
    }

    /// <summary>
    /// 페이저의 번호 칸을 다시 그린다 — <c>1 … 3 4 [5] 6 7 … 42</c>.
    ///
    /// <para>버튼을 XAML 템플릿이 아니라 <b>코드로 짓는 이유</b>: <c>ItemsRepeater</c> 의
    /// <c>DataTemplate</c> 안에서 <c>x:Bind</c> 는 Mode 를 빼면 OneTime 이라, 페이지를 옮겨도
    /// "지금 페이지" 강조가 제자리에서 갱신되지 않는다. 칸이 많아야 아홉이라 통째로 다시 짓는
    /// 편이 싸고 확실하다(<c>MemoBoard.BuildCompactItem</c> 과 같은 방식).</para>
    /// </summary>
    private void RenderPager()
    {
        // 한 장뿐이면 넘길 곳이 없다 — 전체 건수만 남기고 페이저는 숨긴다.
        PagerPanel.Visibility = ViewModel.TotalPages > 1 ? Visibility.Visible : Visibility.Collapsed;

        PageNumberPanel.Children.Clear();
        if (ViewModel.TotalPages <= 1) return;

        foreach (var token in ViewModel.PageTokens)
        {
            if (token.IsEllipsis)
            {
                PageNumberPanel.Children.Add(new TextBlock
                {
                    Text = "…",
                    FontSize = 13,
                    Margin = new Thickness(4, 0, 4, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorTertiaryBrush"]
                });
                continue;
            }

            bool isCurrent = token.Number == ViewModel.CurrentPage;
            var button = new Button
            {
                Content = token.Number.ToString(),
                Tag = token.Number,
                MinWidth = 36,
                Padding = new Thickness(8, 4, 8, 4),
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center
            };

            if (isCurrent)
            {
                // 지금 페이지는 강조만 하고 그대로 둔다 — 눌러도 GoToPageAsync 가 같은 페이지라
                // 조회 없이 돌아오므로, 비활성으로 흐려 보이게 할 필요가 없다.
                //
                // 강조를 굵기와 강조색 둘로 준다. 강조색 스타일을 못 찾더라도(테마 사전이
                // 바뀌면 조회에 실패할 수 있다) 굵기만으로 지금 페이지를 알 수 있어야 한다.
                button.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
                if (Application.Current.Resources.TryGetValue("AccentButtonStyle", out var accent)
                    && accent is Style accentStyle)
                {
                    button.Style = accentStyle;
                }
            }

            button.Click += PageNumberButton_Click;
            PageNumberPanel.Children.Add(button);
        }
    }

    private async void PageNumberButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: int page })
            await ViewModel.GoToPageAsync(page);
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

                case nameof(ViewModel.TotalCountText):
                    PageInfoText.Text = ViewModel.TotalCountText;
                    break;

                case nameof(ViewModel.CurrentPage):
                case nameof(ViewModel.TotalPages):
                    RenderPager();
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

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        Debug.WriteLine($"OnNavigatedTo 시작 - NavigationMode={e.NavigationMode}");

        // NavigationCacheMode=Enabled 시 중복 구독 방지: 항상 재구독
        ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;

        // Back 네비게이션: 상태 유지, 데이터만 새로고침.
        // 보던 페이지와 검색 조건을 그대로 둔다 — 예전에는 여기서 1페이지로 되돌리고 검색까지
        // 풀어 버려서, 검색 결과 3페이지에서 글 하나 열고 돌아오면 전체 목록 1페이지가 나왔다.
        if (e.NavigationMode == NavigationMode.Back)
        {
            Debug.WriteLine("Back 네비게이션 - 데이터만 새로고침");
            await ViewModel.LoadPostsAsync();
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

        // ViewModel에 카테고리/Subject 설정 — 호출부가 곧바로 LoadPostsAsync 를 부르므로
        // setter 의 자동 새로고침은 억제한다(진입 한 번에 조회 3회 → 1회).
        ViewModel.SetScopeWithoutReload(_parameter.Category, _parameter.Subject);

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
            // ViewMode 전환 버튼 표시 여부
            ViewModeToggleButton.Visibility = _parameter.AllowViewModeChange
                ? Visibility.Visible
                : Visibility.Collapsed;

            // 품은 페이지가 이미 여백을 주므로 겹쳐 주지 않는다
            RootGrid.Padding = new Thickness(0);
        }
        else
        {
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
            using var service = Board.CreateCachedService();
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
            using var service = Board.CreateCachedService();
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

        // 조건이 바뀌었으니 1페이지부터 — 5페이지를 보다 주제를 바꾸면 그 주제의 5페이지가
        // 아니라 처음이 맞다(글이 적은 주제면 빈 화면이 됐다).
        await ViewModel.RefreshAsync();
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

        await ViewModel.RefreshAsync();   // 조건이 바뀌었으니 1페이지부터
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
    private async void NewPostButton_Click(object sender, RoutedEventArgs e)
    {
        if (_parameter == null)
        {
            Frame.Navigate(typeof(PostEditPage));
            return;
        }

        // 수업 일지는 전용 창에서 머리 정보·본문·첨부를 한 번에 받는다.
        // 여기서 편집기 페이지로 넘어가지 않으므로 목록만 새로 읽으면 된다.
        if (_parameter.UseLessonJournalTemplate)
        {
            if (await NewSchool.Dialogs.LessonJournalComposer.ComposeAsync())
                await ViewModel.RefreshAsync();
            return;
        }

        Frame.Navigate(typeof(PostEditPage), new PostEditPageParameter
        {
            DefaultCategory = _parameter.Category,
            DefaultSubject = _parameter.Subject,
            AllowCategoryChange = _parameter.AllowCategoryChange
        });
    }

    /// <summary>
    /// 검색 버튼
    /// </summary>
    private async void SearchButton_Click(object sender, RoutedEventArgs e)
    {
        await RunSearchAsync();
    }

    /// <summary>
    /// 검색 실행. 두 진입점(버튼·Enter)이 함께 쓴다.
    ///
    /// <para>[제목]·[내용] 을 <b>둘 다 끄면</b> 검색 조건이 SQL 에 아예 붙지 않아
    /// (<c>PostRepository.GetPostsAsync</c> 의 검색 분기가 통째로 건너뛴다) 검색어를 넣어도
    /// 전체 목록이 그대로 나온다. 오류도 안 나고 안내도 없어서 "검색이 안 된다"로만 보였다.
    /// 기본값이 [제목]만 켜진 상태라 제목을 끄는 순간 이 상태가 된다.</para>
    /// </summary>
    private async Task RunSearchAsync()
    {
        if (!ViewModel.SearchInTitle && !ViewModel.SearchInContent &&
            !string.IsNullOrWhiteSpace(ViewModel.SearchText))
        {
            await MessageBox.ShowAsync(
                "[제목] 과 [내용] 이 모두 꺼져 있어 검색할 범위가 없습니다.\n" +
                "둘 중 하나 이상을 선택한 뒤 다시 검색해 주세요.",
                "검색 범위 없음");
            return;
        }

        await ViewModel.SearchPostsAsync();
    }

    /// <summary>
    /// 검색어 입력 중 Enter 키로 검색 실행
    /// </summary>
    private async void SearchTextBox_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            e.Handled = true;
            await RunSearchAsync();
        }
    }

    /// <summary>
    /// 새로고침 버튼 — 보던 페이지와 검색 조건을 그대로 두고 다시 읽는다.
    /// (3페이지를 보다 F5 를 눌렀는데 1페이지로 튀면 새로고침이 아니라 초기화다.)
    /// </summary>
    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.LoadPostsAsync();
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

    /// <summary>
    /// 정렬 기준 변경
    /// </summary>
    private void SortOrderComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // InitializeComponent()가 ViewModel 생성보다 먼저 실행되어, XAML의 SelectedIndex 초기값이
        // ViewModel이 null인 상태에서 이 핸들러를 한 번 호출한다 — 그 최초 호출은 무시한다.
        if (ViewModel == null) return;

        ViewModel.SortOrder = SortOrderComboBox.SelectedIndex switch
        {
            1 => PostSortOrder.OldestFirst,
            2 => PostSortOrder.TitleAsc,
            3 => PostSortOrder.ReadCountDesc,
            4 => PostSortOrder.UserAsc,
            _ => PostSortOrder.NewestFirst
        };
    }

    /// <summary>
    /// 페이지당 표시 개수 변경
    /// </summary>
    private void PageSizeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // SortOrderComboBox_SelectionChanged와 동일한 이유로 초기 호출 시 ViewModel이 null일 수 있음
        if (ViewModel == null) return;

        ViewModel.PageSize = PageSizeComboBox.SelectedIndex switch
        {
            0 => 10,
            2 => 50,
            3 => 100,
            _ => 20
        };
    }
}
