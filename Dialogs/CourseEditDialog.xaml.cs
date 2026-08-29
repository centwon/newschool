using System;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NewSchool.Controls;
using NewSchool.Models;
using NewSchool.Repositories;
using NewSchool.Services;

namespace NewSchool.Dialogs;

/// <summary>
/// 수업 정보 편집 다이얼로그
/// </summary>
public sealed partial class CourseEditDialog : ContentDialog
{
    private Course? _course;
    private readonly string _schoolCode;
    private readonly string _teacherId;
    private readonly int _year;
    private readonly int _semester;
    private readonly bool _isEdit;
    private InfoBar? _errorInfoBar;

    /// <summary>
    /// 강의실 칸이 잠겼는가. 잠겨 있으면 학년·유형 콤보도 이 칸을 건드리지 못한다.
    /// </summary>
    private bool _roomsLocked;

    /// <summary>
    /// 사람이 [강의실 다시 정하기] 로 초기화에 동의했는가. 저장할 때 실제로 지운다 —
    /// 여기서 바로 지우면 다이얼로그를 취소해도 기록이 사라진다.
    /// </summary>
    private bool _roomResetConfirmed;

    /// <summary>잠글지 판단할 때 쓴 원래 목록. 저장 시 "정말 바뀌었나" 를 이것과 견준다.</summary>
    private string _originalRooms = string.Empty;

    /// <summary>
    /// 새 수업 추가
    /// </summary>
    public CourseEditDialog(string schoolCode, string teacherId, int year, int semester)
    {
        this.InitializeComponent();
        
        _schoolCode = schoolCode;
        _teacherId = teacherId;
        _year = year;
        _semester = semester;
        _isEdit = false;

        Title = "수업 추가";
        InitializeErrorInfoBar();
    }

    /// <summary>
    /// 기존 수업 수정
    /// </summary>
    public CourseEditDialog(Course course)
    {
        this.InitializeComponent();
        
        _course = course;
        _schoolCode = course.SchoolCode;
        _teacherId = course.TeacherID;
        _year = course.Year;
        _semester = course.Semester;
        _isEdit = true;

        Title = "수업 수정";
        InitializeErrorInfoBar();
        LoadCourseData();

        // 잠글지는 DB 를 봐야 알 수 있어(딸린 기록이 있는가) 생성자에서 못 한다.
        this.Loaded += OnLoadedApplyRoomLock;
    }

    private async void OnLoadedApplyRoomLock(object sender, RoutedEventArgs e)
    {
        this.Loaded -= OnLoadedApplyRoomLock;
        await ApplyRoomLockAsync();
    }

    /// <summary>
    /// <b>강의실은 정하고 나면 바뀌지 않는 것이 기본이다.</b> 딸린 기록이 하나라도 있으면
    /// 칸을 잠그고, 바꾸려면 [강의실 다시 정하기] 를 거치게 한다.
    ///
    /// <para>잠금이 경고보다 중요하다 — 학년·유형 콤보가 <c>TxtRooms.Text</c> 를 통째로
    /// 갈아치우던 사고 경로가 잠긴 동안에는 손댈 대상 자체를 잃는다.</para>
    ///
    /// <para>딸린 기록이 없으면 잠그지 않는다. 배치도 진도도 없는 새 수업까지 잠그면
    /// 초기 설정 중에 성가시기만 하다.</para>
    /// </summary>
    private async Task ApplyRoomLockAsync()
    {
        if (!_isEdit || _course == null) return;

        _originalRooms = _course.Rooms ?? string.Empty;

        CourseRoomReset.Impact impact;
        try
        {
            impact = await CourseRoomReset.MeasureAsync(SchoolDatabase.DbPath, _course.No);
        }
        catch (Exception ex)
        {
            // 세지 못하면 잠그지 않는다 — 못 고치게 막는 쪽이 더 나쁘다.
            Debug.WriteLine($"[CourseEditDialog] 강의실 영향 조사 실패: {ex.Message}");
            return;
        }

        if (!impact.HasAny) return;

        _roomsLocked = true;
        TxtRooms.IsEnabled = false;
        // 빠른 입력 버튼들은 숨긴다 — StackPanel 은 Control 이 아니라 IsEnabled 가 없고,
        // 잠긴 동안 눌리지 않는 버튼을 보여 둘 이유도 없다.
        RoomsQuickFillPanel.Visibility = Visibility.Collapsed;
        RoomsLockedPanel.Visibility = Visibility.Visible;
        TxtRoomsLockedNote.Text =
            $"이 강의실에 시간표 배치 {impact.Lessons}칸 · 시수 조정 {impact.WeeklyHours}건 · " +
            $"진도 {impact.Progress}건이 딸려 있어 잠갔습니다.";
    }

    /// <summary>
    /// 에러 InfoBar 초기화
    /// </summary>
    private void InitializeErrorInfoBar()
    {
        _errorInfoBar = new InfoBar
        {
            Severity = InfoBarSeverity.Error,
            IsOpen = false,
            IsClosable = true,
            Margin = new Thickness(0, 0, 0, 12)
        };
    }

    /// <summary>
    /// 수업 데이터 로드 (수정 모드)
    /// </summary>
    private void LoadCourseData()
    {
        if (_course == null) return;

        TxtSubject.Text = _course.Subject;
        CBoxGrade.SelectedIndex = _course.Grade - 1;
        NumUnit.Value = _course.Unit;
        
        // Type 선택
        for (int i = 0; i < CBoxType.Items.Count; i++)
        {
            var item = CBoxType.Items[i] as ComboBoxItem;
            if (item?.Tag?.ToString() == _course.Type)
            {
                CBoxType.SelectedIndex = i;
                break;
            }
        }

        TxtRooms.Text = _course.Rooms;
        UpdateRoomsPreview();

        TxtRemark.Text = _course.Remark;
    }

    /// <summary>
    /// Rooms 텍스트 변경 이벤트
    /// </summary>
    private void OnRoomsTextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateRoomsPreview();
    }

    /// <summary>
    /// Rooms 미리보기 업데이트
    /// </summary>
    private void UpdateRoomsPreview()
    {
        var roomsText = TxtRooms.Text.Trim();
        
        if (string.IsNullOrWhiteSpace(roomsText))
        {
            TxtRoomsPreview.Visibility = Visibility.Collapsed;
            return;
        }

        // 임시 Course 객체로 파싱 테스트
        var tempCourse = new Course { Rooms = roomsText };
        var roomList = tempCourse.RoomList;

        if (roomList.Count > 0)
        {
            TxtRoomsPreview.Text = $"📍 {string.Join(", ", roomList)}";
            TxtRoomsPreview.Visibility = Visibility.Visible;
        }
        else
        {
            TxtRoomsPreview.Visibility = Visibility.Collapsed;
        }
    }

    private async void CBoxGrade_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        await FillRoomsTemplateAsync();
    }

    /// <summary>
    /// 학년·유형에 맞는 강의실 <b>추천 목록</b>을 채운다.
    ///
    /// <para>⚠ 예전에는 대입이 <c>switch</c> <b>밖</b>에 있어서, 유형을 "선택 과목"이나
    /// "동아리" 로 바꾸면 <c>template</c> 이 빈 문자열인 채로 대입돼 <b>강의실 목록이 통째로
    /// 지워졌다.</b> 학년을 바꿔도 직접 적어 둔 "음악실" 같은 것이 자동 목록으로 갈아치워졌다.
    /// 교사는 시수나 비고를 고치러 들어왔다가 콤보를 한 번 건드렸을 뿐인데 그랬다.</para>
    ///
    /// <para>이제 <b>학급 공통일 때만</b>, 그리고 <b>잠기지 않았을 때만</b> 채운다.</para>
    /// </summary>
    private async Task FillRoomsTemplateAsync()
    {
        if (TxtRooms == null) return;

        // 잠긴 강의실은 무엇도 건드리지 않는다.
        if (_roomsLocked) return;

        var type = (CBoxType.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        if (type != CourseTypes.Class) return;   // 선택·동아리는 사람이 직접 적는다

        var grade = (CBoxGrade.SelectedItem as ComboBoxItem)?.Tag;
        if (grade == null || !int.TryParse(grade.ToString(), out int gradeInt)) return;

        TxtRooms.Text = await GetClassListFromEnrollmentAsync(gradeInt);
        UpdateRoomsPreview();
    }

    /// <summary>
    /// 강의실을 다시 정한다 — 무엇이 지워지는지 세어서 보이고, 확인하면 칸을 연다.
    /// 실제 삭제는 <b>저장할 때</b> 한다(취소하면 아무 일도 없어야 한다).
    /// </summary>
    private async void BtnResetRooms_Click(object sender, RoutedEventArgs e)
    {
        if (_course == null) return;

        CourseRoomReset.Impact impact;
        try
        {
            impact = await CourseRoomReset.MeasureAsync(SchoolDatabase.DbPath, _course.No);
        }
        catch (Exception ex)
        {
            await MessageBox.ShowErrorAsync("딸린 기록을 세지 못했습니다.", ex);
            return;
        }

        string message =
            $"강의실을 다시 정하면 이 수업의\n" +
            $"  · 시간표 배치 {impact.Lessons}칸\n" +
            $"  · 시수 조정 {impact.WeeklyHours}건\n" +
            $"  · 진도 {impact.Progress}건\n" +
            $"이 지워집니다.\n\n";

        if (impact.Enrollments > 0)
        {
            message +=
                $"학생 배정은 그대로 남지만, {impact.Enrollments}명의 강의실(분반) 지정은 비워집니다.\n" +
                (IsClassType()
                    ? "학급 공통 수업이라 배정 화면의 [일괄 배정] 한 번으로 되돌아옵니다.\n\n"
                    : "선택 과목·동아리는 분반을 다시 지정하셔야 합니다.\n\n");
        }

        message += "계속할까요?";

        if (!await MessageBox.ShowConfirmAsync(message, "강의실 다시 정하기", "계속", "취소"))
            return;

        _roomResetConfirmed = true;
        _roomsLocked = false;
        TxtRooms.IsEnabled = true;
        RoomsQuickFillPanel.Visibility = Visibility.Visible;
        RoomsLockedPanel.Visibility = Visibility.Collapsed;
        TxtRooms.Focus(FocusState.Programmatic);
    }

    private bool IsClassType() =>
        (CBoxType.SelectedItem as ComboBoxItem)?.Tag?.ToString() == CourseTypes.Class;
    private async Task<string> GetClassListFromEnrollmentAsync(int grade)
    {
        using var enrollservice = new EnrollmentService();
        var classList = await enrollservice.GetClassListAsync(_schoolCode, _year, grade);
        return string.Join(", ", classList.Select(c => $"{grade}-{c}"));

    }


    private async void CBoxType_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        await FillRoomsTemplateAsync();
    }


    /// <summary>
    /// 템플릿 버튼 클릭
    /// </summary>
    ///
    private void BtnWholeRooms_Click(object sender, RoutedEventArgs e)
    {
        var button = sender as Button;
        if (button == null) return;
        TxtRooms.Text = CombineGradeAndClassTemplate(button.Tag);
        UpdateRoomsPreview();
    }

    private void BtnSelectedRooms_Click(object sender, RoutedEventArgs e)
    {
        var button = sender as Button;
        if (button == null) return;
        TxtRooms.Text = CombineGradeAndClassTemplate(button.Tag);
        UpdateRoomsPreview();
    }

    private void BtnABC_Click(object sender, RoutedEventArgs e)
    {
        var button = sender as Button;
        if (button == null) return;
        var rooms = button.Tag as string;
        TxtRooms.Text = rooms;
        UpdateRoomsPreview();
    }

    private void BtnSpecialRooms_Click(object sender, RoutedEventArgs e)
    {
        var button = sender as Button;
        if (button == null) return;
        var rooms = button.Tag as string;
        TxtRooms.Text = rooms;
        UpdateRoomsPreview();
    }
    private string CombineGradeAndClassTemplate(object buttonTag)
    {
        if (buttonTag == null) return string.Empty;
        var tag = buttonTag as string;
        if (string.IsNullOrEmpty(tag)) return string.Empty; 
        var rooms = tag.Split(',').Select(t => t.Trim()).ToList();
        var grade = (CBoxGrade.SelectedItem as ComboBoxItem)?.Tag;

        return string.Join(", ", rooms.Select(r => $"{grade}-{r}"));
    }


    //private async Task OnRoomsTemplateClick(object sender, RoutedEventArgs e)
    //{
    //    if (_course == null) return;
    //    var btn = sender as Button;
    //    if (btn== null) return;
    //    /// btn.Tag 를 ',' 로 구분된 문자열로 넣어준다.
    //    var tags = btn?.Tag?.ToString();
    //    if (!string.IsNullOrEmpty(tags))
    //    {
    //        rooms = string.Join(", ", tags.Split(',').Select(t => t.Trim()));
    //    }

    //    ///  수업 유형이 Selective, Club 일 경우 Romms 템플릿을 이용한다.
    //    var template = string.Empty;
    //    if (_course.Type.Equals("class" ))
    //    {
    //        using var enrollservice = new EnrollmentService();
    //        var classList = await enrollservice.GetClassListAsync(_schoolCode, _year, _course.Grade);
    //        template = string.Join(", ", classList.Select(c => $"{_course.Grade}-{c}"));

    //    }
    //    else
    //    {
    //        var btn = sender as Button;
    //        if (btn != null && btn.Content is string temp)
    //        {
    //            template = temp;
    //        }
    //        else
    //        {

    //    }
    //    ///enrollment 에서 해당 학년의
    //    if (sender is Button button && button.Tag is string template)
    //    {
    //        TxtRooms.Text = template;
    //        UpdateRoomsPreview();
    //    }
    //}

    /// <summary>
    /// 에러 메시지 표시
    /// </summary>
    private void ShowError(string message)
    {
        if (_errorInfoBar != null && ErrorContainer != null)
        {
            _errorInfoBar.Message = message;
            _errorInfoBar.IsOpen = true;
            
            // ErrorContainer에 추가 (중복 추가 방지)
            if (!ErrorContainer.Children.Contains(_errorInfoBar))
            {
                ErrorContainer.Children.Insert(0, _errorInfoBar);
            }
        }
    }

    /// <summary>
    /// 에러 메시지 숨김
    /// </summary>
    private void HideError()
    {
        _errorInfoBar?.IsOpen = false;
    }

    /// <summary>
    /// 저장 버튼 클릭
    /// </summary>
    private async void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        // 유효성 검사를 위해 지연
        var deferral = args.GetDeferral();

        try
        {
            HideError(); // 이전 에러 메시지 숨김

            // 유효성 검사
            if (string.IsNullOrWhiteSpace(TxtSubject.Text))
            {
                ShowError("과목명을 입력해주세요.");
                args.Cancel = true;
                return;
            }

            if (CBoxGrade.SelectedItem == null)
            {
                ShowError("학년을 선택해주세요.");
                args.Cancel = true;
                return;
            }

            if (string.IsNullOrWhiteSpace(TxtRooms.Text))
            {
                ShowError("강의실을 입력해주세요.");
                args.Cancel = true;
                return;
            }

            // Course 객체 생성 또는 업데이트
            if (_isEdit && _course != null)
            {
                string newRooms = TxtRooms.Text.Trim();

                // 강의실이 정말 바뀌었을 때만 딸린 기록을 정리한다. 순서만 바꾼 것이나
                // 공백 차이는 같은 목록으로 본다 — 아무것도 지울 이유가 없다.
                bool roomsChanged =
                    _roomResetConfirmed && !CourseRoomReset.SameRooms(_originalRooms, newRooms);

                if (roomsChanged)
                {
                    try
                    {
                        var done = await CourseRoomReset.ExecuteAsync(SchoolDatabase.DbPath, _course.No);
                        Debug.WriteLine(
                            $"[CourseEditDialog] 강의실 초기화: 배치 {done.Lessons} · 시수 {done.WeeklyHours} · " +
                            $"진도 {done.Progress} · 배정 강의실 {done.Enrollments}");
                    }
                    catch (Exception ex)
                    {
                        // 정리에 실패하면 강의실도 바꾸지 않는다 — 반쯤 지워진 상태가 제일 나쁘다.
                        ShowError($"딸린 기록을 정리하지 못해 강의실을 바꾸지 않았습니다.\n{ex.Message}");
                        args.Cancel = true;
                        return;
                    }
                }

                // 수정
                _course.Subject = TxtSubject.Text.Trim();
                _course.Grade = int.Parse(((ComboBoxItem)CBoxGrade.SelectedItem).Tag.ToString()!);
                _course.Unit = (int)NumUnit.Value;
                _course.Type = ((ComboBoxItem)CBoxType.SelectedItem).Tag.ToString()!;
                _course.Rooms = newRooms;
                _course.Remark = TxtRemark.Text.Trim();

                using var repo = new CourseRepository(SchoolDatabase.DbPath);
                bool success = await repo.UpdateAsync(_course);

                if (!success)
                {
                    ShowError("수업 수정에 실패했습니다.");
                    args.Cancel = true;
                    return;
                }
            }
            else
            {
                // 추가
                var newCourse = new Course
                {
                    SchoolCode = _schoolCode,
                    TeacherID = _teacherId,
                    Year = _year,
                    Semester = _semester,
                    Subject = TxtSubject.Text.Trim(),
                    Grade = int.Parse(((ComboBoxItem)CBoxGrade.SelectedItem).Tag.ToString()!),
                    Unit = (int)NumUnit.Value,
                    Type = ((ComboBoxItem)CBoxType.SelectedItem).Tag.ToString()!,
                    Rooms = TxtRooms.Text.Trim(),
                    Remark = TxtRemark.Text.Trim()
                };

                using var repo = new CourseRepository(SchoolDatabase.DbPath);
                int courseNo = await repo.CreateAsync(newCourse);

                if (courseNo <= 0)
                {
                    ShowError("수업 생성에 실패했습니다.");
                    args.Cancel = true;
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            ShowError($"저장 중 오류가 발생했습니다.\n{ex.Message}");
            args.Cancel = true;
        }
        finally
        {
            deferral.Complete();
        }
    }
}
