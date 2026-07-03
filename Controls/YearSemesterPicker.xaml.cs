using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NewSchool.Repositories;

namespace NewSchool.Controls;

/// <summary>
/// 학년도(선택) · 학기(선택) 선택 필터.
///
/// 확정 규칙:
///   - ShowYear=false 이면 학년도 콤보 숨김, Year 는 Settings.WorkYear 고정.
///   - ShowSemester=false 이면 학기 콤보 숨김, Semester 는 Settings.WorkSemester 고정.
///   - 학년도 목록은 DB에서 조회하고, Settings.WorkYear 를 기본 선택.
///   - 학기는 1/2학기 고정 목록.
///   - 값이 바뀌면 YearSemesterChanged 이벤트 발생.
/// </summary>
public sealed partial class YearSemesterPicker : UserControl
{
    // ── 상태 ────────────────────────────────────────────
    private bool _initialized;
    private bool _updating;

    // ── 옵션 ────────────────────────────────────────────
    /// <summary>학년도 콤보 표시 여부 (기본 true)</summary>
    public bool ShowYear { get; set; } = true;
    /// <summary>학기 콤보 표시 여부 (기본 true)</summary>
    public bool ShowSemester { get; set; } = true;

    // ── 현재 선택값 ─────────────────────────────────────
    public int Year => ShowYear ? GetTag(CBoxYear) : Settings.WorkYear.Value;
    public int Semester => ShowSemester ? GetTag(CBoxSemester) : Settings.WorkSemester.Value;

    // ── 이벤트 ──────────────────────────────────────────
    public event EventHandler<YearSemesterChangedEventArgs>? YearSemesterChanged;

    // ── 생성자 ──────────────────────────────────────────
    public YearSemesterPicker()
    {
        this.InitializeComponent();
        this.Loaded += OnLoaded;
    }

    // ── 초기화 ──────────────────────────────────────────

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_initialized) return;
        await InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        _updating = true;
        try
        {
            CBoxYear.Visibility = ShowYear ? Visibility.Visible : Visibility.Collapsed;
            CBoxSemester.Visibility = ShowSemester ? Visibility.Visible : Visibility.Collapsed;

            if (string.IsNullOrEmpty(Settings.SchoolCode.Value))
            {
                Debug.WriteLine("[YearSemesterPicker] SchoolCode 미설정 - 필터 초기화 건너뜀");
                _initialized = true;
                return;
            }

            if (ShowYear)
                await InitYearComboAsync();

            if (ShowSemester)
                InitSemesterCombo();

            _initialized = true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[YearSemesterPicker] 초기화 오류: {ex.Message}");
        }
        finally
        {
            _updating = false;
        }

        RaiseChanged();
    }

    // ── 콤보 구성 ────────────────────────────────────────

    private async Task InitYearComboAsync()
    {
        var years = new List<int>();
        try
        {
            using var repo = new EnrollmentRepository(SchoolDatabase.DbPath);
            years = await repo.GetEnrollmentYearsAsync(Settings.SchoolCode.Value);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[YearSemesterPicker] 학년도 조회 오류: {ex.Message}");
        }

        // DB에 데이터 없으면 현재 작업연도 포함 최근 3년 기본값
        if (years.Count == 0)
        {
            int cur = Settings.WorkYear.Value > 0 ? Settings.WorkYear.Value : DateTime.Now.Year;
            years = new List<int> { cur, cur - 1, cur - 2 };
        }

        years.Sort((a, b) => b.CompareTo(a)); // 내림차순

        CBoxYear.Items.Clear();
        foreach (var y in years)
            CBoxYear.Items.Add(new ComboBoxItem { Content = $"{y}학년도", Tag = y });

        SelectByTag(CBoxYear, Settings.WorkYear.Value);
        if (CBoxYear.SelectedItem is null && CBoxYear.Items.Count > 0)
            CBoxYear.SelectedIndex = 0;
    }

    private void InitSemesterCombo()
    {
        CBoxSemester.Items.Clear();
        CBoxSemester.Items.Add(new ComboBoxItem { Content = "1학기", Tag = 1 });
        CBoxSemester.Items.Add(new ComboBoxItem { Content = "2학기", Tag = 2 });

        int preferred = Settings.WorkSemester.Value > 0 ? Settings.WorkSemester.Value : 1;
        SelectByTag(CBoxSemester, preferred);
    }

    // ── ComboBox 이벤트 ──────────────────────────────────

    private void CBoxYear_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized || _updating) return;
        RaiseChanged();
    }

    private void CBoxSemester_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized || _updating) return;
        RaiseChanged();
    }

    // ── 이벤트 발생 ──────────────────────────────────────

    private void RaiseChanged()
    {
        YearSemesterChanged?.Invoke(this, new YearSemesterChangedEventArgs
        {
            Year = Year,
            Semester = Semester,
        });
    }

    // ── 공개 메서드 ──────────────────────────────────────

    /// <summary>학년도를 외부에서 강제 지정</summary>
    public void SetYear(int year)
    {
        if (!ShowYear) return;
        _updating = true;
        try { SelectByTag(CBoxYear, year); }
        finally { _updating = false; }
        RaiseChanged();
    }

    /// <summary>학기를 외부에서 강제 지정</summary>
    public void SetSemester(int semester)
    {
        if (!ShowSemester) return;
        _updating = true;
        try { SelectByTag(CBoxSemester, semester); }
        finally { _updating = false; }
        RaiseChanged();
    }

    // ── 헬퍼 ────────────────────────────────────────────

    private static int GetTag(ComboBox cb)
    {
        if (cb.SelectedItem is ComboBoxItem ci && ci.Tag is int v) return v;
        return 0;
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

/// <summary>학년도·학기 변경 이벤트 인자</summary>
public sealed class YearSemesterChangedEventArgs : EventArgs
{
    public int Year { get; init; }
    public int Semester { get; init; }
}
