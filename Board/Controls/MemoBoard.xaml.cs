using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using NewSchool.Board.Services;
using NewSchool.Controls;
using NewSchool.Models;
using Windows.UI;

namespace NewSchool.Board.Controls;

/// <summary>
/// 메모 보드: 활성 메모 1개를 펼쳐 인라인 에디터로 편집(첨부 없음), 나머지는 compact 목록(제목 줄).
/// 처음에는 가장 최근 메모를 펼친다. 목록의 줄을 누르면 그 메모를 펼치고 펼쳐 있던 메모는 목록으로
/// 접힌다(예전에는 줄을 누르면 편집 창이 떴다 — 창은 펼친 메모의 [새 창으로 열기] 로 연다).
/// 완료 체크 = 숨김(아카이브에서 조회). 정식 게시물 승격은 게시판에서 별도.
///
/// 구 설계의 단일 에디터 reparent 트릭은 WebView2/Jodit 이 무거워서 쓰던 우회책이었으나,
/// WinUIRichEditor(공유 Win2D 디바이스, 인스턴스당 +0.1~0.5MB)로 전환하며 제거함.
/// ⚠ 다만 <b>첫 인스턴스</b>는 그 공유 장치를 만드느라 약 24MB 다(2026-09-26 실측). 그래서 인라인
/// 편집기는 누를 때 만들고(<see cref="EditorPreview_Click"/>), 그 전에는 본문 글자만 보여 준다.
/// </summary>
public sealed partial class MemoBoard : UserControl, IDisposable
{
    private readonly List<Post> _memos = [];   // 활성(미완료) 메모, 최신순
    private Post? _recentPost;                  // 인라인 에디터에 펼쳐 둔 메모
    private Post? _expanded;                    // 사용자가 펼치라고 고른 메모(없으면 최신)
    private bool _isLoading;
    private bool _isModified;                   // 본문 밖(분류)을 고쳤거나 [저장] 을 눌렀다

    /// <summary>
    /// 편집기가 본문을 들고 있는가. 아니면(아직 누르지 않았거나 싣다가 실패해 미리보기로 돌아갔으면)
    /// 본문은 손대지 않은 것이다 — 편집기 값을 읽어 저장하면 원래 본문을 덮어쓴다.
    /// </summary>
    private bool EditorInUse => Editor != null && EditorPreview.Visibility == Visibility.Collapsed;

    /// <summary>저장할 것이 있는가. 본문은 편집기의 <c>IsModified</c> 가 안다(불러온 직후는 false).</summary>
    private bool HasChanges => _isModified || (EditorInUse && Editor!.IsModified);
    private bool _isInitialized;
    private bool _isUpdating;                   // 모델→UI 반영 중 역방향 이벤트 억제
    private bool _disposed;

    /// <summary>카테고리 필터 표시 여부.</summary>
    public bool ShowFilter { get; set; } = true;

    /// <summary>카테고리 고정 (설정 시 필터 숨기고 해당 카테고리만).</summary>
    public string? FixedCategory { get; set; }

    public MemoBoard()
    {
        InitializeComponent();
        Loaded += MemoBoard_Loaded;
        Unloaded += MemoBoard_Unloaded;
    }

    #region Lifecycle

    private async void MemoBoard_Loaded(object sender, RoutedEventArgs e)
    {
        if (_isInitialized) return;
        _isInitialized = true;

        CBoxCategoryFilter.Visibility = ShowFilter ? Visibility.Visible : Visibility.Collapsed;
        CBoxCategoryFilter.SelectedIndex = 0;

        await LoadMemosAsync();

        // 퀵잡 준비: 활성 메모가 없으면 빈 메모 하나를 미리 열어둠
        if (_memos.Count == 0)
            await CreateNewMemoAsync();
    }

    private async void MemoBoard_Unloaded(object sender, RoutedEventArgs e)
    {
        if (HasChanges) await SaveRecentMemoAsync();
        Dispose();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Editor?.Clear();
        GC.SuppressFinalize(this);
    }

    #endregion

    #region Load / Render

    public async Task LoadMemosAsync()
    {
        if (_isLoading) return;
        _isLoading = true;
        try
        {
            LoadingRing.IsActive = true;

            using var service = Board.CreateService();
            var memos = await service.GetMemosAsync(
                category: GetCategoryFilter(), subject: "메모", includeCompleted: false);

            _memos.Clear();
            _memos.AddRange(memos.OrderByDescending(m => m.DateTime));
            await RenderAsync();
        }
        catch (Exception ex)
        {
            // 메모판이 통째로 비어 보인다 — 메모가 없는 것과 구별되지 않는다.
            NewSchool.Logging.Log.Error("MemoBoard", "메모를 읽지 못했다", ex);
        }
        finally
        {
            LoadingRing.IsActive = false;
            _isLoading = false;
        }
    }

    /// <summary>펼칠 메모를 인라인 에디터에, 나머지를 compact 목록(최신순)에 반영.</summary>
    private async Task RenderAsync()
    {
        _recentPost = ResolveExpanded();
        bool hasAny = _recentPost != null;

        RecentPanel.Visibility = hasAny ? Visibility.Visible : Visibility.Collapsed;
        PanelEmpty.Visibility = hasAny ? Visibility.Collapsed : Visibility.Visible;

        _isUpdating = true;
        if (_recentPost != null)
        {
            ChkRecent.IsChecked = _recentPost.IsCompleted;     // 미완료만 로드되므로 항상 false
            SelectComboBoxByTag(CBoxRecentCategory, _recentPost.Category);
            TxtRecentTitle.Text = _recentPost.Title ?? "";
        }
        string preview = _recentPost?.PlainText?.Trim() ?? "";
        TxtPreview.Text = preview;
        TxtPreviewHint.Visibility = preview.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        _isUpdating = false;
        _isModified = false;

        // 편집기를 한 번 열었으면 그대로 쓴다(장치 비용은 이미 치렀다). 아직이면 미리보기만.
        if (EditorInUse)
        {
            try
            {
                if (_recentPost?.Content is { Length: > 0 } flow)
                {
                    using var ms = new MemoryStream(flow);
                    await Editor!.LoadPackageAsync(ms);
                }
                else Editor!.Clear();
            }
            catch (Exception ex)
            {
                // 편집기에는 앞 메모가 남아 있다 — 미리보기로 돌아가 그것이 이 메모로 저장되지 않게 한다.
                NewSchool.Logging.Log.Error("MemoBoard", "메모 본문을 편집기에 싣지 못했다", ex);
                EditorPreview.Visibility = Visibility.Visible;
            }
        }

        // 나머지 = compact 목록. 컬렉션째 갈아 끼운다 — 비우고 하나씩 넣으면 줄마다 변경 알림이 간다.
        CompactRepeater.ItemsSource = new ObservableCollection<MemoRow>(
            _memos.Where(m => m != _recentPost).Select(m => new MemoRow(m)));
    }

    /// <summary>
    /// 펼칠 메모. 고른 메모가 아직 목록에 있으면 그것 — 다시 읽어 객체가 바뀌었으면 번호로 찾는다
    /// (저장·필터 전환 뒤에도 펼친 자리가 유지되게). 지웠거나 완료했으면 가장 최근 메모.
    /// </summary>
    private Post? ResolveExpanded()
    {
        if (_expanded != null)
        {
            if (_memos.Contains(_expanded)) return _expanded;
            if (_expanded.No > 0 && _memos.FirstOrDefault(m => m.No == _expanded.No) is { } same)
                return _expanded = same;
        }
        return _expanded = _memos.FirstOrDefault();
    }

    public async Task CreateNewMemoAsync()
    {
        try
        {
            string category = GetCategoryFilter();
            if (string.IsNullOrEmpty(category))
                category = !string.IsNullOrEmpty(FixedCategory) ? FixedCategory : CategoryNames.Lesson;

            var post = new Post
            {
                User = Settings.AuthorName,
                DateTime = DateTime.Now,
                Category = category,
                Subject = "메모",
                Title = "",
                Content = []
            };

            // 빈 메모를 즉시 DB 에 저장하지 않는다(No=0, 메모리 보관).
            // 사용자가 실제로 입력해 HasChanges 가 되면 SaveRecentMemoAsync 가 그때 INSERT.
            // → 입력 없이 떠나면 DB 에 빈 메모가 쌓이지 않음.
            _memos.Insert(0, post);
            _expanded = post;   // 새 메모는 펼쳐서 바로 적게
            await RenderAsync();
            Debug.WriteLine($"[MemoBoard] 새 메모 생성(메모리)");
        }
        catch (Exception ex)
        {
            NewSchool.Logging.Log.Error("MemoBoard", "새 메모를 만들지 못했다 — 눌러도 아무 일이 없어 보인다", ex);
        }
    }

    #endregion

    #region Compact list

    /// <summary>줄을 누르면 그 메모를 펼치고, 펼쳐 있던 메모는 목록으로 접는다.</summary>
    private async void CompactItem_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is Post memo)
            await ExpandAsync(memo);
    }

    private async Task ExpandAsync(Post memo)
    {
        if (memo == _recentPost) return;

        // 접기 전에 펼쳐 있던 메모의 고친 내용을 저장한다 — 편집기는 다음 메모 본문으로 바뀐다.
        if (HasChanges) await SaveRecentMemoAsync();
        if (HasChanges) return;   // 저장에 실패했다(이미 알렸다) — 바꾸면 고친 내용이 사라진다

        // 아무것도 안 적은 새 메모(아직 DB 에 없음)는 접을 때 버린다 — 목록에 "(제목 없음)" 으로 남지 않게.
        if (_recentPost is { No: <= 0 } blank) _memos.Remove(blank);

        _expanded = memo;
        await RenderAsync();
    }

    private async void CompactCheck_Click(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { Tag: Post memo })
            await CompleteMemoAsync(memo);
    }

    #endregion

    #region Recent memo handlers

    /// <summary>미리보기를 누르면 그때 편집기를 만들어 본문을 싣고, 캐럿을 끝에 두어 바로 이어 쓰게 한다.</summary>
    private async void EditorPreview_Click(object sender, RoutedEventArgs e)
    {
        if (!EditorInUse)
        {
            if (Editor == null)
                FindName(nameof(Editor));   // x:Load="False" 를 실체화 — 이후 Editor 필드가 채워진다
            try
            {
                if (_recentPost?.Content is { Length: > 0 } flow)
                {
                    using var ms = new MemoryStream(flow);
                    await Editor!.LoadPackageAsync(ms);
                }
                else Editor!.Clear();
            }
            catch (Exception ex)
            {
                // 편집기를 드러내지 않는다 — 빈 편집기에 쓰고 저장하면 원래 본문을 덮어쓴다.
                NewSchool.Logging.Log.Error("MemoBoard", "메모 본문을 편집기에 싣지 못했다", ex);
                return;
            }
            EditorPreview.Visibility = Visibility.Collapsed;
        }

        // 방금 만든 편집기는 아직 트리에 붙기 전일 수 있다 — 그때 부르면 포커스가 가지 않는다.
        var editor = Editor!;
        if (editor.IsLoaded) { editor.FocusDocumentEnd(); return; }
        void OnEditorLoaded(object s, RoutedEventArgs a)
        {
            editor.Loaded -= OnEditorLoaded;
            editor.FocusDocumentEnd();
        }
        editor.Loaded += OnEditorLoaded;
    }

    private void CBoxRecentCategory_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdating) return;
        _isModified = true;
    }

    private async void ChkRecent_Click(object sender, RoutedEventArgs e)
    {
        if (_isUpdating || _recentPost == null) return;
        await CompleteMemoAsync(_recentPost);
    }

    private async void BtnRecentSave_Click(object sender, RoutedEventArgs e)
    {
        _isModified = true;   // 명시적 저장은 항상 반영
        await SaveRecentMemoAsync();
    }

    private async void BtnRecentOpenDialog_Click(object sender, RoutedEventArgs e)
    {
        if (_recentPost == null) return;
        await OpenDialogForAsync(_recentPost);
    }

    private async void BtnRecentDelete_Click(object sender, RoutedEventArgs e)
    {
        if (_recentPost == null || _recentPost.No <= 0) return;

        bool ok = await MessageBox.ShowConfirmAsync(
            "이 메모를 삭제하시겠습니까?", "메모 삭제", "삭제", "취소");
        if (!ok) return;

        try
        {
            var memo = _recentPost;
            // 쓰기는 캐시 서비스로 — 게시판 목록·상세가 지운 메모를 계속 보여 주지 않도록
            using var service = Board.CreateCachedService();
            await service.DeletePostAsync(memo.No, memo.Category);
            _memos.Remove(memo);
            await RenderAsync();
            Debug.WriteLine($"[MemoBoard] 메모 삭제: No={memo.No}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MemoBoard] 메모 삭제 실패: {ex.Message}");
            await MessageBox.ShowAsync($"삭제 중 오류가 발생했습니다.\n{ex.Message}", "오류");
        }
    }

    private async void BtnAddMemo_Click(object sender, RoutedEventArgs e)
    {
        if (HasChanges) await SaveRecentMemoAsync();
        await CreateNewMemoAsync();
    }

    private async void CBoxCategoryFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isInitialized || _isLoading) return;
        if (HasChanges) await SaveRecentMemoAsync();
        await LoadMemosAsync();
    }

    #endregion

    #region Persistence

    /// <summary>인라인 에디터의 최신 메모를 저장 (변경분이 있을 때만).</summary>
    private async Task SaveRecentMemoAsync()
    {
        if (_recentPost == null || !HasChanges) return;
        try
        {
            // 첨부는 이 화면에 없지만, 메모 편집 창에서 붙여 둔 것이 있을 수 있다.
            // 여기 카테고리 콤보로 옮기면 그 첨부의 실물도 따라가야 한다 — 아니면 조용히 끊긴다.
            // 아직 저장 안 된 메모(No<=0)는 옮길 첨부가 없으므로 빈 값으로 넘겨 건너뛴다.
            string oldCategory = _recentPost.No > 0 ? _recentPost.Category : string.Empty;

            _recentPost.Category = GetRecentCategory();

            // ⚠ 편집기를 아직 만들지 않았으면(미리보기 상태) 본문은 손대지 않은 것이다 —
            //   분류만 바꿔 [저장] 하는 경로. 여기서 빈 편집기의 값을 읽으면 본문이 지워진다.
            if (EditorInUse)
            {
                using (var ms = new MemoryStream())
                {
                    await Editor!.SavePackageAsync(ms);
                    _recentPost.Content = ms.ToArray();
                }
                _recentPost.PlainText = Editor.GetPlainText();
            }

            // 제목이 비어있을 때만 본문 첫 줄로 자동 생성 (기존 제목 보존)
            if (string.IsNullOrWhiteSpace(_recentPost.Title))
                _recentPost.Title = ExtractTitle(_recentPost.PlainText);

            // 작성일시는 처음 저장할 때만 찍는다 — 고칠 때마다 밀면 '언제 쓴 메모'인지가 사라진다
            // (게시글 편집·메모 창·수업 일지 창과 같은 규칙).
            if (_recentPost.No <= 0) _recentPost.DateTime = DateTime.Now;
            TxtRecentTitle.Text = _recentPost.Title;

            using var service = Board.CreateCachedService();   // 쓰기 → 캐시 무효화가 함께 돌아야 한다
            int postNo = await service.SavePostAsync(_recentPost);

            await PostAttachments.MoveAllToCategoryAsync(
                service, postNo, oldCategory, _recentPost.Category);

            _isModified = false;
            if (EditorInUse) Editor!.MarkSaved();
            Debug.WriteLine($"[MemoBoard] 저장: No={_recentPost.No}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MemoBoard] 저장 실패: {ex.Message}");
            await UserErrorReporter.ReportAsync("메모 저장", ex);
        }
    }

    /// <summary>메모를 완료(숨김) 처리하고 목록에서 제거.</summary>
    private async Task CompleteMemoAsync(Post memo)
    {
        try
        {
            // 최신 메모를 닫는 경우, 편집 중이던 내용 먼저 저장
            if (memo == _recentPost && HasChanges) await SaveRecentMemoAsync();

            // 아직 저장 안 된 빈 메모(No<=0)는 DB 갱신 없이 목록에서만 제거.
            // DB 갱신을 먼저 확정한 뒤에 IsCompleted/목록을 바꿔야, 저장 실패 시
            // 체크만 켜진 채 DB 는 미완료로 남는 무음 불일치를 막을 수 있다.
            if (memo.No > 0)
            {
                using var service = Board.CreateCachedService();   // 쓰기 → 목록 캐시도 비운다
                if (!await service.UpdatePostIsCompletedAsync(memo.No, true))
                    throw new InvalidOperationException("완료 상태를 저장하지 못했습니다(대상 없음).");
            }

            memo.IsCompleted = true;
            _memos.Remove(memo);
            await RenderAsync();
            Debug.WriteLine($"[MemoBoard] 완료(숨김): No={memo.No}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MemoBoard] 완료 처리 실패: {ex.Message}");
            // 사용자가 클릭한 체크박스가 켜진 채로 남지 않도록 상태·목록 원복 후 알린다.
            memo.IsCompleted = false;
            await RenderAsync();
            await UserErrorReporter.ReportAsync("메모 완료 처리", ex);
        }
    }

    /// <summary>메모를 다이얼로그로 편집 (제목·카테고리·첨부). 저장 시 목록 갱신.</summary>
    private async Task OpenDialogForAsync(Post memo)
    {
        if (HasChanges) await SaveRecentMemoAsync();

        // 사본을 넘긴다 — 창은 저장을 누르면 값을 먼저 고치고 저장을 시도하므로, 목록의 메모를
        // 그대로 주면 저장에 실패한 뒤 취소해도 고친 제목·분류가 이 판에 남아 다음 저장 때 섞여 들어갔다.
        var copy = memo.Clone();
        var dialog = new Dialogs.MemoEditDialog(copy);
        bool saved = await dialog.ShowDialogAsync(App.MainWindow);
        if (saved)
        {
            // 아직 DB 에 없던 메모는 창이 저장하며 번호를 받았다 — 다시 읽은 뒤에도 그 메모를 펼쳐 둔다.
            if (memo == _expanded) _expanded = copy;
            await LoadMemosAsync();
        }
    }

    #endregion

    #region Helpers

    private string GetCategoryFilter()
    {
        if (!string.IsNullOrEmpty(FixedCategory)) return FixedCategory;
        if (CBoxCategoryFilter?.SelectedItem is ComboBoxItem { Tag: string tag }) return tag;
        return "";
    }

    private string GetRecentCategory()
    {
        if (CBoxRecentCategory.SelectedItem is ComboBoxItem { Tag: string tag }) return tag;
        return CategoryNames.Lesson;
    }

    private static void SelectComboBoxByTag(ComboBox comboBox, string? tag)
    {
        if (!string.IsNullOrEmpty(tag))
        {
            foreach (var item in comboBox.Items)
            {
                if (item is ComboBoxItem cbi && cbi.Tag?.ToString() == tag)
                {
                    comboBox.SelectedItem = item;
                    return;
                }
            }
        }
        comboBox.SelectedIndex = 0;
    }

    /// <summary>순수 텍스트 첫 줄을 제목으로 추출 (최대 15자).</summary>
    private static string ExtractTitle(string? plainText)
    {
        if (string.IsNullOrWhiteSpace(plainText)) return "새 메모";

        var firstLine = plainText
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .FirstOrDefault(l => l.Length > 0);

        if (string.IsNullOrWhiteSpace(firstLine)) return "새 메모";
        return firstLine.Length <= 15 ? firstLine : firstLine[..15] + "…";
    }

    /// <summary>
    /// 분류의 <b>바탕색</b>. 배지 테두리에만 이대로 쓰고, 안은 <see cref="SoftenCategoryColor"/>
    /// 로 옅게 깐다.
    ///
    /// <para>예전에는 이 색을 그대로 채우고 <b>흰 글자</b>를 얹었다. 네 가지가 원색에 가까워
    /// (파랑·초록·빨강·노랑) 목록에서 <b>배지가 제목보다 먼저 눈에 들어왔다</b> — 먼저 읽혀야
    /// 할 것은 메모의 제목이다. 일정 목록의 분류 배지(<c>KAgendaControl</c>)도 같이 손봤다.</para>
    /// </summary>
    internal static Color GetCategoryColor(string? category) => category switch
    {
        CategoryNames.Lesson => Color.FromArgb(0xFF, 0x42, 0x85, 0xF4),
        CategoryNames.Homeroom => Color.FromArgb(0xFF, 0x0F, 0x9D, 0x58),
        CategoryNames.Work => Color.FromArgb(0xFF, 0xDB, 0x44, 0x37),
        CategoryNames.Personal => Color.FromArgb(0xFF, 0xF4, 0xB4, 0x00),
        _ => Microsoft.UI.Colors.Gray
    };

    /// <summary>
    /// 같은 색을 <b>옅게</b>(알파) — 밝은 색을 새로 만들지 않는다. 그래야 밝은 테마에서는
    /// 파스텔로, 어두운 테마에서는 어두운 바탕에 은은하게 얹혀 <b>양쪽 다 글자를 삼키지
    /// 않는다</b>(42차-b: 배경만 밝은 색으로 고정하면 다크 테마에서 글자가 사라진다).
    /// </summary>
    internal static SolidColorBrush SoftenCategoryColor(Color color)
        => new(Color.FromArgb(56, color.R, color.G, color.B));

    #endregion
}

/// <summary>
/// 메모판 compact 목록의 한 줄(<c>CompactRepeater</c> 의 x:Bind 대상). 본문 영역 클릭 → 다이얼로그,
/// 체크 → 완료(숨김). 붓은 줄이 화면에 올라올 때만 만든다 — 보이지 않는 줄은 붓도 없다.
/// </summary>
internal sealed class MemoRow(Post memo)
{
    public Post Memo { get; } = memo;

    public string TitleText => string.IsNullOrWhiteSpace(Memo.Title) ? "(제목 없음)" : Memo.Title;

    public string CategoryText => Memo.Category ?? "기타";

    public string DateText => Memo.DateTime.ToString("M/d HH:mm");

    public Visibility FileIconVisibility => Memo.HasFile ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>체크박스의 UIA 이름. 예전 줄은 이름이 없어 낭독기가 "확인란"이라고만 읽었다.</summary>
    public string CheckName => $"완료로 표시해 숨기기: {TitleText}";

    public SolidColorBrush BadgeBackground => MemoBoard.SoftenCategoryColor(MemoBoard.GetCategoryColor(Memo.Category));

    public SolidColorBrush BadgeBorder => new(MemoBoard.GetCategoryColor(Memo.Category));
}
