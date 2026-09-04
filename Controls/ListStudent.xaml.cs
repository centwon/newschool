using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using NewSchool.Models;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;

namespace NewSchool.Controls;

/// <summary>
/// 학생 목록 UserControl (WinUI3 + ListView)
/// Enrollment 모델 직접 사용
/// 
/// 주요 기능:
/// 1. 4가지 표시 모드 (Full, ClassNumName, NumName, NameOnly)
/// 2. 체크박스 다중 선택 지원 (Multiple 모드)
/// 3. 드래그 앤 드롭 지원
/// 4. 전체 선택/해제
/// </summary>
public sealed partial class ListStudent : UserControl
{
    #region Fields

    private View _viewMode = View.NumName;
    private bool _showCheck = false;
    private bool _suppressSelectAllEvent = false;

    #endregion

    #region Events

    public event EventHandler<Enrollment>? StudentSelected;

    #endregion

    #region Properties

    public ObservableCollection<Enrollment> Students { get; } = new();

    public enum View { Full, ClassNumName, NumName, NameOnly }

    public View ViewMode
    {
        get => _viewMode;
        set { _viewMode = value; ApplyViewMode(); }
    }

    public bool ShowCheckBox
    {
        get => _showCheck;
        set { _showCheck = value; ApplyCheckBoxVisibility(); }
    }

    /// <summary>
    /// 아이템 우클릭 시 표시할 컨텍스트 메뉴 (사용처에서 주입)
    /// </summary>
    public FlyoutBase? ItemContextFlyout { get; set; }

    #endregion

    #region Constructor

    public ListStudent()
    {
        this.InitializeComponent();
        StudentListView.ItemsSource = Students;
        ApplyViewMode();
    }

    #endregion

    #region View Mode

    private double _checkWidth = 0;
    private double _gradeWidth = 0;
    private double _classWidth = 0;
    private double _numberWidth = 50;

    private void ApplyViewMode()
    {
        switch (_viewMode)
        {
            case View.Full:
                _gradeWidth = 50; _classWidth = 50; _numberWidth = 50;
                break;
            case View.ClassNumName:
                _gradeWidth = 0; _classWidth = 50; _numberWidth = 50;
                break;
            case View.NumName:
                _gradeWidth = 0; _classWidth = 0; _numberWidth = 50;
                break;
            case View.NameOnly:
                _gradeWidth = 0; _classWidth = 0; _numberWidth = 0;
                break;
        }

        ColGradeHeader.Width = new GridLength(_gradeWidth);
        ColClassHeader.Width = new GridLength(_classWidth);
        ColNumberHeader.Width = new GridLength(_numberWidth);

        RefreshListViewTemplate();
    }

    private void RefreshListViewTemplate()
    {
        var items = StudentListView.ItemsSource;
        StudentListView.ItemsSource = null;
        StudentListView.ItemsSource = items;
    }

    private void OnItemGridLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is Grid grid && grid.ColumnDefinitions.Count >= 5)
        {
            grid.ColumnDefinitions[0].Width = new GridLength(_checkWidth);
            grid.ColumnDefinitions[1].Width = new GridLength(_gradeWidth);
            grid.ColumnDefinitions[2].Width = new GridLength(_classWidth);
            grid.ColumnDefinitions[3].Width = new GridLength(_numberWidth);
        }
    }

    #endregion

    #region CheckBox Management

    private void ApplyCheckBoxVisibility()
    {
        if (_showCheck)
        {
            // Multiple 모드
            _checkWidth = 0; // ListView 자체 체크마크 사용, 별도 열 불필요
            ColCheckHeader.Width = new GridLength(48);
            ChkSelectAll.Visibility = Visibility.Visible;
            PnlSelectionInfo.Visibility = Visibility.Visible;

            StudentListView.SelectionMode = ListViewSelectionMode.Multiple;
            StudentListView.CanDragItems = true;

            UpdateSelectionInfo();
        }
        else
        {
            // Single 모드
            _checkWidth = 0;
            ColCheckHeader.Width = new GridLength(0);
            ChkSelectAll.Visibility = Visibility.Collapsed;
            PnlSelectionInfo.Visibility = Visibility.Collapsed;

            StudentListView.SelectionMode = ListViewSelectionMode.Single;
            StudentListView.CanDragItems = true;
            StudentListView.SelectedItem = null;
        }

        RefreshListViewTemplate();
    }

    private void OnSelectAllChecked(object sender, RoutedEventArgs e)
    {
        if (_suppressSelectAllEvent) return;
        StudentListView.SelectAll();
    }

    private void OnSelectAllUnchecked(object sender, RoutedEventArgs e)
    {
        if (_suppressSelectAllEvent) return;
        StudentListView.SelectedItems.Clear();
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_showCheck && StudentListView.SelectedItem is Enrollment selected)
        {
            StudentSelected?.Invoke(this, selected);
        }

        if (_showCheck)
        {
            UpdateSelectAllCheckBox();
            UpdateSelectionInfo();
        }
    }

    private void UpdateSelectAllCheckBox()
    {
        _suppressSelectAllEvent = true;
        try
        {
            int count = StudentListView.SelectedItems.Count;
            int total = Students.Count;

            if (total == 0 || count == 0)
                ChkSelectAll.IsChecked = false;
            else if (count == total)
                ChkSelectAll.IsChecked = true;
            else
                ChkSelectAll.IsChecked = null;
        }
        finally
        {
            _suppressSelectAllEvent = false;
        }
    }

    private void UpdateSelectionInfo()
    {
        if (TxtSelectionInfo == null) return;

        int selected = StudentListView.SelectedItems.Count;
        int total = Students.Count;

        TxtSelectionInfo.Text = selected > 0
            ? $"✔ {selected} / {total}명 선택됨"
            : $"전체 {total}명";
    }

    #endregion

    #region Context Menu

    /// <summary>
    /// 학생 항목의 컨텍스트 메뉴를 연다. <b>마우스 오른쪽 단추와 키보드 메뉴 키를 함께</b> 받는다.
    ///
    /// <para>예전에는 <c>RightTapped</c> 를 썼다. 그 이벤트는 포인터에만 오고 키보드의
    /// 메뉴 키·Shift+F10 에는 오지 않아, 키보드만 쓰는 사람에게는 [누가기록 작성]·
    /// [학생 정보 보기]가 아예 없는 것과 같았다(54차). <c>ContextRequested</c> 는 둘 다 받는다.</para>
    ///
    /// <para><c>TryGetPosition</c> 이 <c>false</c> 를 주면 키보드로 부른 것이다 — 그때는
    /// 좌표가 없으므로 항목 자체에 붙여 띄운다.</para>
    /// </summary>
    private void OnItemContextRequested(UIElement sender, ContextRequestedEventArgs e)
    {
        if (sender is not Grid grid) return;
        if (grid.DataContext is not Enrollment enrollment) return;

        // 메뉴를 부른 항목을 선택 상태로 바꾼다
        StudentListView.SelectedItem = enrollment;

        if (ItemContextFlyout == null) return;

        bool hasPoint = e.TryGetPosition(grid, out var point);

        if (ItemContextFlyout is MenuFlyout menuFlyout)
        {
            if (hasPoint) menuFlyout.ShowAt(grid, point);
            else menuFlyout.ShowAt(grid);
        }
        else
        {
            FlyoutBase.SetAttachedFlyout(grid, ItemContextFlyout);
            FlyoutBase.ShowAttachedFlyout(grid);
        }

        e.Handled = true;
    }

    #endregion

    #region Drag and Drop

    // 참고: 150% 등 비100% 배율에서 드래그 비주얼이 커서와 어긋나는 것은 WinUI 3 프레임워크 버그
    // (microsoft-ui-xaml #7008 / #10144)로, DragStarting 에서 비주얼/앵커를 바꿔도 고쳐지지 않는다.
    // 표준 ListView 드래그를 사용하고, 근본 해결은 추후 자체 드래그(고스트) 구현으로 검토.
    private void OnDragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        if (e.Items.Count > 0 && e.Items[0] is Enrollment student)
        {
            e.Data.Properties.Add("Enrollment", student);
            e.Data.Properties.Add("SourceControl", "ListStudent");
            e.Data.RequestedOperation = DataPackageOperation.Copy;
            e.Data.SetText($"{student.Name}({student.Number})");
        }
    }

    #endregion

    #region Public Methods

    public void LoadStudents(System.Collections.Generic.IEnumerable<Enrollment> students)
    {
        // 벌크 로딩: ItemsSource 해제 → 데이터 변경 → 재연결 (N번 레이아웃 방지)
        StudentListView.ItemsSource = null;
        Students.Clear();
        foreach (var s in students) Students.Add(s);
        StudentListView.ItemsSource = Students;

        if (_showCheck) UpdateSelectionInfo();
    }

    public void ClearStudents()
    {
        Students.Clear();
        if (_showCheck) UpdateSelectionInfo();
    }

    public void ClearSelection()
    {
        ChkSelectAll.IsChecked = false;
        StudentListView.SelectedItem = null;
    }

    // SelectedCount 는 쓰는 곳이 없어 지웠다(39차) — 선택 결과는 목록을 직접 받아 센다.

    public void SelectAll() => StudentListView.SelectAll();

    public void DeselectAll()
    {
        StudentListView.SelectedItems.Clear();
        ChkSelectAll.IsChecked = false;
    }

    public Enrollment? SelectedStudent
    {
        get => (Enrollment?)StudentListView.SelectedItem;
        set => StudentListView.SelectedItem = value;
    }

    public System.Collections.Generic.IEnumerable<Enrollment> GetSelectedStudents()
        => StudentListView.SelectedItems.Cast<Enrollment>();

    public void SelectStudent(string studentId)
    {
        var student = Students.FirstOrDefault(s => s.StudentID == studentId);
        if (student != null)
        {
            StudentListView.SelectedItem = student;
            StudentListView.ScrollIntoView(student);
        }
    }

    #endregion
}
