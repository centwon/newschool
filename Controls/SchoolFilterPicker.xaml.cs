using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NewSchool.Repositories;

namespace NewSchool.Controls;

/// <summary>
/// 학년도/학기/학년/반 선택 공용 컨트롤
/// Enrollment 테이블 기반으로 데이터 조회
/// </summary>
public sealed partial class SchoolFilterPicker : UserControl
{
    #region Fields

    private bool _isInitialized = false;
    private bool _isUpdating = false;

    #endregion

    #region DependencyProperties

    /// <summary>
    /// 선택된 학년도
    /// </summary>
    public static readonly DependencyProperty SelectedYearProperty =
        DependencyProperty.Register(
            nameof(SelectedYear),
            typeof(int),
            typeof(SchoolFilterPicker),
            new PropertyMetadata(0, OnSelectedYearChanged));

    public int SelectedYear
    {
        get => (int)GetValue(SelectedYearProperty);
        set => SetValue(SelectedYearProperty, value);
    }

    /// <summary>
    /// 선택된 학기
    /// </summary>
    public static readonly DependencyProperty SelectedSemesterProperty =
        DependencyProperty.Register(
            nameof(SelectedSemester),
            typeof(int),
            typeof(SchoolFilterPicker),
            new PropertyMetadata(0, OnSelectedSemesterChanged));

    public int SelectedSemester
    {
        get => (int)GetValue(SelectedSemesterProperty);
        set => SetValue(SelectedSemesterProperty, value);
    }

    /// <summary>
    /// 선택된 학년 (0 = 전체)
    /// </summary>
    public static readonly DependencyProperty SelectedGradeProperty =
        DependencyProperty.Register(
            nameof(SelectedGrade),
            typeof(int),
            typeof(SchoolFilterPicker),
            new PropertyMetadata(0, OnSelectedGradeChanged));

    public int SelectedGrade
    {
        get => (int)GetValue(SelectedGradeProperty);
        set => SetValue(SelectedGradeProperty, value);
    }

    /// <summary>
    /// 선택된 반 (0 = 전체)
    /// </summary>
    public static readonly DependencyProperty SelectedClassProperty =
        DependencyProperty.Register(
            nameof(SelectedClass),
            typeof(int),
            typeof(SchoolFilterPicker),
            new PropertyMetadata(0, OnSelectedClassChanged));

    public int SelectedClass
    {
        get => (int)GetValue(SelectedClassProperty);
        set => SetValue(SelectedClassProperty, value);
    }

    /// <summary>
    /// 학기 표시 여부
    /// </summary>
    public static readonly DependencyProperty ShowSemesterProperty =
        DependencyProperty.Register(
            nameof(ShowSemester),
            typeof(bool),
            typeof(SchoolFilterPicker),
            new PropertyMetadata(true, OnShowSemesterChanged));

    public bool ShowSemester
    {
        get => (bool)GetValue(ShowSemesterProperty);
        set => SetValue(ShowSemesterProperty, value);
    }

    /// <summary>
    /// 학년 표시 여부
    /// </summary>
    public static readonly DependencyProperty ShowGradeProperty =
        DependencyProperty.Register(
            nameof(ShowGrade),
            typeof(bool),
            typeof(SchoolFilterPicker),
            new PropertyMetadata(true, OnShowGradeChanged));

    public bool ShowGrade
    {
        get => (bool)GetValue(ShowGradeProperty);
        set => SetValue(ShowGradeProperty, value);
    }

    /// <summary>
    /// 반 표시 여부
    /// </summary>
    public static readonly DependencyProperty ShowClassProperty =
        DependencyProperty.Register(
            nameof(ShowClass),
            typeof(bool),
            typeof(SchoolFilterPicker),
            new PropertyMetadata(true, OnShowClassChanged));

    public bool ShowClass
    {
        get => (bool)GetValue(ShowClassProperty);
        set => SetValue(ShowClassProperty, value);
    }

    /// <summary>
    /// 학년에 "전체" 포함 여부
    /// </summary>
    public static readonly DependencyProperty IncludeAllGradeProperty =
        DependencyProperty.Register(
            nameof(IncludeAllGrade),
            typeof(bool),
            typeof(SchoolFilterPicker),
            new PropertyMetadata(true));

    public bool IncludeAllGrade
    {
        get => (bool)GetValue(IncludeAllGradeProperty);
        set => SetValue(IncludeAllGradeProperty, value);
    }

    /// <summary>
    /// 반에 "전체" 포함 여부
    /// </summary>
    public static readonly DependencyProperty IncludeAllClassProperty =
        DependencyProperty.Register(
            nameof(IncludeAllClass),
            typeof(bool),
            typeof(SchoolFilterPicker),
            new PropertyMetadata(true));

    public bool IncludeAllClass
    {
        get => (bool)GetValue(IncludeAllClassProperty);
        set => SetValue(IncludeAllClassProperty, value);
    }

    #endregion

    #region Events

    /// <summary>
    /// 선택 변경 이벤트
    /// </summary>
    public event EventHandler<SchoolFilterChangedEventArgs>? SelectionChanged;

    #endregion

    #region Constructor

    public SchoolFilterPicker()
    {
        this.InitializeComponent();
        this.Loaded += SchoolFilterPicker_Loaded;
    }

    #endregion

    #region Lifecycle

    private async void SchoolFilterPicker_Loaded(object sender, RoutedEventArgs e)
    {
        if (_isInitialized) return;

        try
        {
            _isUpdating = true;

            // SchoolCode 미설정 시 빈 상태로 초기화
            if (string.IsNullOrEmpty(Settings.SchoolCode.Value))
            {
                Debug.WriteLine("[SchoolFilterPicker] SchoolCode 미설정 - 필터 초기화 건너뜀");
                _isInitialized = true;
                return;
            }

            // 표시 여부 적용
            ApplyVisibility();

            // 학기 콤보 초기화 (고정값)
            InitializeSemesterComboBox();

            // 학년도 콤보 초기화 (DB + 현재년도)
            await InitializeYearComboBoxAsync();

            // 기본값을 먼저 설정 (순차적으로)
            await ApplyDefaultSelectionAsync();

            _isInitialized = true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SchoolFilterPicker] 초기화 오류: {ex.Message}");
        }
        finally
        {
            _isUpdating = false;
            
            // 초기화 완료 후 이벤트 발생
            RaiseSelectionChanged();
        }
    }

    #endregion

    #region Initialization

    /// <summary>
    /// 표시 여부 적용
    /// </summary>
    private void ApplyVisibility()
    {
        CBoxSemester.Visibility = ShowSemester ? Visibility.Visible : Visibility.Collapsed;
        CBoxGrade.Visibility = ShowGrade ? Visibility.Visible : Visibility.Collapsed;
        CBoxClass.Visibility = ShowClass ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// 학기 콤보박스 초기화
    /// </summary>
    private void InitializeSemesterComboBox()
    {
        CBoxSemester.Items.Clear();
        CBoxSemester.Items.Add(new ComboBoxItem { Content = "1학기", Tag = 1 });
        CBoxSemester.Items.Add(new ComboBoxItem { Content = "2학기", Tag = 2 });
    }

    /// <summary>
    /// 학년도 콤보박스 초기화 (Enrollment + 현재년도)
    /// </summary>
    private async Task InitializeYearComboBoxAsync()
    {
        CBoxYear.Items.Clear();

        // 현재 년도 + 설정된 WorkYear 모두 포함
        var years = new HashSet<int> { DateTime.Today.Year };
        if (Settings.WorkYear.Value > 0)
        {
            years.Add(Settings.WorkYear.Value);
        }

        try
        {
            // Enrollment에서 학년도 목록 조회
            using var repo = new EnrollmentRepository(SchoolDatabase.DbPath);
            var enrollmentYears = await repo.GetEnrollmentYearsAsync(Settings.SchoolCode.Value);
            
            foreach (var year in enrollmentYears)
            {
                years.Add(year);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SchoolFilterPicker] 학년도 조회 오류: {ex.Message}");
        }

        // 내림차순 정렬
        foreach (var year in years.OrderByDescending(y => y))
        {
            CBoxYear.Items.Add(new ComboBoxItem { Content = $"{year}학년도", Tag = year });
        }
    }

    /// <summary>
    /// 학년 콤보박스 초기화
    /// </summary>
    private async Task InitializeGradeComboBoxAsync()
    {
        CBoxGrade.Items.Clear();

        if (IncludeAllGrade)
        {
            CBoxGrade.Items.Add(new ComboBoxItem { Content = "전체", Tag = 0 });
        }

        var grades = new HashSet<int>();

        try
        {
            if (SelectedYear > 0)
            {
                using var repo = new EnrollmentRepository(SchoolDatabase.DbPath);
                var enrollmentGrades = await repo.GetGradesByYearAsync(Settings.SchoolCode.Value, SelectedYear);
                
                foreach (var grade in enrollmentGrades)
                {
                    grades.Add(grade);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SchoolFilterPicker] 학년 조회 오류: {ex.Message}");
        }

        // 데이터가 없으면 기본값 (1, 2, 3)
        if (grades.Count == 0)
        {
            grades = new HashSet<int> { 1, 2, 3 };
        }

        foreach (var grade in grades.OrderBy(g => g))
        {
            CBoxGrade.Items.Add(new ComboBoxItem { Content = $"{grade}학년", Tag = grade });
        }
    }

    /// <summary>
    /// 반 콤보박스 초기화
    /// </summary>
    private async Task InitializeClassComboBoxAsync()
    {
        CBoxClass.Items.Clear();

        if (IncludeAllClass)
        {
            CBoxClass.Items.Add(new ComboBoxItem { Content = "전체", Tag = 0 });
        }

        var classes = new HashSet<int>();

        try
        {
            if (SelectedYear > 0 && SelectedGrade > 0)
            {
                using var repo = new EnrollmentRepository(SchoolDatabase.DbPath);
                var enrollmentClasses = await repo.GetClassListByGradeAsync(
                    Settings.SchoolCode.Value, SelectedYear, SelectedGrade);
                
                foreach (var classNo in enrollmentClasses)
                {
                    classes.Add(classNo);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SchoolFilterPicker] 반 조회 오류: {ex.Message}");
        }

        foreach (var classNo in classes.OrderBy(c => c))
        {
            CBoxClass.Items.Add(new ComboBoxItem { Content = $"{classNo}반", Tag = classNo });
        }
    }

    /// <summary>
    /// 기본 선택값 적용 (순차적으로 - 학년도 → 학년 → 반)
    /// </summary>
    private async Task ApplyDefaultSelectionAsync()
    {
        // 1. 학년도 기본값 설정
        int defaultYear = SelectedYear > 0 ? SelectedYear : Settings.WorkYear.Value;
        SelectComboBoxByTag(CBoxYear, defaultYear);
        SelectedYear = defaultYear;

        // 2. 학기 기본값 설정
        int defaultSemester = SelectedSemester > 0 ? SelectedSemester : Settings.WorkSemester.Value;
        SelectComboBoxByTag(CBoxSemester, defaultSemester);
        SelectedSemester = defaultSemester;

        // 3. 학년 콤보 초기화 (학년도가 설정된 후)
        await InitializeGradeComboBoxAsync();

        // 4. 학년 기본값 설정
        int defaultGrade = SelectedGrade > 0 ? SelectedGrade :
            (IncludeAllGrade ? 0 : Settings.HomeGrade.Value);
        if (defaultGrade > 0)
            SelectComboBoxByTag(CBoxGrade, defaultGrade);
        // HomeGrade 미설정(0)이고 IncludeAllGrade=False면 첫 번째 항목 선택
        if (defaultGrade == 0 && !IncludeAllGrade && CBoxGrade.Items.Count > 0)
        {
            CBoxGrade.SelectedIndex = 0;
            if (CBoxGrade.SelectedItem is ComboBoxItem gi && gi.Tag is int g)
                defaultGrade = g;
        }
        SelectedGrade = defaultGrade;

        // 5. 반 콤보 초기화 (학년이 설정된 후)
        await InitializeClassComboBoxAsync();

        // 6. 반 기본값 설정
        int defaultClass = SelectedClass > 0 ? SelectedClass :
            (IncludeAllClass ? 0 : Settings.HomeRoom.Value);
        if (defaultClass > 0)
            SelectComboBoxByTag(CBoxClass, defaultClass);
        // HomeRoom 미설정(0)이고 IncludeAllClass=False면 첫 번째 항목 선택
        if (defaultClass == 0 && !IncludeAllClass && CBoxClass.Items.Count > 0)
        {
            CBoxClass.SelectedIndex = 0;
            if (CBoxClass.SelectedItem is ComboBoxItem ci && ci.Tag is int c)
                defaultClass = c;
        }
        SelectedClass = defaultClass;
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// 외부에서 선택값 설정
    /// </summary>
    public void SetSelection(int year, int semester = 0, int grade = 0, int classNo = 0)
    {
        _isUpdating = true;

        try
        {
            if (year > 0) SelectComboBoxByTag(CBoxYear, year);
            if (semester > 0) SelectComboBoxByTag(CBoxSemester, semester);
            SelectComboBoxByTag(CBoxGrade, grade);
            SelectComboBoxByTag(CBoxClass, classNo);

            // DependencyProperty 업데이트
            SelectedYear = year;
            SelectedSemester = semester;
            SelectedGrade = grade;
            SelectedClass = classNo;
        }
        finally
        {
            _isUpdating = false;
        }

        RaiseSelectionChanged();
    }

    /// <summary>
    /// 데이터 새로고침
    /// </summary>
    public async Task RefreshAsync()
    {
        _isUpdating = true;

        try
        {
            await InitializeYearComboBoxAsync();
            await ApplyDefaultSelectionAsync();
        }
        finally
        {
            _isUpdating = false;
        }
    }

    #endregion

    #region Event Handlers - ComboBox Selection

    private async void CBoxYear_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isInitialized || _isUpdating) return;

        var selectedItem = CBoxYear.SelectedItem as ComboBoxItem;
        if (selectedItem?.Tag is int year)
        {
            SelectedYear = year;

            // 학년/반 콤보 갱신
            _isUpdating = true;
            await InitializeGradeComboBoxAsync();
            await InitializeClassComboBoxAsync();
            
            // 기본값 선택
            if (CBoxGrade.Items.Count > 0) CBoxGrade.SelectedIndex = 0;
            if (CBoxClass.Items.Count > 0) CBoxClass.SelectedIndex = 0;
            _isUpdating = false;

            RaiseSelectionChanged();
        }
    }

    private void CBoxSemester_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isInitialized || _isUpdating) return;

        var selectedItem = CBoxSemester.SelectedItem as ComboBoxItem;
        if (selectedItem?.Tag is int semester)
        {
            SelectedSemester = semester;
            RaiseSelectionChanged();
        }
    }

    private async void CBoxGrade_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isInitialized || _isUpdating) return;

        var selectedItem = CBoxGrade.SelectedItem as ComboBoxItem;
        if (selectedItem?.Tag is int grade)
        {
            SelectedGrade = grade;

            // 반 콤보 갱신
            _isUpdating = true;
            await InitializeClassComboBoxAsync();
            if (CBoxClass.Items.Count > 0) CBoxClass.SelectedIndex = 0;
            _isUpdating = false;

            RaiseSelectionChanged();
        }
    }

    private void CBoxClass_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isInitialized || _isUpdating) return;

        var selectedItem = CBoxClass.SelectedItem as ComboBoxItem;
        if (selectedItem?.Tag is int classNo)
        {
            SelectedClass = classNo;
            RaiseSelectionChanged();
        }
    }

    #endregion

    #region DependencyProperty Callbacks

    private static void OnSelectedYearChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SchoolFilterPicker picker && picker._isInitialized && !picker._isUpdating)
        {
            picker.SelectComboBoxByTag(picker.CBoxYear, (int)e.NewValue);
        }
    }

    private static void OnSelectedSemesterChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SchoolFilterPicker picker && picker._isInitialized && !picker._isUpdating)
        {
            picker.SelectComboBoxByTag(picker.CBoxSemester, (int)e.NewValue);
        }
    }

    private static void OnSelectedGradeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SchoolFilterPicker picker && picker._isInitialized && !picker._isUpdating)
        {
            picker.SelectComboBoxByTag(picker.CBoxGrade, (int)e.NewValue);
        }
    }

    private static void OnSelectedClassChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SchoolFilterPicker picker && picker._isInitialized && !picker._isUpdating)
        {
            picker.SelectComboBoxByTag(picker.CBoxClass, (int)e.NewValue);
        }
    }

    private static void OnShowSemesterChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SchoolFilterPicker picker)
        {
            picker.CBoxSemester.Visibility = (bool)e.NewValue ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private static void OnShowGradeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SchoolFilterPicker picker)
        {
            picker.CBoxGrade.Visibility = (bool)e.NewValue ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private static void OnShowClassChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SchoolFilterPicker picker)
        {
            picker.CBoxClass.Visibility = (bool)e.NewValue ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Tag 값으로 ComboBox 아이템 선택
    /// </summary>
    private void SelectComboBoxByTag(ComboBox comboBox, int tagValue)
    {
        foreach (var item in comboBox.Items)
        {
            if (item is ComboBoxItem comboItem && comboItem.Tag is int tag && tag == tagValue)
            {
                comboBox.SelectedItem = comboItem;
                return;
            }
        }

        // 찾지 못하면 첫 번째 선택
        if (comboBox.Items.Count > 0)
        {
            comboBox.SelectedIndex = 0;
        }
    }

    /// <summary>
    /// SelectionChanged 이벤트 발생
    /// </summary>
    private void RaiseSelectionChanged()
    {
        SelectionChanged?.Invoke(this, new SchoolFilterChangedEventArgs
        {
            Year = SelectedYear,
            Semester = SelectedSemester,
            Grade = SelectedGrade,
            Class = SelectedClass
        });
    }

    #endregion
}

/// <summary>
/// 필터 변경 이벤트 인자
/// </summary>
public class SchoolFilterChangedEventArgs : EventArgs
{
    public int Year { get; set; }
    public int Semester { get; set; }
    public int Grade { get; set; }
    public int Class { get; set; }

    /// <summary>
    /// 전체 선택 여부 (학년 또는 반이 0인 경우)
    /// </summary>
    public bool IsAllGrade => Grade == 0;
    public bool IsAllClass => Class == 0;
}
