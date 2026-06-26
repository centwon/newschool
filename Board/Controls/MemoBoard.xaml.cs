using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using NewSchool.Board.Services;
using NewSchool.Controls;
using NewSchool.Models;

namespace NewSchool.Board.Controls;

/// <summary>
/// 메모 보드: 가장 최근 활성 메모 1개를 고정 인라인 에디터로 편집(첨부 없음),
/// 나머지 활성 메모는 compact 목록으로 표시(클릭 시 다이얼로그 편집).
/// 완료 체크 = 숨김(아카이브에서 조회). 정식 게시물 승격은 게시판에서 별도.
///
/// 구 설계의 단일 에디터 reparent 트릭은 WebView2/Jodit 이 무거워서 쓰던 우회책이었으나,
/// WinUIRichEditor(공유 Win2D 디바이스, 인스턴스당 +0.1~0.5MB)로 전환하며 제거함.
/// </summary>
public sealed partial class MemoBoard : UserControl, IDisposable
{
    private readonly List<Post> _memos = [];   // 활성(미완료) 메모, 최신순
    private Post? _recentPost;                  // 인라인 에디터에 표시 중인 최신 메모
    private bool _isLoading;
    private bool _isModified;
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
        Editor.PropertyChanged += Editor_PropertyChanged;
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
        if (_isModified) await SaveRecentMemoAsync();
        Dispose();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Editor.PropertyChanged -= Editor_PropertyChanged;
        Editor.Dispose();
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
            Render();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MemoBoard] 메모 로드 실패: {ex.Message}");
        }
        finally
        {
            LoadingRing.IsActive = false;
            _isLoading = false;
        }
    }

    /// <summary>최신 메모를 인라인 에디터에, 나머지를 compact 목록에 반영.</summary>
    private void Render()
    {
        _recentPost = _memos.FirstOrDefault();
        bool hasAny = _recentPost != null;

        RecentPanel.Visibility = hasAny ? Visibility.Visible : Visibility.Collapsed;
        PanelEmpty.Visibility = hasAny ? Visibility.Collapsed : Visibility.Visible;

        _isUpdating = true;
        if (_recentPost != null)
        {
            ChkRecent.IsChecked = _recentPost.IsCompleted;     // 미완료만 로드되므로 항상 false
            SelectComboBoxByTag(CBoxRecentCategory, _recentPost.Category);
            TxtRecentTitle.Text = _recentPost.Title ?? "";
            Editor.LoadFlow(_recentPost.Content);
        }
        else
        {
            Editor.LoadFlow(null);
        }
        _isUpdating = false;
        _isModified = false;

        // 나머지 = compact 목록
        CompactPanel.Children.Clear();
        foreach (var memo in _memos.Skip(1))
            CompactPanel.Children.Add(BuildCompactItem(memo));
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
                User = Settings.UserName ?? Settings.User.Value,
                DateTime = DateTime.Now,
                Category = category,
                Subject = "메모",
                Title = "",
                Content = []
            };

            using var service = Board.CreateService();
            post.No = await service.SavePostAsync(post);

            _memos.Insert(0, post);
            Render();
            Debug.WriteLine($"[MemoBoard] 새 메모 생성: No={post.No}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MemoBoard] 새 메모 생성 실패: {ex.Message}");
        }
    }

    #endregion

    #region Compact list

    /// <summary>나머지 활성 메모 한 줄. 본문 영역 클릭 → 다이얼로그, 체크 → 완료(숨김).</summary>
    private Grid BuildCompactItem(Post memo)
    {
        var grid = new Grid
        {
            Padding = new Thickness(12, 8, 12, 8),
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0, 0, 0, 1),
            BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
            MinHeight = 40,
            ColumnSpacing = 8,
            Tag = memo
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Tapped += CompactItem_Tapped;

        // 완료 체크 (탭은 체크박스가 소비 → 행 Tapped 미발생)
        var chk = new CheckBox { MinWidth = 0, VerticalAlignment = VerticalAlignment.Center, Tag = memo };
        chk.Click += CompactCheck_Click;
        Grid.SetColumn(chk, 0);
        grid.Children.Add(chk);

        // 카테고리 배지
        var badge = new Border
        {
            Background = GetCategoryColor(memo.Category),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(6, 2, 6, 2),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = memo.Category ?? "기타",
                FontSize = 10,
                Foreground = new SolidColorBrush(Microsoft.UI.Colors.White)
            }
        };
        Grid.SetColumn(badge, 1);
        grid.Children.Add(badge);

        // 제목 (+ 첨부 아이콘)
        var titlePanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        titlePanel.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(memo.Title) ? "(제목 없음)" : memo.Title,
            FontSize = 13,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"]
        });
        if (memo.HasFile)
        {
            titlePanel.Children.Add(new FontIcon
            {
                Glyph = "",
                FontSize = 10,
                Margin = new Thickness(6, 0, 0, 0),
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
            });
        }
        Grid.SetColumn(titlePanel, 2);
        grid.Children.Add(titlePanel);

        // 날짜
        var date = new TextBlock
        {
            Text = memo.DateTime.ToString("M/d HH:mm"),
            FontSize = 11,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(date, 3);
        grid.Children.Add(date);

        return grid;
    }

    private async void CompactItem_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is Post memo)
            await OpenDialogForAsync(memo);
    }

    private async void CompactCheck_Click(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { Tag: Post memo })
            await CompleteMemoAsync(memo);
    }

    #endregion

    #region Recent memo handlers

    private void Editor_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_isUpdating) return;
        if (e.PropertyName == nameof(RichTextEditor.Text))
            _isModified = true;
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
            using var service = Board.CreateService();
            await service.DeletePostAsync(memo.No, memo.Category);
            _memos.Remove(memo);
            Render();
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
        if (_isModified) await SaveRecentMemoAsync();
        await CreateNewMemoAsync();
    }

    private async void CBoxCategoryFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isInitialized || _isLoading) return;
        if (_isModified) await SaveRecentMemoAsync();
        await LoadMemosAsync();
    }

    #endregion

    #region Persistence

    /// <summary>인라인 에디터의 최신 메모를 저장 (변경분이 있을 때만).</summary>
    private async Task SaveRecentMemoAsync()
    {
        if (_recentPost == null || !_isModified) return;
        try
        {
            _recentPost.Category = GetRecentCategory();
            _recentPost.Content = Editor.GetFlowBytes();
            _recentPost.PlainText = Editor.PlainText;

            // 제목이 비어있을 때만 본문 첫 줄로 자동 생성 (기존 제목 보존)
            if (string.IsNullOrWhiteSpace(_recentPost.Title))
                _recentPost.Title = ExtractTitle(Editor.PlainText);

            _recentPost.DateTime = DateTime.Now;
            TxtRecentTitle.Text = _recentPost.Title;

            using var service = Board.CreateService();
            await service.SavePostAsync(_recentPost);
            _isModified = false;
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
            if (memo == _recentPost && _isModified) await SaveRecentMemoAsync();

            memo.IsCompleted = true;
            using var service = Board.CreateService();
            await service.UpdatePostIsCompletedAsync(memo.No, true);

            _memos.Remove(memo);
            Render();
            Debug.WriteLine($"[MemoBoard] 완료(숨김): No={memo.No}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MemoBoard] 완료 처리 실패: {ex.Message}");
        }
    }

    /// <summary>메모를 다이얼로그로 편집 (제목·카테고리·첨부). 저장 시 목록 갱신.</summary>
    private async Task OpenDialogForAsync(Post memo)
    {
        if (_isModified) await SaveRecentMemoAsync();

        var dialog = new Dialogs.MemoEditDialog(memo) { XamlRoot = XamlRoot };
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
            await LoadMemosAsync();
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

    private static SolidColorBrush GetCategoryColor(string? category)
    {
        var color = category switch
        {
            CategoryNames.Lesson => Color.FromArgb(0xFF, 0x42, 0x85, 0xF4),
            CategoryNames.Homeroom => Color.FromArgb(0xFF, 0x0F, 0x9D, 0x58),
            CategoryNames.Work => Color.FromArgb(0xFF, 0xDB, 0x44, 0x37),
            CategoryNames.Personal => Color.FromArgb(0xFF, 0xF4, 0xB4, 0x00),
            _ => Microsoft.UI.Colors.Gray
        };
        return new SolidColorBrush(color);
    }

    #endregion
}
