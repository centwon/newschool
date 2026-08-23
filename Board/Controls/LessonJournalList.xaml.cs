using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NewSchool.Dialogs;

namespace NewSchool.Board.Controls;

/// <summary>
/// 최근 수업 일지 카드. 수업 일지는 게시판 글이므로 <c>board.db</c> 에서
/// 카테고리 <c>수업</c> · 주제 <c>수업일지</c> 의 최근 글을 읽어 온다.
///
/// 화면 이동은 직접 하지 않고 <see cref="PostSelected"/> · <see cref="AddRequested"/> 로
/// 올려 보낸다 — 이 컨트롤은 대시보드 카드 안에 얹히므로, 편집기를 열 프레임은
/// 카드가 아니라 그것을 품은 페이지가 알고 있다.
/// </summary>
public sealed partial class LessonJournalList : UserControl
{
    /// <summary>목록에 담을 최근 글 수</summary>
    private const int PageSize = 20;

    private readonly List<Post> _posts = [];

    /// <summary>글을 골랐다. 인자는 <c>Post.No</c>.</summary>
    public event EventHandler<int>? PostSelected;

    /// <summary>새 수업 일지 요청(+ 버튼)</summary>
    public event EventHandler? AddRequested;

    public LessonJournalList()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 최근 수업 일지를 다시 읽어 온다.
    /// </summary>
    public async Task LoadAsync()
    {
        LoadingRing.IsActive = true;
        LoadingRing.Visibility = Visibility.Visible;

        try
        {
            using var service = Board.CreateCachedService();
            var page = await service.GetPostsPagedAsync(
                pageNumber: 1,
                pageSize: PageSize,
                category: LessonJournalComposer.Category,
                subject: LessonJournalComposer.Subject);

            _posts.Clear();
            _posts.AddRange(page.Items);

            LvJournals.ItemsSource = null;
            LvJournals.ItemsSource = _posts;

            TxtCount.Text = page.TotalCount > 0 ? $"{page.TotalCount}건" : string.Empty;
            TxtEmpty.Visibility = _posts.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LessonJournalList] 목록 로드 실패: {ex.Message}");
            TxtEmpty.Text = "수업 일지를 불러올 수 없습니다.";
            TxtEmpty.Visibility = Visibility.Visible;
        }
        finally
        {
            LoadingRing.IsActive = false;
            LoadingRing.Visibility = Visibility.Collapsed;
        }
    }

    private void LvJournals_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is Post post)
            PostSelected?.Invoke(this, post.No);
    }

    private void BtnAdd_Click(object sender, RoutedEventArgs e)
        => AddRequested?.Invoke(this, EventArgs.Empty);

    private async void BtnRefresh_Click(object sender, RoutedEventArgs e)
        => await LoadAsync();
}

/// <summary>
/// x:Bind 함수 바인딩용 헬퍼. 값 변환기를 따로 두지 않으려고 static 함수로 둔다.
/// </summary>
public static class LessonJournalListHelpers
{
    /// <summary>요약에 쓸 최대 글자 수 — 카드 한 줄에 들어가는 정도</summary>
    private const int SummaryMaxLength = 60;

    /// <summary>글을 쓴 날 (예: "8/21")</summary>
    public static string ShortDate(DateTime dateTime) => $"{dateTime.Month}/{dateTime.Day}";

    /// <summary>
    /// 본문 첫 줄. 수업 일지는 머리 정보 다이얼로그가 첫 줄에 단원을 심어 주므로
    /// 대개 "1-1-1 덧셈과 뺄셈의 혼합 계산 (p.8~11)" 이 걸린다.
    /// </summary>
    public static string Summary(string? plainText)
    {
        var line = plainText?
            .Split('\n')
            .Select(l => l.Trim())
            .FirstOrDefault(l => l.Length > 0);

        if (string.IsNullOrEmpty(line)) return string.Empty;

        return line.Length <= SummaryMaxLength ? line : line[..SummaryMaxLength];
    }

    public static Visibility SummaryVisibility(string? plainText)
        => Summary(plainText).Length > 0 ? Visibility.Visible : Visibility.Collapsed;
}
