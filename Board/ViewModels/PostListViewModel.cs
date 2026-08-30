using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using NewSchool.Board.Models;
using NewSchool.Board.Services;
using NewSchool.Board.ViewModels;
using NewSchool.Collections;
using NewSchool.Models;

namespace NewSchool.Board.ViewModels;


/// <summary>
/// Post 목록 ViewModel - MVVM 패턴
/// </summary>
public class PostListViewModel : NotifyPropertyChangedBase
{
    private readonly BoardService _service;
    private OptimizedObservableCollection<PostItemViewModel> _posts;

    // ── 확정된 검색 조건 ─────────────────────────────────────────────────
    // 검색창(SearchText·SearchInTitle·SearchInContent)은 사용자가 "지금 치고 있는" 값이고,
    // 아래 셋은 검색 버튼을 눌러 "확정된" 값이다. 목록 조회는 항상 아래 셋만 본다.
    // 둘을 가르지 않으면, 검색을 누르지 않고 카테고리만 바꿔도 치다 만 글자로 걸러진다.
    private string _appliedSearchText = "";
    private bool _appliedSearchInTitle = true;
    private bool _appliedSearchInContent;

    #region Properties

    public OptimizedObservableCollection<PostItemViewModel> Posts
    {
        get => _posts;
        set
        {
            _posts = value;
            OnPropertyChanged();
        }
    }

    /// <summary>외부에서 카테고리·주제를 한 번에 설정하고 확정적으로 한 번만 새로고침할 때,
    /// 두 setter 의 자동 새로고침과 중복 로드되지 않도록 억제하는 플래그.
    /// <see cref="SetScopeWithoutReload"/> 참고.</summary>
    private bool _suppressAutoReload;

    public string SelectedCategory
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged();
                if (!_suppressAutoReload) ReloadInBackground();
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
                if (!_suppressAutoReload) ReloadInBackground();
            }
        }
    } = "";

    /// <summary>
    /// 카테고리·주제를 조회 없이 한꺼번에 설정한다. 호출한 쪽이 곧바로 목록을 로드하는 경우에만 쓴다.
    ///
    /// <para>두 값을 그냥 대입하면 setter 가 각각 자동 새로고침을 걸어, 화면 진입 한 번에
    /// 같은 목록 조회가 세 번(카테고리 → 주제 → 호출부의 명시적 로드) 돌았다.</para>
    /// </summary>
    public void SetScopeWithoutReload(string category, string subject)
    {
        _suppressAutoReload = true;
        try
        {
            SelectedCategory = category;
            SelectedSubject = subject;
        }
        finally
        {
            _suppressAutoReload = false;
        }
    }

    // 주제 설정 + 새로고침을 한 번에 하던 SetSubjectAndRefreshAsync 는 지웠다 —
    // 호출부가 없었다. 이중 로드를 막는 장치 자체는 살아 있다(SetScopeWithoutReload 가
    // 쓰고, PostListPage 가 그쪽을 부른다).


    public Visibility HasPosts
    {
        get
        {
            return Posts.Count > 0 && !IsLoading
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
    }

    public Visibility IsEmpty
    {
        get
        {
            return Posts.Count == 0 && !IsLoading
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
    }




    public PostSortOrder SortOrder
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged();
                ReloadInBackground();
            }
        }
    } = PostSortOrder.NewestFirst;

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
            OnPropertyChanged(nameof(HasPreviousPage));
            OnPropertyChanged(nameof(HasNextPage));
        }
    } = 1;

    public int PageSize
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged();
                ReloadInBackground();
            }
        }
    } = 20;

    public int TotalPages
    {
        get;
        set
        {
            field = value;
            OnPropertyChanged();
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
            OnPropertyChanged(nameof(TotalCountText));
        }
    }

    /// <summary>
    /// 페이저 옆에 붙는 전체 건수. "몇 / 몇 페이지"는 페이저의 번호가 직접 보여 주므로 뺐다.
    /// </summary>
    public string TotalCountText => TotalCount > 0 ? $"전체 {TotalCount}개" : string.Empty;

    /// <summary>페이저에 그릴 칸(첫 장 · 생략표 · 현재 둘레 · 끝 장).</summary>
    public IReadOnlyList<PageToken> PageTokens => PageWindow.Build(CurrentPage, TotalPages);

    public bool HasPreviousPage => CurrentPage > 1;
    public bool HasNextPage => CurrentPage < TotalPages;

    /// <summary>
    /// 지금 목록이 검색 결과인지. 검색 버튼으로 확정된 검색어가 있을 때만 참이다
    /// (검색창에 치고만 있는 글자는 세지 않는다).
    /// </summary>
    public bool IsSearchActive => !string.IsNullOrWhiteSpace(_appliedSearchText);



    #endregion

    // ICommand 다섯(Load·Search·PreviousPage·NextPage·Refresh)과 그 구현체 RelayCommand·
    // RelayCommand<T> 는 지웠다(39차). 어느 XAML 도 Command 로 묶지 않았고 코드에서도 부르지
    // 않아, 생성자에서 만들어 두기만 하고 끝나는 값이었다. 화면은 버튼 Click 핸들러에서
    // 아래 메서드들(LoadPostsAsync·SearchPostsAsync·…)을 직접 부른다.

    public PostListViewModel()
    {
        _service = Board.CreateCachedService();
        _posts = new OptimizedObservableCollection<PostItemViewModel>();
        // Posts 컬렉션 변경 감지 추가
        _posts.CollectionChanged += (s, e) =>
        {
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(HasPosts));  // 추가
        };
    }



    #region Methods

    /// <summary>
    /// 목록 조회 — <b>모든 경로가 여기 하나로 모인다.</b>
    ///
    /// <para>예전에는 조회가 둘로 갈라져 있었다. 검색은 <c>SearchPostsAsync</c> 가, 나머지
    /// (페이지 넘김·정렬·필터·새로고침·뒤로가기)는 <c>LoadPostsAsync</c> 가 맡았는데
    /// <b>뒤엣것이 검색어를 아예 넘기지 않았다</b>. 그래서 검색 결과에서 '다음'을 누르면
    /// 전체 목록 2페이지가 나왔다 — 총 페이지 수는 검색 결과 기준이라 버튼은 켜져 있고
    /// 검색창에도 검색어가 그대로 남아 있어서, 증상은 "검색이 안 먹는다"로만 보였다.
    /// 정렬을 바꿔도, 글 하나 열고 뒤로 돌아와도 같은 일이 벌어졌다.</para>
    ///
    /// <para>검색어는 카테고리·주제와 마찬가지로 <b>조건 하나일 뿐</b>이다. 조회는 하나로 두고,
    /// 경로마다 갈리는 것은 "지금 페이지를 유지할지"뿐이다.</para>
    /// </summary>
    /// <param name="resetToFirstPage">
    /// 조건이 바뀌어 지금 페이지 번호가 뜻을 잃는 경우(검색·필터·정렬·페이지 크기) true.
    /// 페이지 이동이나 제자리 새로고침은 false.
    /// </param>
    private async Task LoadAsync(bool resetToFirstPage)
    {
        try
        {
            // ⚠ 조회 인자를 읽기 "전에" 곧바로 바꾼다. 예전에는 이 대입을 DispatcherQueue 로
            //    미뤄 놓고 바로 다음 줄에서 CurrentPage 를 읽었는데, 인자 평가가 먼저라 큐에 넣은
            //    람다는 아직 돌지 않았다 — 3페이지에서 검색하면 검색 결과의 3페이지를 가져오면서
            //    화면에는 "1 / N 페이지"가 찍혔다.
            if (resetToFirstPage) CurrentPage = 1;

            IsLoading = true;
            Debug.WriteLine($"=== Posts 로딩 시작 (page={CurrentPage}, 검색='{_appliedSearchText}') ===");

            var result = await QueryAsync();

            // 글이 지워져 마지막 페이지가 통째로 사라졌을 수 있다. 그대로 두면 빈 목록에
            // '이전'만 켜진 상태가 되므로, 남아 있는 마지막 장으로 한 번 물러나 다시 읽는다.
            // (0건이면 물러날 곳이 없으니 1페이지 그대로 둔다.)
            if (result.TotalPages > 0 && CurrentPage > result.TotalPages)
            {
                Debug.WriteLine($"페이지 초과({CurrentPage} > {result.TotalPages}) — 마지막 장으로 물러남");
                CurrentPage = result.TotalPages;
                result = await QueryAsync();
            }

            Debug.WriteLine($"서비스에서 받은 아이템 수: {result.Items.Count}");

            // 댓글 개수를 한 번의 쿼리로 일괄 조회 (글마다 조회하던 N+1 제거)
            var ids = new List<int>(result.Items.Count);
            foreach (var p in result.Items) ids.Add(p.No);
            Dictionary<int, int> commentCounts;
            try
            {
                commentCounts = await _service.GetCommentCountsAsync(ids);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"댓글 개수 일괄 조회 실패: {ex.Message}");
                commentCounts = new Dictionary<int, int>();
            }

            var postItems = new List<PostItemViewModel>(result.Items.Count);
            foreach (var post in result.Items)
            {
                commentCounts.TryGetValue(post.No, out int commentCount);
                postItems.Add(new PostItemViewModel(post, commentCount));
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

        Task<PagedResult<Post>> QueryAsync() => _service.GetPostsPagedAsync(
            pageNumber: CurrentPage,
            pageSize: PageSize,
            category: SelectedCategory,
            subject: SelectedSubject,
            searchTitle: _appliedSearchInTitle,
            searchContent: _appliedSearchInContent,
            searchText: _appliedSearchText,
            sortOrder: SortOrder);
    }

    /// <summary>지금 조건·지금 페이지 그대로 다시 읽는다 (새로고침·화면 진입·뒤로가기).</summary>
    public Task LoadPostsAsync() => LoadAsync(resetToFirstPage: false);

    /// <summary>조건이 바뀌었으니 1페이지부터 다시 읽는다 (필터 변경·새 글 작성 후).</summary>
    public Task RefreshAsync() => LoadAsync(resetToFirstPage: true);

    /// <summary>
    /// 화면의 검색 조건을 <b>확정</b>하고 1페이지부터 검색한다.
    ///
    /// <para>확정한 값만 <see cref="LoadAsync"/> 가 쓴다 — 검색창의 살아 있는 값을 그때그때
    /// 읽으면, 검색 버튼을 누르지 않고 카테고리만 바꿔도 <b>치다 만 글자</b>로 걸러진다
    /// (검색창의 TwoWay 바인딩은 포커스를 잃을 때 넘어오므로 실제로 그렇게 된다).
    /// 검색 해제는 검색창을 비우고 다시 검색을 누르면 된다.</para>
    /// </summary>
    public Task SearchPostsAsync()
    {
        _appliedSearchText = SearchText ?? "";
        _appliedSearchInTitle = SearchInTitle;
        _appliedSearchInContent = SearchInContent;
        OnPropertyChanged(nameof(IsSearchActive));
        return LoadAsync(resetToFirstPage: true);
    }

    /// <summary>
    /// 지정한 페이지로 이동한다 (검색·필터·정렬은 그대로).
    /// 범위를 벗어난 값은 있는 범위로 끌어당긴다 — 페이저 버튼이 옛 페이지 수로 그려져 있을 수 있다.
    /// </summary>
    public async Task GoToPageAsync(int page)
    {
        int target = Math.Clamp(page, 1, Math.Max(TotalPages, 1));
        if (target == CurrentPage) return;

        CurrentPage = target;
        await LoadAsync(resetToFirstPage: false);
    }

    public Task PreviousPageAsync() => GoToPageAsync(CurrentPage - 1);

    public Task NextPageAsync() => GoToPageAsync(CurrentPage + 1);

    /// <summary>
    /// setter 에서 부르는 조회. 조건이 바뀐 자리이므로 1페이지부터 읽는다
    /// (5페이지를 보다 카테고리를 바꾸면 그 카테고리의 5페이지가 아니라 처음이 맞다).
    /// </summary>
    private void ReloadInBackground()
    {
        _ = RefreshAsync().ContinueWith(t =>
        {
            if (t.IsFaulted)
                Debug.WriteLine($"[PostListViewModel] {t.Exception?.InnerException?.Message}");
        }, TaskContinuationOptions.OnlyOnFaulted);
    }

    #endregion
}
#region Post Wrapper for UI

/// <summary>
/// UI 바인딩용 Post 래퍼 - 댓글 개수 포함
/// </summary>
public class PostItemViewModel : NotifyPropertyChangedBase
{
    private readonly Post _post;
    private int _commentCount;

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
    public string Content => _post.PlainText;   // 미리보기·검색용 평문 (Content 는 .flow BLOB)
    public int RefNo => _post.RefNo;
    public int ReplyOrder => _post.ReplyOrder;
    public int Depth => _post.Depth;
    public int ReadCount => _post.ReadCount;
    public bool HasFile => _post.HasFile;
    public bool HasComment => _post.HasComment;
    public bool IsCompleted => _post.IsCompleted;

    /// <summary>중요 글 여부. 목록에서 이 글이 맨 앞으로 온다.</summary>
    public bool IsPinned
    {
        get => _post.IsPinned;
        set
        {
            if (_post.IsPinned == value) return;
            _post.IsPinned = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PinIconVisibility));
        }
    }

    // UI 바인딩용 속성
    public Visibility FileIconVisibility => _post.FileIconVisibility;
    public Visibility CommentIconVisibility => _post.CommentIconVisibility;
    public Visibility PinIconVisibility => _post.PinIconVisibility;
    public string DateTimeDisplay => _post.DateTimeDisplay;

    /// <summary>
    /// 댓글 개수.
    ///
    /// <para>⚠ <b>지금 어느 화면도 이 값을 보여 주지 않는다</b>(바인딩 0건). 그런데 목록을
    /// 읽을 때마다 <c>GetCommentCountsAsync</c> 로 일괄 조회는 계속 돈다. 즉 값을 구해서
    /// 버리고 있다.</para>
    ///
    /// <para>그럼에도 지우지 않는다 — 그 조회에는 "글마다 조회하던 N+1 제거" 라고 적혀 있어,
    /// 누군가 <b>보여 줄 작정으로 일부러 다듬어 둔</b> 자리다. 목록 항목 서식에 한 줄만
    /// 이으면 살아난다. 정말 안 쓸 것으로 정하면 이 속성과 함께 위의 일괄 조회도 걷어야
    /// 헛조회가 사라진다.</para>
    /// </summary>
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

}

#endregion
