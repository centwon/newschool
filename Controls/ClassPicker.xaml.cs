using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NewSchool.Models;
using NewSchool.Repositories;

namespace NewSchool.Controls;

/// <summary>
/// 학년 · 반 선택 필터.
///
/// 확정 규칙:
///   - IncludeAllClass=true 이면 반 목록에 "전체(0)" 항목 포함 (기본 true).
///   - IncludeAllGrade=true 이면 학년 목록에 "전체(0)" 항목 포함 (기본 false).
///   - ShowGrade/ShowClass=false 이면 해당 콤보를 숨김(값은 전체(0) 취급).
///   - LoadAsync(year, semester) 로 학년/반 목록을 (재)로드.
///     YearSemesterPicker.YearSemesterChanged 에서 호출하거나,
///     단독 사용 시 Loaded 에서 Settings.WorkYear/WorkSemester 로 자동 초기화.
///   - 학년이 확정되면(전체 포함) 학생 목록을 조회해서 ClassChangedEventArgs.Students 에 담아 이벤트 발생.
/// </summary>
public sealed partial class ClassPicker : UserControl
{
    // ── 상태 ────────────────────────────────────────────
    private bool _initialized;
    private bool _updating;
    private int _loadedYear;
    private int _loadedSemester;

    // ── 옵션 ────────────────────────────────────────────
    /// <summary>학년 콤보 표시 여부 (기본 true)</summary>
    public bool ShowGrade { get; set; } = true;
    /// <summary>반 콤보 표시 여부 (기본 true)</summary>
    public bool ShowClass { get; set; } = true;
    /// <summary>학년 목록에 "전체(0)" 항목 포함 여부 (기본 false)</summary>
    public bool IncludeAllGrade { get; set; } = false;
    /// <summary>반 목록에 "전체(0)" 항목 포함 여부 (기본 true)</summary>
    public bool IncludeAllClass { get; set; } = true;
    /// <summary>
    /// Loaded 시 Settings 값으로 자동 초기화할지 여부 (기본 true).
    /// YearSemesterPicker와 함께 써서 YearSemesterChanged에서 명시적으로 LoadAsync를
    /// 호출하는 경우, 이중 로드를 막기 위해 false로 지정해야 한다.
    /// </summary>
    public bool AutoLoad { get; set; } = true;

    // ── 현재 선택값 ─────────────────────────────────────
    public int Grade => ShowGrade ? GetTag(CBoxGrade) : 0;
    public int ClassNum => ShowClass ? GetTag(CBoxClass) : 0;

    // ── 이벤트 ──────────────────────────────────────────
    public event EventHandler<ClassChangedEventArgs>? ClassChanged;

    // ── 생성자 ──────────────────────────────────────────
    public ClassPicker()
    {
        this.InitializeComponent();
        this.Loaded += OnLoaded;
    }

    // ── 초기화 ──────────────────────────────────────────

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_initialized || !AutoLoad) return;
        // YearSemesterPicker 없이 단독 사용 시 Settings 값으로 자동 초기화
        await LoadAsync(Settings.WorkYear.Value, Settings.WorkSemester.Value);
    }

    /// <summary>
    /// 학년/반 목록을 (재)로드.
    /// YearSemesterPicker.YearSemesterChanged 에서 호출.
    /// </summary>
    public async Task LoadAsync(int year, int semester)
    {
        _loadedYear = year;
        _loadedSemester = semester;

        _updating = true;
        try
        {
            CBoxGrade.Visibility = ShowGrade ? Visibility.Visible : Visibility.Collapsed;
            CBoxClass.Visibility = ShowClass ? Visibility.Visible : Visibility.Collapsed;

            await InitGradeComboAsync(year);
            ApplyDefaultGrade();

            await InitClassComboAsync(year, GetTag(CBoxGrade));
            ApplyDefaultClass();

            _initialized = true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ClassPicker] 초기화 오류: {ex.Message}");
        }
        finally
        {
            _updating = false;
        }

        await RaiseChangedAsync();
    }

    // ── 콤보 구성 ────────────────────────────────────────

    private async Task InitGradeComboAsync(int year)
    {
        var grades = new HashSet<int>();
        try
        {
            if (year > 0)
            {
                using var repo = new EnrollmentRepository(SchoolDatabase.DbPath);
                foreach (var g in await repo.GetGradesByYearAsync(Settings.SchoolCode.Value, year))
                    grades.Add(g);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ClassPicker] 학년 조회 오류: {ex.Message}");
        }

        if (grades.Count == 0) grades = new HashSet<int> { 1, 2, 3 };

        CBoxGrade.Items.Clear();
        if (IncludeAllGrade)
            CBoxGrade.Items.Add(new ComboBoxItem { Content = "전체", Tag = 0 });
        foreach (var g in grades.OrderBy(x => x))
            CBoxGrade.Items.Add(new ComboBoxItem { Content = $"{g}학년", Tag = g });
    }

    private async Task InitClassComboAsync(int year, int grade)
    {
        var classes = new HashSet<int>();
        try
        {
            if (year > 0 && grade > 0)
            {
                using var repo = new EnrollmentRepository(SchoolDatabase.DbPath);
                foreach (var c in await repo.GetClassListByGradeAsync(Settings.SchoolCode.Value, year, grade))
                    classes.Add(c);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ClassPicker] 반 조회 오류: {ex.Message}");
        }

        CBoxClass.Items.Clear();
        if (IncludeAllClass)
            CBoxClass.Items.Add(new ComboBoxItem { Content = "전체", Tag = 0 });
        foreach (var c in classes.OrderBy(x => x))
            CBoxClass.Items.Add(new ComboBoxItem { Content = $"{c}반", Tag = c });
    }

    private void ApplyDefaultGrade()
    {
        int preferred = Settings.HomeGrade.Value;
        if (preferred > 0) SelectByTag(CBoxGrade, preferred);
        if (CBoxGrade.SelectedItem is null && CBoxGrade.Items.Count > 0)
            CBoxGrade.SelectedIndex = 0;
    }

    private void ApplyDefaultClass()
    {
        int preferredGrade = Settings.HomeGrade.Value;
        int preferredClass = Settings.HomeRoom.Value;

        // 담임 학급이 현재 선택된 학년과 일치하면 우선 자동 선택 (전체 옵션 포함 여부와 무관)
        if (preferredClass > 0 && GetTag(CBoxGrade) == preferredGrade && HasTag(CBoxClass, preferredClass))
        {
            SelectByTag(CBoxClass, preferredClass);
        }
        else if (IncludeAllClass)
        {
            SelectByTag(CBoxClass, 0);
        }

        if (CBoxClass.SelectedItem is null && CBoxClass.Items.Count > 0)
            CBoxClass.SelectedIndex = 0;
    }

    // ── ComboBox 이벤트 ──────────────────────────────────

    private async void CBoxGrade_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized || _updating) return;
        _updating = true;
        try
        {
            await InitClassComboAsync(_loadedYear, GetTag(CBoxGrade));
            ApplyDefaultClass();
        }
        finally { _updating = false; }
        await RaiseChangedAsync();
    }

    private async void CBoxClass_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized || _updating) return;
        await RaiseChangedAsync();
    }

    // ── 학생 조회 & 이벤트 발생 ─────────────────────────

    private async Task RaiseChangedAsync()
    {
        if (ClassChanged == null) return;

        int year = _loadedYear;
        int sem = _loadedSemester;
        int grade = Grade;
        int classNo = ClassNum;

        List<Enrollment> students;
        try
        {
            using var repo = new EnrollmentRepository(SchoolDatabase.DbPath);
            if (grade <= 0)
                // 전체 학년: 반 구분은 학년 간 번호가 겹치므로 적용하지 않음
                students = await repo.GetByGradeAsync(Settings.SchoolCode.Value, year, sem, 0);
            else if (classNo == 0)
                students = await repo.GetByGradeAsync(Settings.SchoolCode.Value, year, sem, grade);
            else
                students = await repo.GetByClassAsync(Settings.SchoolCode.Value, year, grade, classNo);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ClassPicker] 학생 조회 오류: {ex.Message}");
            students = new List<Enrollment>();
        }

        ClassChanged?.Invoke(this, new ClassChangedEventArgs
        {
            Year = year,
            Semester = sem,
            Grade = grade,
            Class = classNo,
            Students = students.AsReadOnly(),
        });
    }

    // ── 공개 메서드 ──────────────────────────────────────

    /// <summary>외부에서 학년·반을 강제 지정 후 이벤트 발생</summary>
    public async Task SetSelectionAsync(int grade, int classNum = 0)
    {
        _updating = true;
        try
        {
            SelectByTag(CBoxGrade, grade);
            await InitClassComboAsync(_loadedYear, grade);
            SelectByTag(CBoxClass, classNum);
        }
        finally { _updating = false; }
        await RaiseChangedAsync();
    }

    // ── 헬퍼 ────────────────────────────────────────────

    private static int GetTag(ComboBox cb)
    {
        if (cb.SelectedItem is ComboBoxItem ci && ci.Tag is int v) return v;
        return 0;
    }

    private static bool HasTag(ComboBox cb, int tag)
    {
        foreach (var item in cb.Items)
            if (item is ComboBoxItem ci && ci.Tag is int v && v == tag) return true;
        return false;
    }

    private static void SelectByTag(ComboBox cb, int tag)
    {
        foreach (var item in cb.Items)
        {
            if (item is ComboBoxItem ci && ci.Tag is int v && v == tag)
            {
                cb.SelectedItem = ci;
                return;
            }
        }
        if (cb.Items.Count > 0) cb.SelectedIndex = 0;
    }
}

/// <summary>학급 필터 변경 이벤트 인자 — 학생 목록 포함</summary>
public sealed class ClassChangedEventArgs : EventArgs
{
    public int Year { get; init; }
    public int Semester { get; init; }
    public int Grade { get; init; }
    public int Class { get; init; }
    public IReadOnlyList<Enrollment> Students { get; init; } = Array.Empty<Enrollment>();

    public bool IsAllGrade => Grade == 0;
    public bool IsAllClass => Class == 0;
}
