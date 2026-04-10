using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Storage;
using Windows.UI;
using NewSchool.Board.Services;
using NewSchool.Controls;
using NewSchool.Models;

namespace NewSchool.Board.Controls;

/// <summary>
/// 메모 보드 컨트롤
/// 선택된 메모: 자기 위치에서 인라인 확장 (헤더 + JoditEditor + 첨부파일)
/// 나머지: 1줄 compact 요약
/// JoditEditor 인스턴스는 1개만 유지하고, 확장 아이템에 reparent하여 재사용
/// </summary>
public sealed partial class MemoBoard : UserControl
{
    #region Fields

    private Post? _selectedPost;
    private readonly List<Post> _memos = [];
    private bool _isLoading;
    private bool _isModified;
    private bool _isInitialized;
    private bool _isUpdating;

    // 확장 아이템 내 컨트롤 참조 (동적 생성)
    private CheckBox? _currentCheckBox;
    private ComboBox? _currentCBoxCategory;
    private TextBlock? _currentTxtTitle;

    // JoditEditor 단일 인스턴스 (코드에서 생성, reparent하여 재사용)
    private JoditEditor? _memoEditor;
    private Border? _editorPlaceholder;

    // 파일리스트 (메모 전환 시 교체)
    private PostFileListBox? _currentFileList;
    private Border? _fileListContainer;

    #endregion

    #region Properties

    /// <summary>현재 선택된 Post</summary>
    public Post? SelectedPost
    {
        get => _selectedPost;
        private set
        {
            if (_selectedPost != value)
            {
                // 이전 메모 저장 확인
                if (_isModified && _selectedPost != null)
                {
                    SaveCurrentMemo();
                }

                // 에디터를 현재 부모에서 분리
                DetachEditor();

                var previousPost = _selectedPost;
                _selectedPost = value;

                // 증분 업데이트: 이전/새 선택 아이템만 교체 (전체 RebuildList 방지)
                if (previousPost != null || value != null)
                {
                    UpdateSelectionInPlace(previousPost, value);
                }
            }
        }
    }

    /// <summary>필터 패널 표시 여부</summary>
    public bool ShowFilter { get; set; } = true;

    /// <summary>카테고리 고정 (설정 시 필터 무시, 해당 카테고리만 표시/생성)</summary>
    public string? FixedCategory { get; set; }

    #endregion

    #region Constructor

    public MemoBoard()
    {
        InitializeComponent();
        Loaded += MemoBoard_Loaded;
        Unloaded += MemoBoard_Unloaded;
    }

    private string GetSelectedCategoryFilter()
    {
        // FixedCategory가 설정되면 항상 해당 카테고리 반환
        if (!string.IsNullOrEmpty(FixedCategory))
            return FixedCategory;

        if (CBoxCategoryFilter?.SelectedItem is ComboBoxItem item && item.Tag is string tag)
        {
            return tag;
        }
        return "";
    }

    #endregion

    #region Lifecycle

    private async void MemoBoard_Loaded(object sender, RoutedEventArgs e)
    {
        if (_isInitialized) return;
        _isInitialized = true;

        if (CBoxCategoryFilter != null)
        {
            CBoxCategoryFilter.Visibility = ShowFilter ? Visibility.Visible : Visibility.Collapsed;
            CBoxCategoryFilter.SelectedIndex = 0;
        }

        // JoditEditor 단일 인스턴스 생성 (한 번만)
        _memoEditor = new JoditEditor
        {
            Mode = JoditEditor.EditorMode.Simple,
            MinHeight = 120
        };
        _memoEditor.PropertyChanged += MemoEditor_PropertyChanged;

        await LoadMemosAsync();

        if (_memos.Count > 0)
        {
            SelectedPost = _memos[0];
        }
        else
        {
            await CreateNewMemoAsync();
        }
    }

    private void MemoBoard_Unloaded(object sender, RoutedEventArgs e)
    {
        DetachEditor();
        DetachFileList();

        if (_memoEditor != null)
        {
            _memoEditor.PropertyChanged -= MemoEditor_PropertyChanged;
            _memoEditor.Dispose();
            _memoEditor = null;
        }
    }

    /// <summary>
    /// JoditEditor를 현재 부모에서 분리 (다음 확장 아이템에 삽입 준비)
    /// </summary>
    private void DetachEditor()
    {
        if (_memoEditor == null) return;

        // placeholder에서 분리
        if (_editorPlaceholder?.Child == _memoEditor)
        {
            _editorPlaceholder.Child = null;
        }
        _editorPlaceholder = null;

        // VisualTreeHelper로 다른 부모에 남아있는 경우도 처리
        try
        {
            var parent = VisualTreeHelper.GetParent(_memoEditor);
            if (parent is Border parentBorder)
            {
                parentBorder.Child = null;
            }
        }
        catch { /* parent 조회 실패 시 무시 */ }
    }

    private void DetachFileList()
    {
        if (_currentFileList != null)
        {
            _currentFileList.FileBoxesChanged -= CurrentFileList_Changed;
            _currentFileList = null;
        }
        if (_fileListContainer != null)
        {
            _fileListContainer.Child = null;
            _fileListContainer = null;
        }
    }

    #endregion

    #region Data Loading

    public async Task LoadMemosAsync()
    {
        if (_isLoading) return;
        _isLoading = true;

        try
        {
            LoadingRing.IsActive = true;
            PanelEmpty.Visibility = Visibility.Collapsed;

            string categoryFilter = GetSelectedCategoryFilter();

            using var service = Board.CreateService();
            var memos = await service.GetMemosAsync(
                category: categoryFilter,
                subject: "메모",
                includeCompleted: false);

            _memos.Clear();
            foreach (var memo in memos.OrderByDescending(m => m.DateTime))
            {
                _memos.Add(memo);
            }

            RebuildList();

            PanelEmpty.Visibility = _memos.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;
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

    public async Task CreateNewMemoAsync()
    {
        try
        {
            string newCategory = GetSelectedCategoryFilter();
            if (string.IsNullOrEmpty(newCategory))
            {
                newCategory = !string.IsNullOrEmpty(FixedCategory) ? FixedCategory : CategoryNames.Lesson;
            }

            var post = new Post
            {
                User = Settings.UserName ?? Settings.User.Value,
                DateTime = DateTime.Now,
                Category = newCategory,
                Subject = "메모",
                Title = "",
                Content = ""
            };

            using var service = Board.CreateService();
            post.No = await service.SavePostAsync(post);

            _memos.Insert(0, post);
            SelectedPost = post;

            PanelEmpty.Visibility = Visibility.Collapsed;

            Debug.WriteLine($"[MemoBoard] 새 메모 생성: No={post.No}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MemoBoard] 새 메모 생성 실패: {ex.Message}");
        }
    }

    #endregion

    #region UI Building

    /// <summary>
    /// 메모 목록 다시 빌드: 선택된 메모는 인라인 확장, 나머지는 compact
    /// </summary>
    private void RebuildList()
    {
        // 에디터를 먼저 분리 (MemoPanel.Children.Clear() 전에!)
        DetachEditor();
        DetachFileList();
        _currentCheckBox = null;
        _currentCBoxCategory = null;
        _currentTxtTitle = null;

        MemoPanel.Children.Clear();

        foreach (var memo in _memos)
        {
            if (memo == _selectedPost)
            {
                var expandedItem = CreateExpandedItem(memo);
                MemoPanel.Children.Add(expandedItem);
            }
            else
            {
                var compactItem = CreateCompactItem(memo);
                MemoPanel.Children.Add(compactItem);
            }
        }
    }

    /// <summary>
    /// 증분 업데이트: 이전 선택 → compact, 새 선택 → expanded (전체 재구성 방지)
    /// </summary>
    private void UpdateSelectionInPlace(Post? previousPost, Post? newPost)
    {
        DetachFileList();
        _currentCheckBox = null;
        _currentCBoxCategory = null;
        _currentTxtTitle = null;

        // 이전 선택 아이템을 compact으로 교체
        if (previousPost != null)
        {
            int prevIndex = _memos.IndexOf(previousPost);
            if (prevIndex >= 0 && prevIndex < MemoPanel.Children.Count)
            {
                MemoPanel.Children[prevIndex] = CreateCompactItem(previousPost);
            }
        }

        // 새 선택 아이템을 expanded로 교체
        if (newPost != null)
        {
            int newIndex = _memos.IndexOf(newPost);
            if (newIndex >= 0 && newIndex < MemoPanel.Children.Count)
            {
                MemoPanel.Children[newIndex] = CreateExpandedItem(newPost);
            }
        }
    }

    /// <summary>
    /// 선택된 메모의 인라인 확장 아이템 생성
    /// JoditEditor 단일 인스턴스를 placeholder에 삽입하여 재사용
    /// </summary>
    private Border CreateExpandedItem(Post memo)
    {
        _isUpdating = true;

        var border = new Border
        {
            Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
            BorderBrush = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"],
            BorderThickness = new Thickness(0,1,0,1),
            //CornerRadius = new CornerRadius(6),
            Margin = new Thickness(0, 0, 0, 4),
            Padding = new Thickness(0)
        };

        var mainGrid = new Grid();
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 헤더
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 에디터
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 파일리스트

        // ===== 헤더 =====
        var header = new Grid
        {
            Padding = new Thickness(12, 6, 12, 6),
            Background = (Brush)Application.Current.Resources["SubtleFillColorSecondaryBrush"]
        };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // 체크박스
        _currentCheckBox = new CheckBox
        {
            IsChecked = memo.IsCompleted,
            VerticalAlignment = VerticalAlignment.Center
        };
        _currentCheckBox.Click += ExpandedCheckBox_Click;
        Grid.SetColumn(_currentCheckBox, 0);
        header.Children.Add(_currentCheckBox);

        // 카테고리 콤보박스
        _currentCBoxCategory = new ComboBox
        {
            MinWidth = 80,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };
        _currentCBoxCategory.Items.Add(new ComboBoxItem { Content = CategoryNames.Lesson, Tag = CategoryNames.Lesson });
        _currentCBoxCategory.Items.Add(new ComboBoxItem { Content = CategoryNames.Homeroom, Tag = CategoryNames.Homeroom });
        _currentCBoxCategory.Items.Add(new ComboBoxItem { Content = CategoryNames.Work, Tag = CategoryNames.Work });
        _currentCBoxCategory.Items.Add(new ComboBoxItem { Content = CategoryNames.Personal, Tag = CategoryNames.Personal });
        SelectComboBoxByTag(_currentCBoxCategory, memo.Category);
        _currentCBoxCategory.SelectionChanged += CBoxCategory_SelectionChanged;
        Grid.SetColumn(_currentCBoxCategory, 1);
        header.Children.Add(_currentCBoxCategory);

        // 제목 (자동 생성, 읽기 전용)
        _currentTxtTitle = new TextBlock
        {
            Text = memo.Title ?? "",
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 13,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
        };
        Grid.SetColumn(_currentTxtTitle, 2);
        header.Children.Add(_currentTxtTitle);

        // 창으로 열기 버튼
        var btnOpenDialog = new Button
        {
            Margin = new Thickness(8, 0, 4, 0),
            Padding = new Thickness(8, 4, 8, 4)
        };
        ToolTipService.SetToolTip(btnOpenDialog, "새 창으로 열기");
        btnOpenDialog.Content = new FontIcon { Glyph = "\uE8A7", FontSize = 12 };
        btnOpenDialog.Click += BtnOpenDialog_Click;
        Grid.SetColumn(btnOpenDialog, 3);
        header.Children.Add(btnOpenDialog);

        // 저장 버튼
        var btnSave = new Button
        {
            Margin = new Thickness(4, 0, 0, 0),
            Padding = new Thickness(8, 4, 8, 4)
        };
        var savePanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        savePanel.Children.Add(new FontIcon { Glyph = "\uE74E", FontSize = 12 });
        savePanel.Children.Add(new TextBlock { Text = "저장", FontSize = 12 });
        btnSave.Content = savePanel;
        btnSave.Click += BtnSave_Click;
        Grid.SetColumn(btnSave, 4);
        header.Children.Add(btnSave);

        // 삭제 버튼
        var btnDelete = new Button
        {
            Margin = new Thickness(4, 0, 0, 0),
            Padding = new Thickness(8, 4, 8, 4),
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            Content = new FontIcon { Glyph = "\uE74D", FontSize = 12 }
        };
        btnDelete.Click += BtnDelete_Click;
        Grid.SetColumn(btnDelete, 5);
        header.Children.Add(btnDelete);

        Grid.SetRow(header, 0);
        mainGrid.Children.Add(header);

        // ===== 에디터 (_memoEditor를 placeholder에 삽입하여 재사용) =====
        _editorPlaceholder = new Border
        {
            Padding = new Thickness(12, 8, 12, 8)
        };

        if (_memoEditor != null)
        {
            // 혹시 아직 다른 부모에 붙어있으면 안전하게 분리
            try
            {
                var parent = VisualTreeHelper.GetParent(_memoEditor);
                if (parent is Border parentBorder && parentBorder != _editorPlaceholder)
                {
                    parentBorder.Child = null;
                }
            }
            catch { /* parent 조회 실패 시 무시 */ }

            // placeholder에 삽입
            _editorPlaceholder.Child = _memoEditor;

            // 에디터 내용 교체 후 강제 리프레시
            _memoEditor.Text = memo.Content ?? "";
            _memoEditor.Visibility = Visibility.Visible;
        }

        Grid.SetRow(_editorPlaceholder, 1);
        mainGrid.Children.Add(_editorPlaceholder);

        // ===== 파일리스트 =====
        _currentFileList = new PostFileListBox
        {
            MinHeight = 10,
            AllowDrop = true,
            Post = memo,
            Category = memo.Category
        };
        _currentFileList.FileBoxesChanged += CurrentFileList_Changed;

        _fileListContainer = new Border
        {
            Padding = new Thickness(12, 0, 12, 11),
            Child = _currentFileList,
            Visibility = Visibility.Visible
        };
        Grid.SetRow(_fileListContainer, 2);
        mainGrid.Children.Add(_fileListContainer);

        // 파일 로드
        if (memo.No > 0)
        {
            _ = LoadFilesAsync(memo).ContinueWith(t =>
            {
                if (t.IsFaulted)
                    System.Diagnostics.Debug.WriteLine($"[MemoBoard] {t.Exception?.InnerException?.Message}");
            }, TaskContinuationOptions.OnlyOnFaulted);
        }

        border.Child = mainGrid;

        _isUpdating = false;
        _isModified = false;

        return border;
    }

    private Button CreateCompactItem(Post memo)
    {
        var btn = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(0),
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0, 0, 0, 1),
            BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
            CornerRadius = new CornerRadius(0),
            MinHeight = 40,
            Tag = memo
        };

        btn.Resources["ButtonBackgroundPointerOver"] = new SolidColorBrush(Color.FromArgb(0x10, 0, 0, 0));
        btn.Resources["ButtonBackgroundPressed"] = new SolidColorBrush(Color.FromArgb(0x20, 0, 0, 0));
        btn.Click += CompactItem_Click;

        var grid = new Grid { Padding = new Thickness(12, 8, 12, 8) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });

        // 체크박스
        var chk = new CheckBox
        {
            IsChecked = memo.IsCompleted,
            VerticalAlignment = VerticalAlignment.Center,
            Tag = memo
        };
        chk.Click += ListCheckBox_Click;
        Grid.SetColumn(chk, 0);
        grid.Children.Add(chk);

        // 카테고리 배지
        var badge = new Border
        {
            Background = GetCategoryColor(memo.Category),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(6, 2, 6, 2),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = new TextBlock
            {
                Text = memo.Category ?? "기타",
                FontSize = 10,
                Foreground = new SolidColorBrush(Microsoft.UI.Colors.White)
            }
        };
        Grid.SetColumn(badge, 1);
        grid.Children.Add(badge);

        // 제목
        var titlePanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };
        titlePanel.Children.Add(new TextBlock
        {
            Text = memo.Title ?? "(제목 없음)",
            FontSize = 13,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextDecorations = memo.IsCompleted ? Windows.UI.Text.TextDecorations.Strikethrough : Windows.UI.Text.TextDecorations.None,
            Foreground = memo.IsCompleted
                ? (Brush)Application.Current.Resources["TextFillColorDisabledBrush"]
                : (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"]
        });

        if (memo.HasFile)
        {
            titlePanel.Children.Add(new FontIcon
            {
                Glyph = "\uE723",
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
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        Grid.SetColumn(date, 4);
        grid.Children.Add(date);

        btn.Content = grid;
        return btn;
    }

    private async Task LoadFilesAsync(Post memo)
    {
        if (memo.No <= 0 || _currentFileList == null) return;

        try
        {
            using var service = Board.CreateService();
            var files = await service.GetPostFilesByPostAsync(memo.No);
            _currentFileList.SetFiles(files);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MemoBoard] 파일 로드 실패: {ex.Message}");
        }
    }

    #endregion

    #region Helpers

    private static SolidColorBrush GetCategoryColor(string? category)
    {
        var color = category switch
        {
            CategoryNames.Lesson => Windows.UI.Color.FromArgb(0xFF, 0x42, 0x85, 0xF4),  // #4285F4 파란색
            CategoryNames.Homeroom => Windows.UI.Color.FromArgb(0xFF, 0x0F, 0x9D, 0x58),  // #0F9D58 초록색
            CategoryNames.Work => Windows.UI.Color.FromArgb(0xFF, 0xDB, 0x44, 0x37),  // #DB4437 빨간색
            CategoryNames.Personal => Windows.UI.Color.FromArgb(0xFF, 0xF4, 0xB4, 0x00),  // #F4B400 노란색
            _ => Microsoft.UI.Colors.Gray
        };
        return new SolidColorBrush(color);
    }

    private static void SelectComboBoxByTag(ComboBox comboBox, string? tag)
    {
        if (string.IsNullOrEmpty(tag))
        {
            comboBox.SelectedIndex = 0;
            return;
        }

        foreach (var item in comboBox.Items)
        {
            if (item is ComboBoxItem cbi && cbi.Tag?.ToString() == tag)
            {
                comboBox.SelectedItem = item;
                return;
            }
        }
        comboBox.SelectedIndex = 0;
    }

    private string GetSelectedCategory()
    {
        if (_currentCBoxCategory?.SelectedItem is ComboBoxItem item && item.Tag is string tag)
        {
            return tag;
        }
        return CategoryNames.Lesson;
    }

    /// <summary>
    /// HTML 태그를 제거하고 내용의 첫 줄을 제목으로 추출 (최대 15자)
    /// 웹페이지 붙여넣기 시 style/script/meta 등 비가시 요소도 제거
    /// </summary>
    /// <summary>
    /// 순수 텍스트에서 첫 줄을 제목으로 추출 (최대 15자)
    /// JoditEditor.PlainText (editor.text)를 사용하여 HTML 파싱 불필요
    /// </summary>
    private static string ExtractTitleFromPlainText(string? plainText)
    {
        if (string.IsNullOrWhiteSpace(plainText))
            return "새 메모";

        // 첫 번째 비어있지 않은 줄 추출
        var firstLine = plainText
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .FirstOrDefault(l => l.Length > 0);

        if (string.IsNullOrWhiteSpace(firstLine))
            return "새 메모";

        return firstLine.Length <= 15 ? firstLine : firstLine[..15] + "…";
    }

    private async void SaveCurrentMemo()
    {
        if (_selectedPost == null || !_isModified) return;

        try
        {
            _selectedPost.Category = GetSelectedCategory();
            _selectedPost.Content = _memoEditor?.Text ?? _selectedPost.Content;

            // 제목이 비어있을 때만 자동 생성 (기존 제목 보존)
            if (string.IsNullOrWhiteSpace(_selectedPost.Title))
                _selectedPost.Title = ExtractTitleFromPlainText(_memoEditor?.PlainText);

            _selectedPost.DateTime = DateTime.Now;

            if (_currentTxtTitle != null)
                _currentTxtTitle.Text = _selectedPost.Title;

            using var service = Board.CreateService();
            await service.SavePostAsync(_selectedPost);

            _isModified = false;
            Debug.WriteLine($"[MemoBoard] 자동 저장: No={_selectedPost.No}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MemoBoard] 자동 저장 실패: {ex.Message}");
        }
    }

    #endregion

    #region Event Handlers - Compact Item

    private void CompactItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is Post post)
        {
            SelectedPost = post;
        }
    }

    private async void ListCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox chk || chk.Tag is not Post post) return;

        post.IsCompleted = chk.IsChecked == true;

        try
        {
            using var service = Board.CreateService();
            await service.UpdatePostIsCompletedAsync(post.No, post.IsCompleted);

            // 완료된 메모는 목록에서 제거
            if (post.IsCompleted)
            {
                _memos.Remove(post);
                RebuildList();
                PanelEmpty.Visibility = _memos.Count == 0
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MemoBoard] 완료 상태 변경 실패: {ex.Message}");
        }
    }

    #endregion

    #region Event Handlers - Expanded Item

    private async void ExpandedCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedPost == null || _isUpdating) return;

        _selectedPost.IsCompleted = _currentCheckBox?.IsChecked == true;

        try
        {
            using var service = Board.CreateService();
            await service.UpdatePostIsCompletedAsync(_selectedPost.No, _selectedPost.IsCompleted);

            // 완료된 메모는 목록에서 제거
            if (_selectedPost.IsCompleted)
            {
                var completed = _selectedPost;
                DetachEditor();
                _memos.Remove(completed);
                _selectedPost = null;

                if (_memos.Count > 0)
                {
                    SelectedPost = _memos[0];
                }
                else
                {
                    RebuildList();
                    PanelEmpty.Visibility = Visibility.Visible;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MemoBoard] 완료 상태 저장 실패: {ex.Message}");
        }
    }

    private void CBoxCategory_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdating) return;
        _isModified = true;
    }

    private void MemoEditor_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_isUpdating) return;
        if (e.PropertyName == nameof(JoditEditor.Text))
        {
            _isModified = true;
        }
    }

    private void CurrentFileList_Changed(object? sender, EventArgs e)
    {
        if (_isUpdating || _selectedPost == null) return;

        if (_currentFileList != null)
        {
            _selectedPost.HasFile = _currentFileList.FileCount > 0;
        }
    }

    private async void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedPost == null) return;

        try
        {
            _selectedPost.Category = GetSelectedCategory();
            _selectedPost.Content = _memoEditor?.Text ?? "";

            // 제목이 비어있을 때만 자동 생성 (기존 제목 보존)
            if (string.IsNullOrWhiteSpace(_selectedPost.Title))
                _selectedPost.Title = ExtractTitleFromPlainText(_memoEditor?.PlainText);

            _selectedPost.DateTime = DateTime.Now;

            if (_currentTxtTitle != null)
                _currentTxtTitle.Text = _selectedPost.Title;

            using var service = Board.CreateService();

            int postNo = await service.SavePostAsync(_selectedPost);

            if (postNo > 0 && _currentFileList != null)
            {
                string fileCategory = _selectedPost.Category;

                foreach (var fileToDelete in _currentFileList.FilesToDelete)
                {
                    await service.DeletePostFileAsync(fileToDelete.No, fileCategory);
                    Debug.WriteLine($"[MemoBoard] 파일 삭제: {fileToDelete.FileName}");
                }

                foreach (var fileBox in _currentFileList.FileBoxes)
                {
                    if (!string.IsNullOrEmpty(fileBox.OrgFilePath) && fileBox.PostFile != null)
                    {
                        var savedFile = await SaveFileAsync(
                            fileBox.OrgFilePath,
                            postNo,
                            fileCategory);

                        if (savedFile != null)
                        {
                            await service.AddPostFileAsync(savedFile);
                            Debug.WriteLine($"[MemoBoard] 파일 저장: {savedFile.FileName}");
                        }
                    }
                }

                _selectedPost.HasFile = _currentFileList.FileCount > 0;
            }

            _isModified = false;

            Debug.WriteLine($"[MemoBoard] 메모 저장 완료: No={_selectedPost.No}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MemoBoard] 메모 저장 실패: {ex.Message}");
            await MessageBox.ShowAsync($"저장 중 오류가 발생했습니다.\n{ex.Message}", "오류");
        }
    }

    private async void BtnDelete_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedPost == null || _selectedPost.No <= 0) return;

        var confirmed = await MessageBox.ShowConfirmAsync(
            "이 메모를 삭제하시겠습니까?",
            "메모 삭제", "삭제", "취소");
        if (!confirmed) return;

        try
        {
            var deletedNo = _selectedPost.No;

            // 에디터를 현재 부모에서 분리
            DetachEditor();

            using var service = Board.CreateService();
            await service.DeletePostAsync(_selectedPost.No, _selectedPost.Category);

            var toRemove = _memos.FirstOrDefault(m => m.No == deletedNo);
            if (toRemove != null)
            {
                _memos.Remove(toRemove);
            }

            if (_memos.Count > 0)
            {
                _selectedPost = null;
                SelectedPost = _memos[0];
            }
            else
            {
                _selectedPost = null;
                RebuildList();
                await CreateNewMemoAsync();
            }

            PanelEmpty.Visibility = _memos.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;

            Debug.WriteLine($"[MemoBoard] 메모 삭제 완료: No={deletedNo}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MemoBoard] 메모 삭제 실패: {ex.Message}");
            await MessageBox.ShowAsync($"삭제 중 오류가 발생했습니다.\n{ex.Message}", "오류");
        }
    }

    #endregion

    private async void BtnAddMemo_Click(object sender, RoutedEventArgs e)
    {
        await CreateNewMemoAsync();
    }

    private async void CBoxCategoryFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isInitialized || _isLoading) return;

        if (_isModified && _selectedPost != null)
        {
            SaveCurrentMemo();
        }

        _selectedPost = null;
        DetachEditor();
        await LoadMemosAsync();

        if (_memos.Count > 0)
        {
            SelectedPost = _memos[0];
        }
    }

    private async void BtnOpenDialog_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedPost == null) return;

        if (_isModified)
        {
            SaveCurrentMemo();
        }

        var dialog = new Dialogs.MemoEditDialog(_selectedPost)
        {
            XamlRoot = XamlRoot
        };

        var result = await dialog.ShowAsync();

        if (result == ContentDialogResult.Primary)
        {
            await LoadMemosAsync();

            var updatedPost = _memos.FirstOrDefault(m => m.No == _selectedPost.No);
            if (updatedPost != null)
            {
                _selectedPost = null;
                SelectedPost = updatedPost;
            }
        }
    }

    private async Task<PostFile?> SaveFileAsync(string sourceFilePath, int postNo, string category)
    {
        try
        {
            Board.EnsureCategoryDirectory(category);

            var sourceFile = await StorageFile.GetFileFromPathAsync(sourceFilePath);
            var properties = await sourceFile.GetBasicPropertiesAsync();

            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var extension = Path.GetExtension(sourceFile.Name);
            var fileNameWithoutExt = Path.GetFileNameWithoutExtension(sourceFile.Name);
            var fileName = $"{timestamp}_{fileNameWithoutExt}{extension}";

            var destinationPath = Board.GetFilePath(fileName, category);
            var destinationFolder = await StorageFolder.GetFolderFromPathAsync(
                Path.GetDirectoryName(destinationPath));

            await sourceFile.CopyAsync(destinationFolder, fileName, NameCollisionOption.ReplaceExisting);

            var postFile = new PostFile
            {
                Post = postNo,
                FileName = fileName,
                FileSize = (int)properties.Size,
                DateTime = DateTime.Now
            };

            Debug.WriteLine($"[MemoBoard] 파일 저장 완료: {destinationPath}");
            return postFile;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MemoBoard] 파일 저장 실패: {ex.Message}");
            return null;
        }
    }
}
