using System;
using System.Diagnostics;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using NewSchool.Board.ViewModels;

namespace NewSchool.Board;

/// <summary>
/// MVVM 패턴을 적용한 게시판 목록 페이지
/// PostListViewModel + 수동 UI 업데이트 방식
/// </summary>
public sealed partial class ListViewer : Page
{
    private string _initialCategory = string.Empty;
    private string _initialSubject = string.Empty;

    public PostListViewModel ViewModel { get; }

    public ListViewer()
    {
        Debug.WriteLine("ListViewer 생성자 시작");
        this.InitializeComponent();
        Board.Init();

        ViewModel = new PostListViewModel();
        
        // PropertyChanged 이벤트로 수동 UI 업데이트
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;

        this.DataContext = ViewModel;

        InitCategory();
        Debug.WriteLine("ViewModel 및 이벤트 설정 완료");
    }

    public ListViewer(string category, string subject = "")
    {
        Debug.WriteLine("ListViewer 생성자 시작 (카테고리 지정)");
        this.InitializeComponent();
        Board.Init();

        ViewModel = new PostListViewModel();
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        this.DataContext = ViewModel;

        if (!string.IsNullOrWhiteSpace(category))
        {
            _initialCategory = category;
            CBoxCategory.Visibility = Visibility.Collapsed;

            if (!string.IsNullOrWhiteSpace(subject))
            {
                _initialSubject = subject;
                CBoxSubject.Visibility = Visibility.Collapsed;
            }
        }

        InitCategory();
    }

    /// <summary>
    /// ViewModel PropertyChanged 이벤트 핸들러 - 수동 UI 업데이트
    /// </summary>
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
                    // ItemsRepeater ItemsSource 직접 설정
                    PostsRepeater.ItemsSource = null;
                    PostsRepeater.ItemsSource = ViewModel.Posts;
                    Debug.WriteLine($"  -> ItemsRepeater ItemsSource 수동 설정: Count={ViewModel.Posts.Count}");
                    break;

                case nameof(ViewModel.HasPosts):
                    ScrollViewerPosts.Visibility = ViewModel.HasPosts;
                    Debug.WriteLine($"  -> ScrollViewer 수동 업데이트: Visibility={ScrollViewerPosts.Visibility}");
                    break;

                case nameof(ViewModel.IsEmpty):
                    EmptyMessage.Visibility = ViewModel.IsEmpty;
                    Debug.WriteLine($"  -> EmptyMessage 수동 업데이트: Visibility={EmptyMessage.Visibility}");
                    break;

                case nameof(ViewModel.PageInfo):
                    TBCount.Text = ViewModel.PageInfo;
                    break;

                case nameof(ViewModel.HasPreviousPage):
                    BtnPagePrevios.Visibility = ViewModel.HasPreviousPage ? Visibility.Visible : Visibility.Collapsed;
                    BtnPageStart.Visibility = ViewModel.HasPreviousPage ? Visibility.Visible : Visibility.Collapsed;
                    break;

                case nameof(ViewModel.HasNextPage):
                    BtnPageNext.Visibility = ViewModel.HasNextPage ? Visibility.Visible : Visibility.Collapsed;
                    BtnPageEnd.Visibility = ViewModel.HasNextPage ? Visibility.Visible : Visibility.Collapsed;
                    break;
            }
        });
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        Debug.WriteLine("OnNavigatedTo 시작");

        // 초기 ItemsSource 설정
        PostsRepeater.ItemsSource = ViewModel.Posts;
        Debug.WriteLine($"초기 ItemsSource 설정: Count={ViewModel.Posts.Count}");

        // Navigate 파라미터로 카테고리 설정
        if (e.Parameter is string category && !string.IsNullOrWhiteSpace(category))
        {
            _initialCategory = category;
            CBoxCategory.Visibility = Visibility.Collapsed;
            CBoxSubject.Visibility = Visibility.Collapsed;
            ViewModel.SelectedCategory = category;
            Debug.WriteLine($"Navigate 파라미터로 카테고리 설정: {category}");
        }
        // 생성자에서 설정된 초기 카테고리
        else if (!string.IsNullOrWhiteSpace(_initialCategory))
        {
            ViewModel.SelectedCategory = _initialCategory;
        }

        await ViewModel.LoadPostsAsync();

        Debug.WriteLine($"LoadPostsAsync 완료");
        Debug.WriteLine($"  Posts.Count: {ViewModel.Posts.Count}");
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);

        // 이벤트 구독 해제 (메모리 누수 방지)
        if (ViewModel != null)
            ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
    }

    #region 카테고리 및 주제 초기화

    private void InitCategory()
    {
        CBoxCategory.Items.Clear();
        CBoxCategory.Items.Add("전체");

        var categories = Board.GetCategories();
        foreach (var cat in categories)
        {
            CBoxCategory.Items.Add(cat);
        }

        if (CBoxCategory.Items.Count == 1)
        {
            CBoxCategory.Items.Add("추가");
        }

        CBoxCategory.SelectedIndex = 0;
    }

    private void CBoxCategory_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CBoxCategory.SelectedIndex < 0) return;

        var category = CBoxCategory.SelectedItem?.ToString() ?? string.Empty;
        ViewModel.SelectedCategory = (category == "전체") ? string.Empty : category;

        InitSubject();
    }

    private void InitSubject()
    {
        CBoxSubject.Items.Clear();
        CBoxSubject.Items.Add("전체");

        var subjects = Board.GetSubjects(ViewModel.SelectedCategory);
        foreach (var subj in subjects)
        {
            CBoxSubject.Items.Add(subj);
        }

        CBoxSubject.SelectedIndex = 0;
    }

    private void CBoxSubject_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CBoxSubject.SelectedIndex < 0) return;

        var subject = CBoxSubject.SelectedItem?.ToString() ?? string.Empty;
        // Subject는 아직 ViewModel에 없으므로 나중에 추가 필요
        // 현재는 LoadPostsAsync 호출
        _ = ViewModel.LoadPostsAsync().ContinueWith(t =>
        {
            if (t.IsFaulted)
                System.Diagnostics.Debug.WriteLine($"[ListViewer] {t.Exception?.InnerException?.Message}");
        }, TaskContinuationOptions.OnlyOnFaulted);
    }

    #endregion

    #region 버튼 이벤트 핸들러

    private void BtnAddPost_Click(object sender, RoutedEventArgs e)
    {
        ShowPost(null);
    }

    private void PostItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is Post post)
        {
            ShowPost(post);
        }
    }

    private void Box_Unloaded(object sender, RoutedEventArgs e)
    {
        _ = ViewModel.LoadPostsAsync().ContinueWith(t =>
        {
            if (t.IsFaulted)
                System.Diagnostics.Debug.WriteLine($"[ListViewer] {t.Exception?.InnerException?.Message}");
        }, TaskContinuationOptions.OnlyOnFaulted);
    }

    private void ShowPost(Post? post)
    {
        GridPost.Children.Clear();
        PostBox box = new(post);
        box.Unloaded += Box_Unloaded;
        GridPost.Children.Add(box);

        RowTop.Height = new GridLength(0, GridUnitType.Pixel);
        RowList.Height = new GridLength(0, GridUnitType.Pixel);
        RowPaging.Height = new GridLength(0, GridUnitType.Pixel);
        RowSearch.Height = new GridLength(0, GridUnitType.Pixel);
        RowContent.Height = new GridLength(1, GridUnitType.Star);

        if (BtnAddPost.Visibility == Visibility.Visible)
            BtnAddPost.Visibility = Visibility.Collapsed;
    }

    #endregion

    #region 페이징 버튼

    private async void BtnPagePrevios_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.PreviousPageAsync();
    }

    private async void BtnPageNext_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.NextPageAsync();
    }

    private async void BtnPageEnd_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.HasNextPage)
        {
            ViewModel.CurrentPage = ViewModel.TotalPages;
            await ViewModel.LoadPostsAsync();
        }
    }

    private async void BtnPageStart_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.HasPreviousPage)
        {
            ViewModel.CurrentPage = 1;
            await ViewModel.LoadPostsAsync();
        }
    }

    #endregion

    #region 검색

    private async void BtnSearch_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(TBoxSearch.Text))
        {
            // 검색어가 없으면 검색 취소
            ViewModel.SearchText = string.Empty;
            TBoxSearch.Text = string.Empty;
            BtnSearch.Content = "검색";
        }
        else
        {
            // 검색 실행
            ViewModel.SearchText = TBoxSearch.Text;
            BtnSearch.Content = "검색 취소";
        }

        await ViewModel.SearchPostsAsync();
    }

    private void CBoxSearch_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CBoxSearch.SelectedIndex < 0) return;

        switch (CBoxSearch.SelectedIndex)
        {
            case 0: // 제목
                ViewModel.SearchInTitle = true;
                ViewModel.SearchInContent = false;
                break;
            case 1: // 내용
                ViewModel.SearchInTitle = false;
                ViewModel.SearchInContent = true;
                break;
            case 2: // 제목+내용
                ViewModel.SearchInTitle = true;
                ViewModel.SearchInContent = true;
                break;
            default:
                ViewModel.SearchInTitle = false;
                ViewModel.SearchInContent = false;
                break;
        }
    }

    #endregion
}
