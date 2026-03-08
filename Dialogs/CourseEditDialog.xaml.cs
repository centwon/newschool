using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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
        if (TxtRooms == null) return;
        var type = (CBoxType.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        string template = string.Empty;
        switch (type)
        {
            case CourseTypes.Class:
                var grade = (CBoxGrade.SelectedItem as ComboBoxItem)?.Tag;
                if (grade != null && int.TryParse(grade.ToString(), out int gradeInt))
                {
                    template = await GetClassListFromEnrollmentAsync(gradeInt);
                }
                break;
            case CourseTypes.Club:

                break;
            case CourseTypes.Selective:

                break;
            default:
                break;
        }

        TxtRooms.Text = template;
        UpdateRoomsPreview();


    }
    private async Task<string> GetClassListFromEnrollmentAsync(int grade)
    {
        using var enrollservice = new EnrollmentService();
        var classList = await enrollservice.GetClassListAsync(_schoolCode, _year, grade);
        return string.Join(", ", classList.Select(c => $"{grade}-{c}"));

    }


    private async void CBoxType_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TxtRooms == null) return;
        var type = (CBoxType.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        string template = string.Empty;
        switch (type)
        {
            case CourseTypes.Class:
                var grade = (CBoxGrade.SelectedItem as ComboBoxItem)?.Tag;
                if (grade != null && int.TryParse(grade.ToString(), out int gradeInt))
                {
                    template = await GetClassListFromEnrollmentAsync(gradeInt);
                }
                break;
            case CourseTypes.Club:

                break;
            case CourseTypes.Selective:

                break;
            default:

                break;
        }
        TxtRooms.Text = template;
        UpdateRoomsPreview();
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
                // 수정
                _course.Subject = TxtSubject.Text.Trim();
                _course.Grade = int.Parse(((ComboBoxItem)CBoxGrade.SelectedItem).Tag.ToString()!);
                _course.Unit = (int)NumUnit.Value;
                _course.Type = ((ComboBoxItem)CBoxType.SelectedItem).Tag.ToString()!;
                _course.Rooms = TxtRooms.Text.Trim();
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
