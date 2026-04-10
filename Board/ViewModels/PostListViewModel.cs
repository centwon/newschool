using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using NewSchool.Board.Services;
using NewSchool.Board.ViewModels;
using NewSchool.Collections;

namespace NewSchool.Board.ViewModels;


/// <summary>
/// Post 목록 ViewModel - MVVM 패턴
/// </summary>
public class PostListViewModel : INotifyPropertyChanged
{
    private readonly BoardService _service;
    private OptimizedObservableCollection<PostItemViewModel> _posts;
    private readonly DispatcherQueue? _dispatcherQueue;
    public event PropertyChangedEventHandler? PropertyChanged;

    #region Properties

    public OptimizedObservableCollection<PostItemViewModel> Posts
    {
        get => _posts;
        set
        {
            _posts = value;
            OnPropertyChanged();
            //OnPropertyChanged(nameof(IsEmpty));
        }
    }

    public string SelectedCategory
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged();
                _ = LoadPostsAsync().ContinueWith(t =>
                {
                    if (t.IsFaulted)
                        System.Diagnostics.Debug.WriteLine($"[PostListViewModel] {t.Exception?.InnerException?.Message}");
                }, TaskContinuationOptions.OnlyOnFaulted); // 자동 새로고침
            }
        }
    } = "";

    public string SelectedSubject
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged();
            }
        }
    } = "";


    public Visibility HasPosts
    {
        get
        {
            var visibility = Posts.Count > 0 && !IsLoading
                ? Visibility.Visible
                : Visibility.Collapsed;
            Debug.WriteLine($"HasPosts 계산됨: Posts.Count={Posts.Count}, IsLoading={IsLoading}, Visibility={visibility}");
            return visibility;
        }
    }

    public Visibility IsEmpty
    {
        get
        {
            var visibility = Posts.Count == 0 && !IsLoading
                ? Visibility.Visible
                : Visibility.Collapsed;
            Debug.WriteLine($"IsEmpty 계산됨: Posts.Count={Posts.Count}, IsLoading={IsLoading}, Visibility={visibility}");
            return visibility;
        }
    }

    public Visibility LoadingVisibility => IsLoading ? Visibility.Visible : Visibility.Collapsed;



    public string SearchText
    {
        get;
        set
        {
            field = value;
            OnPropertyChanged();
        }
    } = "";

    public bool SearchInTitle
    {
        get;
        set
        {
            field = value;
            OnPropertyChanged();
        }
    } = true;

    public bool SearchInContent
    {
        get;
        set
        {
            field = value;
            OnPropertyChanged();
        }
    } = false;

    public bool IsLoading
    {
        get;
        set
        {
            if (field != value)  // 값이 실제로 변경될 때만
            {
                field = value;
                Debug.WriteLine($"[IsLoading Setter] 값 변경: {value}");
                OnPropertyChanged();
                OnPropertyChanged(nameof(LoadingVisibility));
                OnPropertyChanged(nameof(IsEmpty));
                OnPropertyChanged(nameof(HasPosts));
            }
        }
    }

    public int CurrentPage
    {
        get;
        set
        {
            field = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PageInfo));
        }
    } = 1;

    public int PageSize
    {
        get;
        set
        {
            field = value;
            OnPropertyChanged();
        }
    } = 20;

    public int TotalPages
    {
        get;
        set
        {
            field = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PageInfo));
            OnPropertyChanged(nameof(HasPreviousPage));
            OnPropertyChanged(nameof(HasNextPage));
        }
    }

    public int TotalCount
    {
        get;
        set
        {
            field = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PageInfo));
        }
    }

    public string PageInfo => $"{CurrentPage} / {TotalPages} 페이지 (전체 {TotalCount}개)";

    public bool HasPreviousPage => CurrentPage > 1;
    public bool HasNextPage => CurrentPage < TotalPages;



    #endregion

    #region Commands

    public ICommand LoadCommand { get; }
    public ICommand SearchCommand { get; }
    public ICommand PreviousPageCommand { get; }
    public ICommand NextPageCommand { get; }
    public ICommand DeletePostCommand { get; }
    public ICommand RefreshCommand { get; }

    #endregion

    public PostListViewModel()
    {
        // DispatcherQueue 가져오기
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

        _service = Board.CreateService();
        _posts = new OptimizedObservableCollection<PostItemViewModel>();
        // Posts 컬렉션 변경 감지 추가
        _posts.CollectionChanged += (s, e) =>
        {
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(HasPosts));  // 추가
        };
        // Command 초기화
        LoadCommand = new RelayCommand(async () => await LoadPostsAsync());
        SearchCommand = new RelayCommand(async () => await SearchPostsAsync());
        PreviousPageCommand = new RelayCommand(async () => await PreviousPageAsync(), () => HasPreviousPage);
        NextPageCommand = new RelayCommand(async () => await NextPageAsync(), () => HasNextPage);
        DeletePostCommand = new RelayCommand<PostItemViewModel>(async (postItem) => await DeletePostAsync(postItem));
        RefreshCommand = new RelayCommand(async () => await RefreshAsync());
    }



    #region Methods

    public async Task LoadPostsAsync()
    {
        try
        {
            IsLoading = true;
            Debug.WriteLine($"=== Posts 로딩 시작 ===");

            var result = await _service.GetPostsPagedAsync(
                pageNumber: CurrentPage,
                pageSize: PageSize,
                category: SelectedCategory,
                subject: SelectedSubject);

            Debug.WriteLine($"서비스에서 받은 아이템 수: {result.Items.Count}");

            // ConfigureAwait(true)로 UI 컨텍스트로 돌아옴 (기본값이지만 명시)
            var postItems = new List<PostItemViewModel>();
            foreach (var post in result.Items)
            {
                // 댓글 개수 계산
                int commentCount = 0;
                try
                {
                    var comments = await _service.GetCommentsByPostAsync(post.No);
                    commentCount = comments.Count;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"댓글 개수 조회 실패 (Post {post.No}): {ex.Message}");
                }

                var postItem = new PostItemViewModel(post, commentCount);
                postItems.Add(postItem);
                Debug.WriteLine($"추가됨 - No: {post.No}, Title: {post.Title}, Comments: {commentCount}");
            }
            Posts.ReplaceAll(postItems);

            TotalPages = result.TotalPages;
            TotalCount = result.TotalCount;

            Debug.WriteLine($"최종 Posts.Count: {Posts.Count}");

            // 명시적으로 UI 업데이트 알림
            OnPropertyChanged(nameof(Posts));
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(HasPosts));

            Debug.WriteLine($"Post 로드 완료");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Post 로드 실패: {ex.Message}");
            Debug.WriteLine($"StackTrace: {ex.StackTrace}");
        }
        finally
        {
            IsLoading = false;
            Debug.WriteLine("IsLoading = false");

            // 강제로 모든 Visibility 속성 업데이트
            OnPropertyChanged(nameof(IsLoading));
            OnPropertyChanged(nameof(HasPosts));
            OnPropertyChanged(nameof(IsEmpty));

            Debug.WriteLine($"최종 상태: Posts.Count={Posts.Count}, IsLoading={IsLoading}");
        }
    }
    public async Task SearchPostsAsync()
    {
        try
        {
            if (_dispatcherQueue != null)
            {
                _dispatcherQueue.TryEnqueue(() =>
                {
                    IsLoading = true;
                    CurrentPage = 1;
                });
            }

            var result = await _service.GetPostsPagedAsync(
                pageNumber: CurrentPage,
                pageSize: PageSize,
                category: SelectedCategory,
                subject: SelectedSubject,
                searchTitle: SearchInTitle,
                searchContent: SearchInContent,
                searchText: SearchText);

            if (_dispatcherQueue != null)
            {
                _dispatcherQueue.TryEnqueue(async () =>
                {
                    Posts.Clear();
                    foreach (var post in result.Items)
                    {
                        // 댓글 개수 계산
                        int commentCount = 0;
                        try
                        {
                            var comments = await _service.GetCommentsByPostAsync(post.No);
                            commentCount = comments.Count;
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"댓글 개수 조회 실패 (Post {post.No}): {ex.Message}");
                        }

                        var postItem = new PostItemViewModel(post, commentCount);
                        Posts.Add(postItem);
                    }

                    TotalPages = result.TotalPages;
                    TotalCount = result.TotalCount;

                    OnPropertyChanged(nameof(Posts));
                    OnPropertyChanged(nameof(IsEmpty));
                    OnPropertyChanged(nameof(HasPosts));

                    IsLoading = false;
                });
            }

            Debug.WriteLine($"검색 완료: {result.Items.Count}개");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"검색 실패: {ex.Message}");

            if (_dispatcherQueue != null)
            {
                _dispatcherQueue.TryEnqueue(() =>
                {
                    IsLoading = false;
                });
            }
        }
    }



    public async Task PreviousPageAsync()
    {
        if (HasPreviousPage)
        {
            CurrentPage--;
            await LoadPostsAsync();
        }
    }

    public async Task NextPageAsync()
    {
        if (HasNextPage)
        {
            CurrentPage++;
            await LoadPostsAsync();
        }
    }

    public async Task DeletePostAsync(PostItemViewModel? postItem)
    {
        if (postItem == null) return;

        try
        {
            // 삭제 확인은 UI에서 처리되었다고 가정
            bool success = await _service.DeletePostAsync(postItem.No, postItem.Category);

            if (success)
            {
                Posts.Remove(postItem);
                TotalCount--;
                Debug.WriteLine($"Post 삭제 완료: No={postItem.No}");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Post 삭제 실패: {ex.Message}");
        }
    }

    public async Task RefreshAsync()
    {
        CurrentPage = 1;
        await LoadPostsAsync();
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    #endregion
}
#region RelayCommand Implementation

/// <summary>
/// ICommand 구현체 (Native AOT 호환)
/// </summary>
public class RelayCommand : ICommand
{
    private readonly Func<Task> _execute;
    private readonly Func<bool>? _canExecute;

    public event EventHandler? CanExecuteChanged;

    public RelayCommand(Func<Task> execute, Func<bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public bool CanExecute(object? parameter)
    {
        return _canExecute?.Invoke() ?? true;
    }

    public async void Execute(object? parameter)
    {
        await _execute();
    }

    public void RaiseCanExecuteChanged()
    {
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>
/// 제네릭 ICommand 구현체 (Native AOT 호환)
/// </summary>
public class RelayCommand<T> : ICommand
{
    private readonly Func<T?, Task> _execute;
    private readonly Func<T?, bool>? _canExecute;

    public event EventHandler? CanExecuteChanged;

    public RelayCommand(Func<T?, Task> execute, Func<T?, bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public bool CanExecute(object? parameter)
    {
        return _canExecute?.Invoke((T?)parameter) ?? true;
    }

    public async void Execute(object? parameter)
    {
        await _execute((T?)parameter);
    }

    public void RaiseCanExecuteChanged()
    {
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}

#endregion

#region Post Wrapper for UI

/// <summary>
/// UI 바인딩용 Post 래퍼 - 댓글 개수 포함
/// </summary>
[Microsoft.UI.Xaml.Data.Bindable]
public class PostItemViewModel : INotifyPropertyChanged
{
    private readonly Post _post;
    private int _commentCount;

    public event PropertyChangedEventHandler? PropertyChanged;

    public PostItemViewModel(Post post, int commentCount = 0)
    {
        _post = post;
        _commentCount = commentCount;
    }

    // Post의 모든 속성을 전달
    public int No => _post.No;
    public string User => _post.User;
    public DateTime DateTime => _post.DateTime;
    public string Category => _post.Category;
    public string Subject => _post.Subject;
    public string Title => _post.Title;
    public string Content => _post.Content;
    public int RefNo => _post.RefNo;
    public int ReplyOrder => _post.ReplyOrder;
    public int Depth => _post.Depth;
    public int ReadCount => _post.ReadCount;
    public bool HasFile => _post.HasFile;
    public bool HasComment => _post.HasComment;
    public bool IsCompleted => _post.IsCompleted;

    // UI 바인딩용 속성
    public Visibility FileIconVisibility => _post.FileIconVisibility;
    public Visibility CommentIconVisibility => _post.CommentIconVisibility;
    public string DateTimeDisplay => _post.DateTimeDisplay;

    // 댓글 개수
    public int CommentCount
    {
        get => _commentCount;
        set
        {
            if (_commentCount != value)
            {
                _commentCount = value;
                OnPropertyChanged();
            }
        }
    }

    // 원본 Post 객체 접근
    public Post Post => _post;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

#endregion
