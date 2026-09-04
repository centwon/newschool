using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NewSchool.Models;
using NewSchool.Repositories;
using NewSchool.Services;

namespace NewSchool.Dialogs;

/// <summary>
/// 동아리 부원 관리 다이얼로그
/// Club에 학생을 등록/해제
/// </summary>
public sealed partial class ClubEnrollmentDialog : ContentDialog
{
    #region Fields

    private readonly Club _club;
    private readonly List<Enrollment> _allStudents = new();
    private readonly List<ClubEnrollment> _originalEnrollments = new();

    /// <summary>추가할 학생 목록</summary>
    private readonly HashSet<string> _toAdd = new();

    /// <summary>제거할 학생 목록</summary>
    private readonly HashSet<string> _toRemove = new();

    #endregion

    #region Constructor

    public ClubEnrollmentDialog(Club club)
    {
        this.InitializeComponent();
        _club = club;

        Title = $"부원 관리 - {club.ClubName}";
    }

    #endregion

    #region Initialization

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        InitializeClubInfo();
        await LoadDataAsync();
    }

    /// <summary>
    /// 동아리 정보 표시
    /// </summary>
    private void InitializeClubInfo()
    {
        TxtClubName.Text = _club.ClubName;
        TxtActivityRoom.Text = string.IsNullOrEmpty(_club.ActivityRoom) 
            ? "" 
            : $"📍 {_club.ActivityRoom}";
    }

    /// <summary>
    /// 학년·반 필터를 <b>불러온 명부에서</b> 만든다.
    ///
    /// <para>예전에는 학년을 1~3, 반을 1~15 로 적어 두었다. 학년은 중·고등학교라 맞았지만
    /// <b>반은 늘 15개가 나왔다</b> — 3반까지인 학교에서도 4~15반을 고를 수 있었고, 골라도
    /// 목록은 비었다. 명부에 있는 반만 세워 두면 그 일이 없다.</para>
    ///
    /// <para>같은 필터를 <see cref="CourseEnrollmentDialog"/> 도 이 방식으로 만든다
    /// (<c>RefreshClassFilter</c>). 한쪽만 적어 두면 두 화면이 다른 목록을 보인다.</para>
    ///
    /// <para>⚠ 명부를 읽은 <b>뒤에</b> 불러야 한다.</para>
    /// </summary>
    private void BuildFiltersFromRoster()
    {
        CBoxGradeFilter.ItemsSource = BuildFilterItems(
            _allStudents.Select(s => s.Grade), g => $"{g}학년");
        CBoxGradeFilter.SelectedIndex = 0;

        CBoxClassFilter.ItemsSource = BuildFilterItems(
            _allStudents.Select(s => s.Class), c => $"{c}반");
        CBoxClassFilter.SelectedIndex = 0;
    }

    /// <summary>"전체" 를 맨 앞에 두고, 실제로 있는 값만 오름차순으로 잇는다.</summary>
    private static List<ComboBoxItem> BuildFilterItems(
        IEnumerable<int> values, Func<int, string> label)
    {
        var items = new List<ComboBoxItem>
        {
            new ComboBoxItem { Content = "전체", Tag = "0" }
        };

        foreach (int v in values.Where(v => v > 0).Distinct().OrderBy(v => v))
        {
            items.Add(new ComboBoxItem { Content = label(v), Tag = v.ToString() });
        }

        return items;
    }

    /// <summary>
    /// 데이터 로드
    /// </summary>
    private async Task LoadDataAsync()
    {
        try
        {
            // 1. 학교 전체 학생 로드
            await LoadAllStudentsAsync();

            // 2. 기존 부원 등록 로드
            await LoadEnrollmentsAsync();

            // 3. 필터는 명부에서 만든다 — 그래서 읽은 뒤여야 한다
            BuildFiltersFromRoster();

            // 4. 목록 갱신
            RefreshLists();
        }
        catch (Exception ex)
        {
            NewSchool.Logging.Log.Error("ClubEnrollmentDialog", "동아리·학생 자료를 읽지 못했다", ex);
        }
    }

    /// <summary>
    /// 그 해 재적자를 전부 읽는다. 동아리는 학년을 가리지 않는다.
    ///
    /// <para>학년·반을 넘기지 않으면 <c>GetEnrollmentsAsync</c> 가 그 조건을 아예 걸지
    /// 않아 <b>조회 한 번</b>으로 끝난다. 예전에는 <c>1~3학년 × 1~15반</c> 을 돌며
    /// <b>45번</b>을 물었다. 결과는 같았지만, 없는 학급까지 묻느라 대부분이 헛물이었다.</para>
    ///
    /// <para>더 나빴던 것은 그 루프의 빈 <c>catch</c> 다. "학급에 학생이 없으면 무시" 라고
    /// 적혀 있었지만 <b>모든</b> 예외를 삼켜서, DB 가 잠겨 조회가 진짜로 실패해도 학생이
    /// 조금 모자란 목록과 구별되지 않았다. 조회가 하나뿐이면 실패는 실패로 드러난다
    /// (<c>LoadDataAsync</c> 의 <c>catch</c> 가 받는다).</para>
    /// </summary>
    private async Task LoadAllStudentsAsync()
    {
        _allStudents.Clear();

        using var enrollmentService = new EnrollmentService();

        var roster = await enrollmentService.GetEnrollmentsAsync(
            Settings.SchoolCode.Value, _club.Year);
        _allStudents.AddRange(roster);

        Debug.WriteLine($"[ClubEnrollmentDialog] 전체 학생 로드: {_allStudents.Count}명");
    }

    /// <summary>
    /// 기존 부원 등록 로드
    /// </summary>
    private async Task LoadEnrollmentsAsync()
    {
        _originalEnrollments.Clear();

        using var repo = new ClubEnrollmentRepository(SchoolDatabase.DbPath);
        var enrollments = await repo.GetByClubAsync(_club.No);

        foreach (var e in enrollments)
        {
            _originalEnrollments.Add(e);
        }

        Debug.WriteLine($"[ClubEnrollmentDialog] 기존 등록: {_originalEnrollments.Count}명");
    }

    #endregion

    #region List Management

    /// <summary>
    /// 목록 갱신
    /// </summary>
    private void RefreshLists()
    {
        // 등록된 학생 ID 목록 계산
        var enrolledIds = _originalEnrollments
            .Select(e => e.StudentID)
            .Union(_toAdd)
            .Except(_toRemove)
            .ToHashSet();

        // 필터 조건
        int filterGrade = GetSelectedGrade();
        int filterClass = GetSelectedClass();
        string searchText = TxtSearch.Text?.Trim().ToLower() ?? "";

        // 등록 가능한 학생 (필터 + 미등록)
        var availableFiltered = _allStudents
            .Where(s => !enrolledIds.Contains(s.StudentID))
            .Where(s => filterGrade == 0 || s.Grade == filterGrade)
            .Where(s => filterClass == 0 || s.Class == filterClass)
            .OrderBy(s => s.Grade)
            .ThenBy(s => s.Class)
            .ThenBy(s => s.Number)
            .ToList();

        ListAvailable.LoadStudents(availableFiltered);

        // 등록된 부원 (검색 필터)
        var enrolledFiltered = _allStudents
            .Where(s => enrolledIds.Contains(s.StudentID))
            .Where(s => string.IsNullOrEmpty(searchText) || s.Name.ToLower().Contains(searchText))
            .OrderBy(s => s.Grade)
            .ThenBy(s => s.Class)
            .ThenBy(s => s.Number)
            .ToList();

        ListEnrolled.LoadStudents(enrolledFiltered);

        // UI 업데이트
        UpdateCounts();
    }

    /// <summary>
    /// 카운트 업데이트
    /// </summary>
    private void UpdateCounts()
    {
        TxtAvailableCount.Text = $"({ListAvailable.Students.Count}명)";
        TxtRegisteredCount.Text = $"({ListEnrolled.Students.Count}명)";
        TxtEnrolledCount.Text = $"부원: {ListEnrolled.Students.Count}명";
    }

    private int GetSelectedGrade()
    {
        if (CBoxGradeFilter.SelectedItem is ComboBoxItem item && item.Tag != null)
        {
            return int.Parse(item.Tag.ToString()!);
        }
        return 0;
    }

    private int GetSelectedClass()
    {
        if (CBoxClassFilter.SelectedItem is ComboBoxItem item && item.Tag != null)
        {
            return int.Parse(item.Tag.ToString()!);
        }
        return 0;
    }

    #endregion

    #region Event Handlers - Filters

    private void CBoxGradeFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ListAvailable != null)
        {
            RefreshLists();
        }
    }

    private void CBoxClassFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ListAvailable != null)
        {
            RefreshLists();
        }
    }

    private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        RefreshLists();
    }

    #endregion

    #region Event Handlers - Add/Remove

    /// <summary>
    /// 선택한 학생 등록
    /// </summary>
    private void BtnAdd_Click(object sender, RoutedEventArgs e)
    {
        var selected = ListAvailable.GetSelectedStudents().ToList();

        if (selected.Count == 0)
        {
            return;
        }

        foreach (var student in selected)
        {
            // 제거 목록에 있으면 제거
            _toRemove.Remove(student.StudentID);

            // 원래 등록되지 않았으면 추가 목록에
            if (!_originalEnrollments.Any(e => e.StudentID == student.StudentID))
            {
                _toAdd.Add(student.StudentID);
            }
        }

        ListAvailable.DeselectAll();
        RefreshLists();
    }

    /// <summary>
    /// 선택한 학생 등록 해제
    /// </summary>
    private void BtnRemove_Click(object sender, RoutedEventArgs e)
    {
        var selected = ListEnrolled.GetSelectedStudents().ToList();

        if (selected.Count == 0)
        {
            return;
        }

        foreach (var student in selected)
        {
            // 추가 목록에 있으면 제거
            _toAdd.Remove(student.StudentID);

            // 원래 등록되어 있었으면 제거 목록에
            if (_originalEnrollments.Any(e => e.StudentID == student.StudentID))
            {
                _toRemove.Add(student.StudentID);
            }
        }

        ListEnrolled.DeselectAll();
        RefreshLists();
    }

    /// <summary>
    /// InfoBar 메시지 표시
    /// </summary>
    private void ShowInfo(string message, InfoBarSeverity severity = InfoBarSeverity.Informational)
    {
        InfoMessage.Message = message;
        InfoMessage.Severity = severity;
        InfoMessage.IsOpen = true;
    }

    #endregion

    #region Save

    /// <summary>
    /// 저장 버튼
    /// </summary>
    private async void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var deferral = args.GetDeferral();

        try
        {
            using var repo = new ClubEnrollmentRepository(SchoolDatabase.DbPath);

            // 추가와 해제를 단일 트랜잭션으로 묶는다. 예전에는 건별 자동커밋인 데다
            // 결과도 확인하지 않아, 중간에 실패하면 "일부만 반영된 채 저장 완료"가 됐다.
            repo.BeginTransaction();
            try
            {
                // 1. 새로 등록
                //
                // 동아리 배정은 학생이 아니라 그 해 학적을 가리킨다. 후보 목록
                // (_allStudents)이 이미 학적이라 거기서 번호를 찾는다 — 못 찾으면 그
                // 학생은 이 학년도 명부에 없다는 뜻이므로 저장하지 않고 알린다.
                foreach (var studentId in _toAdd)
                {
                    var target = _allStudents.FirstOrDefault(s => s.StudentID == studentId);
                    if (target == null || target.No <= 0)
                        throw new InvalidOperationException($"학생 {studentId} 의 학적을 찾지 못했습니다.");

                    var enrollment = new ClubEnrollment
                    {
                        EnrollmentNo = target.No,
                        ClubNo = _club.No,
                        Status = ClubEnrollmentStatus.Active
                    };
                    if (await repo.CreateAsync(enrollment) <= 0)
                        throw new InvalidOperationException($"부원 추가에 실패했습니다: {studentId}");
                }

                // 2. 등록 해제
                foreach (var studentId in _toRemove)
                {
                    var original = _originalEnrollments.FirstOrDefault(e => e.StudentID == studentId);
                    if (original == null) continue;

                    if (!await repo.DeleteAsync(original.No))
                        throw new InvalidOperationException($"부원 해제에 실패했습니다: {studentId}");
                }

                repo.Commit();
            }
            catch
            {
                repo.Rollback();
                throw;
            }

            Debug.WriteLine($"[ClubEnrollmentDialog] 저장 완료 - 추가: {_toAdd.Count}, 제거: {_toRemove.Count}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ClubEnrollmentDialog] 저장 실패: {ex.Message}");
            ShowInfo($"저장 중 오류가 발생했습니다: {ex.Message}", InfoBarSeverity.Error);
            args.Cancel = true;
        }
        finally
        {
            deferral.Complete();
        }
    }

    #endregion
}
