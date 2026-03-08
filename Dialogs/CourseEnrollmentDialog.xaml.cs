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
    /// 좌측: 학급별 학생 필터 (실제 학생 데이터 기반)
    /// 우측: 강의실별 수강생
    /// 일괄 배치: 학급 공통 → 전체 학생 자동 배정 (Room = "학년-반")
    /// </summary>
    public sealed partial class CourseEnrollmentDialog : ContentDialog
    {
        #region Fields

        private readonly Course _course;
        private readonly List<Enrollment> _allStudents = new();
        private readonly List<CourseEnrollment> _originalEnrollments = new();

        /// <summary>추가할 학생 (StudentID → Room)</summary>
        private readonly Dictionary<string, string> _toAdd = new();

        /// <summary>제거할 학생 목록</summary>
        private readonly HashSet<string> _toRemove = new();

        /// <summary>등록된 학생의 강의실 배정 (StudentID → Room)</summary>
        private readonly Dictionary<string, string> _enrolledRooms = new();

        private bool _isLoaded = false;

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
            await LoadDataAsync();

            // 데이터 로드 후 필터 초기화 (_allStudents 기반)
            InitializeLeftFilters();
            InitializeRightFilters();
            InitializeBulkAssign();

            _isLoaded = true;
            RefreshLists();
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
        /// 좌측 필터 초기화 — _allStudents 기반으로 실제 학년/반 목록 구성
        /// </summary>
        private void InitializeLeftFilters()
        {
            // 학년 필터
            var grades = _allStudents.Select(s => s.Grade).Distinct().OrderBy(g => g).ToList();
            var gradeItems = new List<ComboBoxItem>();

            if (_course.EffectiveType == CourseTypes.Club || grades.Count > 1)
            {
                gradeItems.Add(new ComboBoxItem { Content = "전체", Tag = 0 });
            }

            foreach (var g in grades)
            {
                gradeItems.Add(new ComboBoxItem { Content = $"{g}학년", Tag = g });
            }

            CBoxGradeFilter.ItemsSource = gradeItems;

            // 기본 선택: 과목 학년 (Club이면 전체)
            if (_course.EffectiveType == CourseTypes.Club)
            {
                CBoxGradeFilter.SelectedIndex = 0; // 전체
            }
            else
            {
                // 해당 학년 선택
                var matchIdx = gradeItems.FindIndex(i => i.Tag is int t && t == _course.Grade);
                CBoxGradeFilter.SelectedIndex = matchIdx >= 0 ? matchIdx : 0;
            }

            // 반 필터는 학년 선택 시 갱신됨
            RefreshClassFilter();
        }

        /// <summary>
        /// 반 필터 갱신 — 선택된 학년의 실제 반 목록
        /// </summary>
        private void RefreshClassFilter()
        {
            int selectedGrade = GetFilterGrade();

            var classes = _allStudents
                .Where(s => selectedGrade == 0 || s.Grade == selectedGrade)
                .Select(s => s.Class)
                .Distinct()
                .OrderBy(c => c)
                .ToList();

            var classItems = new List<ComboBoxItem>
            {
                new ComboBoxItem { Content = "전체", Tag = 0 }
            };

            foreach (var c in classes)
            {
                classItems.Add(new ComboBoxItem { Content = $"{c}반", Tag = c });
            }

            CBoxClassFilter.ItemsSource = classItems;
            CBoxClassFilter.SelectedIndex = 0;
        }

        /// <summary>
        /// 우측 필터 초기화 — 강의실 + 검색
        /// </summary>
        private void InitializeRightFilters()
        {
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
        }

        /// <summary>
        /// 일괄 배치 초기화 — 학급 공통일 때만 표시
        /// </summary>
        private void InitializeBulkAssign()
        {
            if (_course.IsClassType)
            {
                BulkAssignPanel.Visibility = Visibility.Visible;

                // 실제 반 목록으로 설명 텍스트 구성
                var classes = _allStudents
                    .Where(s => s.Grade == _course.Grade)
                    .Select(s => s.Class)
                    .Distinct()
                    .OrderBy(c => c)
                    .ToList();

                if (classes.Count > 0)
                {
                    var examples = classes.Take(3)
                        .Select(c => $"{_course.Grade}-{c}")
                        .ToList();
                    string exampleText = string.Join(", ", examples);
                    if (classes.Count > 3) exampleText += ", ...";

                    TxtBulkDescription.Text = $"{_course.Grade}학년 전체 학생을 학급별 강의실로 배정 (예: {exampleText})";
                }
                else
                {
                    TxtBulkDescription.Text = "배정할 학생이 없습니다.";
                }
            }
            else
            {
                BulkAssignPanel.Visibility = Visibility.Collapsed;
            }
        }

        #endregion

        #region Data Loading

        /// <summary>
        /// 데이터 로드
        /// </summary>
        private async Task LoadDataAsync()
        {
            try
            {
                await LoadAllStudentsAsync();
                await LoadEnrollmentsAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CourseEnrollmentDialog] 데이터 로드 실패: {ex.Message}");
            }
        }

        /// <summary>
        /// 학생 로드 (학년 단위 조회)
        /// </summary>
        private async Task LoadAllStudentsAsync()
        {
            _allStudents.Clear();

            using var enrollmentService = new EnrollmentService();

            if (_course.EffectiveType == CourseTypes.Club)
            {
                for (int grade = 1; grade <= 3; grade++)
                {
                    var students = await enrollmentService.GetEnrollmentsAsync(
                        Settings.SchoolCode.Value, Settings.WorkYear.Value, 0, grade);
                    _allStudents.AddRange(students);
                }
            }
            else
            {
                var students = await enrollmentService.GetEnrollmentsAsync(
                    Settings.SchoolCode.Value, Settings.WorkYear.Value, 0, _course.Grade);
                _allStudents.AddRange(students);
            }

            Debug.WriteLine($"[CourseEnrollmentDialog] 전체 학생 로드: {_allStudents.Count}명");
        }

        /// <summary>
        /// 기존 수강 등록 로드
        /// </summary>
        private async Task LoadEnrollmentsAsync()
        {
            _originalEnrollments.Clear();
            _enrolledRooms.Clear();

            using var repo = new CourseEnrollmentRepository(SchoolDatabase.DbPath);
            var enrollments = await repo.GetByCourseAsync(_course.No);

            foreach (var e in enrollments)
            {
                _originalEnrollments.Add(e);
                _enrolledRooms[e.StudentID] = e.Room ?? string.Empty;
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
            var enrolledIds = _enrolledRooms.Keys
                .Union(_toAdd.Keys)
                .Except(_toRemove)
                .ToHashSet();

            // 좌측 필터 조건
            int filterGrade = GetFilterGrade();
            int filterClass = GetFilterClass();

            // 우측 필터 조건
            string filterRoom = GetSelectedRoom();
            string searchText = TxtSearch?.Text?.Trim().ToLower() ?? "";

            // 좌측: 등록 가능한 학생 (필터 + 미등록)
            var availableFiltered = _allStudents
                .Where(s => !enrolledIds.Contains(s.StudentID))
                .Where(s => filterGrade == 0 || s.Grade == filterGrade)
                .Where(s => filterClass == 0 || s.Class == filterClass)
                .OrderBy(s => s.Grade)
                .ThenBy(s => s.Class)
                .ThenBy(s => s.Number)
                .ToList();

            ListAvailable.LoadStudents(availableFiltered);

            // 우측: 등록된 학생 (강의실/검색 필터)
            var enrolledFiltered = _allStudents
                .Where(s => enrolledIds.Contains(s.StudentID))
                .Where(s =>
                {
                    if (string.IsNullOrEmpty(filterRoom)) return true;
                    string studentRoom = GetStudentRoom(s.StudentID);
                    return studentRoom == filterRoom;
                })
                .Where(s => string.IsNullOrEmpty(searchText) || s.Name.ToLower().Contains(searchText))
                .OrderBy(s => s.Grade)
                .ThenBy(s => s.Class)
                .ThenBy(s => s.Number)
                .ToList();

            ListEnrolled.LoadStudents(enrolledFiltered);

            // UI 업데이트
            UpdateCounts(enrolledIds.Count);
        }

        /// <summary>
        /// 학생의 배정 강의실 조회
        /// </summary>
        private string GetStudentRoom(string studentId)
        {
            if (_toAdd.TryGetValue(studentId, out var addRoom))
                return addRoom;
            if (_enrolledRooms.TryGetValue(studentId, out var room))
                return room;
            return string.Empty;
        }

        /// <summary>
        /// 카운트 업데이트
        /// </summary>
        private void UpdateCounts(int totalEnrolled)
        {
            TxtAvailableCount.Text = $"({ListAvailable.Students.Count}명)";
            TxtRegisteredCount.Text = $"({ListEnrolled.Students.Count}명)";
            TxtEnrolledCount.Text = $"등록: {totalEnrolled}명";
        }

        private int GetFilterGrade()
        {
            if (CBoxGradeFilter.SelectedItem is ComboBoxItem item && item.Tag is int grade)
                return grade;
            return 0;
        }

        private int GetFilterClass()
        {
            if (CBoxClassFilter.SelectedItem is ComboBoxItem item && item.Tag is int classNo)
                return classNo;
            return 0;
        }

        private string GetSelectedRoom()
        {
            if (CBoxRoomFilter.SelectedItem is ComboBoxItem item && item.Tag != null)
                return item.Tag.ToString() ?? "";
            return "";
        }

        #endregion

        #region Event Handlers - Filters

        private void CBoxGradeFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isLoaded) return;

            RefreshClassFilter();
            RefreshLists();
        }

        private void CBoxClassFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoaded)
            {
                RefreshLists();
            }
        }

        private void CBoxRoomFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoaded && ListEnrolled != null)
            {
                RefreshLists();
            }
        }

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isLoaded)
            {
                RefreshLists();
            }
        }

        #endregion

        #region Event Handlers - Add/Remove

        /// <summary>
        /// 선택한 학생 등록 (우측 강의실 필터 값으로 배정)
        /// </summary>
        private void BtnAdd_Click(object sender, RoutedEventArgs e)
        {
            var selected = ListAvailable.GetSelectedStudents().ToList();
            if (selected.Count == 0) return;

            string targetRoom = GetSelectedRoom();

            foreach (var student in selected)
            {
                _toRemove.Remove(student.StudentID);

                if (_originalEnrollments.Any(oe => oe.StudentID == student.StudentID))
                {
                    // 원래 등록되어 있던 학생 → 제거 목록에서만 빼면 복원됨
                }
                else
                {
                    _toAdd[student.StudentID] = targetRoom;
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
            if (selected.Count == 0) return;

            foreach (var student in selected)
            {
                _toAdd.Remove(student.StudentID);

                if (_originalEnrollments.Any(oe => oe.StudentID == student.StudentID))
                {
                    _toRemove.Add(student.StudentID);
                }
            }

            ListEnrolled.DeselectAll();
            RefreshLists();
        }

        /// <summary>
        /// 전체 학생 일괄 배치 (학급 공통 전용)
        /// 학년의 모든 학생을 등록하고, Room = "{학년}-{반}" 형태로 자동 배정
        /// </summary>
        private void BtnBulkAssign_Click(object sender, RoutedEventArgs e)
        {
            int targetGrade = _course.Grade;

            // 해당 학년의 모든 학생
            var gradeStudents = _allStudents
                .Where(s => s.Grade == targetGrade)
                .ToList();

            if (gradeStudents.Count == 0)
            {
                ShowInfo($"{targetGrade}학년에 학생이 없습니다.", InfoBarSeverity.Warning);
                return;
            }

            int newCount = 0;
            var classSet = new HashSet<int>();

            foreach (var student in gradeStudents)
            {
                // Room = "학년-반" (예: 3-1, 3-2)
                string room = $"{student.Grade}-{student.Class}";
                classSet.Add(student.Class);

                _toRemove.Remove(student.StudentID);

                if (_originalEnrollments.Any(oe => oe.StudentID == student.StudentID))
                {
                    // 기존 등록 학생 → 강의실만 변경
                    _enrolledRooms[student.StudentID] = room;
                }
                else
                {
                    _toAdd[student.StudentID] = room;
                    newCount++;
                }
            }

            RefreshLists();

            var classNums = classSet.OrderBy(c => c).Select(c => $"{targetGrade}-{c}");
            string roomList = string.Join(", ", classNums);

            ShowInfo($"{targetGrade}학년 전체 {gradeStudents.Count}명 배치 완료 ({roomList}) — 신규: {newCount}명", InfoBarSeverity.Success);
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

                // 1. 새로 등록 (Room 포함)
                foreach (var kvp in _toAdd)
                {
                    var enrollment = new CourseEnrollment
                    {
                        StudentID = kvp.Key,
                        CourseNo = _course.No,
                        Status = CourseEnrollmentStatus.Active,
                        Room = kvp.Value
                    };
                    await repo.CreateAsync(enrollment);
                }

                // 2. 등록 해제
                foreach (var studentId in _toRemove)
                {
                    var original = _originalEnrollments.FirstOrDefault(oe => oe.StudentID == studentId);
                    if (original != null)
                    {
                        await repo.DeleteAsync(original.No);
                    }
                }

                // 3. 강의실 변경 (기존 등록 학생)
                foreach (var original in _originalEnrollments)
                {
                    if (_toRemove.Contains(original.StudentID)) continue;

                    if (_enrolledRooms.TryGetValue(original.StudentID, out var newRoom))
                    {
                        if (newRoom != (original.Room ?? string.Empty))
                        {
                            original.Room = newRoom;
                            original.UpdatedAt = DateTime.Now;
                            await repo.UpdateAsync(original);
                        }
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
