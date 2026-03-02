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

namespace NewSchool.Dialogs
{
    /// <summary>
    /// 수강생 관리 다이얼로그
    /// Course에 학생을 등록/해제
    /// ListStudent 컨트롤 사용
    /// </summary>
    public sealed partial class CourseEnrollmentDialog : ContentDialog
    {
        #region Fields

        private readonly Course _course;
        private readonly List<Enrollment> _allStudents = new();
        private readonly List<CourseEnrollment> _originalEnrollments = new();

        /// <summary>추가할 학생 목록</summary>
        private readonly HashSet<string> _toAdd = new();

        /// <summary>제거할 학생 목록</summary>
        private readonly HashSet<string> _toRemove = new();

        #endregion

        #region Constructor

        public CourseEnrollmentDialog(Course course)
        {
            this.InitializeComponent();
            _course = course;

            Title = $"수강생 관리 - {course.Subject}";
        }

        #endregion

        #region Initialization

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            InitializeCourseInfo();
            InitializeFilters();
            await LoadDataAsync();
        }

        /// <summary>
        /// 과목 정보 표시
        /// </summary>
        private void InitializeCourseInfo()
        {
            TxtSubject.Text = _course.Subject;
            TxtType.Text = _course.TypeDisplay;
            TxtGradeInfo.Text = $"{_course.Grade}학년";
        }

        /// <summary>
        /// 필터 초기화 (Type별 설정)
        /// </summary>
        private void InitializeFilters()
        {
            // 반 필터 초기화
            var classItems = new List<ComboBoxItem>
            {
                new ComboBoxItem { Content = "전체", Tag = "0" }
            };
            for (int i = 1; i <= 15; i++)
            {
                classItems.Add(new ComboBoxItem { Content = $"{i}반", Tag = i.ToString() });
            }
            CBoxClassFilter.ItemsSource = classItems;
            CBoxClassFilter.SelectedIndex = 0;

            // 강의실 필터 초기화
            var roomItems = new List<ComboBoxItem>
            {
                new ComboBoxItem { Content = "전체", Tag = "" }
            };
            foreach (var room in _course.RoomList)
            {
                roomItems.Add(new ComboBoxItem { Content = room, Tag = room });
            }
            CBoxRoomFilter.ItemsSource = roomItems;
            CBoxRoomFilter.SelectedIndex = 0;

            // 강의실이 없으면 콤보박스 숨김
            CBoxRoomFilter.Visibility = _course.RoomList.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

            // Type에 따른 필터 제한
            switch (_course.EffectiveType)
            {
                case "Class":
                case "Selective":
                    // 학년 고정
                    CBoxGradeFilter.SelectedIndex = _course.Grade; // 1학년=1, 2학년=2, 3학년=3
                    CBoxGradeFilter.IsEnabled = false;
                    break;

                case "Club":
                    // 전 학년 선택 가능
                    CBoxGradeFilter.SelectedIndex = 0; // 전체
                    CBoxGradeFilter.IsEnabled = true;
                    break;
            }

            // 학급 일괄 등록 버튼 (Class 유형만)
            BulkEnrollPanel.Visibility = _course.IsClassType ? Visibility.Visible : Visibility.Collapsed;
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

                // 2. 기존 수강 등록 로드
                await LoadEnrollmentsAsync();

                // 3. 목록 갱신
                RefreshLists();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CourseEnrollmentDialog] 데이터 로드 실패: {ex.Message}");
            }
        }

        /// <summary>
        /// 학교 전체 학생 로드
        /// </summary>
        private async Task LoadAllStudentsAsync()
        {
            _allStudents.Clear();

            using var enrollmentService = new EnrollmentService();

            // Type에 따라 로드 범위 결정
            int gradeStart = 1, gradeEnd = 3;
            if (_course.EffectiveType != "Club")
            {
                gradeStart = _course.Grade;
                gradeEnd = _course.Grade;
            }

            for (int grade = gradeStart; grade <= gradeEnd; grade++)
            {
                for (int classNum = 1; classNum <= 15; classNum++)
                {
                    try
                    {
                        var roster = await enrollmentService.GetClassRosterAsync(
                            Settings.SchoolCode.Value,
                            Settings.WorkYear.Value,
                            grade,
                            classNum
                        );

                        _allStudents.AddRange(roster);
                    }
                    catch
                    {
                        // 해당 학급에 학생이 없으면 무시
                    }
                }
            }

            Debug.WriteLine($"[CourseEnrollmentDialog] 전체 학생 로드: {_allStudents.Count}명");
        }

        /// <summary>
        /// 기존 수강 등록 로드
        /// </summary>
        private async Task LoadEnrollmentsAsync()
        {
            _originalEnrollments.Clear();

            using var repo = new CourseEnrollmentRepository(SchoolDatabase.DbPath);
            var enrollments = await repo.GetByCourseAsync(_course.No);

            foreach (var e in enrollments)
            {
                _originalEnrollments.Add(e);
            }

            Debug.WriteLine($"[CourseEnrollmentDialog] 기존 등록: {_originalEnrollments.Count}명");
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

            // 등록된 학생 (검색 필터)
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
            TxtEnrolledCount.Text = $"등록: {ListEnrolled.Students.Count}명";
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

        private string GetSelectedRoom()
        {
            if (CBoxRoomFilter.SelectedItem is ComboBoxItem item && item.Tag != null)
            {
                return item.Tag.ToString() ?? "";
            }
            return "";
        }

        #endregion

        #region Event Handlers - Filters

        private void CBoxGradeFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ListAvailable != null) // 초기화 완료 후에만
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

        private void CBoxRoomFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // 강의실 필터 변경 시 (현재는 UI용으로만 사용)
            // 필요시 RefreshLists() 호출
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
        /// 학급 일괄 등록
        /// </summary>
        private void BtnBulkEnroll_Click(object sender, RoutedEventArgs e)
        {
            int filterClass = GetSelectedClass();
            if (filterClass == 0)
            {
                ShowInfo("일괄 등록할 반을 선택해주세요.", InfoBarSeverity.Warning);
                return;
            }

            // 현재 필터된 모든 학생 등록
            int count = 0;
            foreach (var student in ListAvailable.Students.ToList())
            {
                _toRemove.Remove(student.StudentID);

                if (!_originalEnrollments.Any(e => e.StudentID == student.StudentID))
                {
                    _toAdd.Add(student.StudentID);
                    count++;
                }
            }

            RefreshLists();

            if (count > 0)
            {
                ShowInfo($"{count}명이 등록 대기열에 추가되었습니다.", InfoBarSeverity.Success);
            }
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
                using var repo = new CourseEnrollmentRepository(SchoolDatabase.DbPath);

                // 1. 새로 등록
                foreach (var studentId in _toAdd)
                {
                    var enrollment = new CourseEnrollment
                    {
                        StudentID = studentId,
                        CourseNo = _course.No,
                        Status = "수강중"
                    };
                    await repo.CreateAsync(enrollment);
                }

                // 2. 등록 해제
                foreach (var studentId in _toRemove)
                {
                    var original = _originalEnrollments.FirstOrDefault(e => e.StudentID == studentId);
                    if (original != null)
                    {
                        await repo.DeleteAsync(original.No);
                    }
                }

                Debug.WriteLine($"[CourseEnrollmentDialog] 저장 완료 - 추가: {_toAdd.Count}, 제거: {_toRemove.Count}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CourseEnrollmentDialog] 저장 실패: {ex.Message}");
                args.Cancel = true;
            }
            finally
            {
                deferral.Complete();
            }
        }

        #endregion
    }
}
