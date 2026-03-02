using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NewSchool.Controls;
using NewSchool.Models;
using NewSchool.Services;

namespace NewSchool.Dialogs;

/// <summary>
/// 수업 기록 편집 다이얼로그
/// Grade, Class, CourseSectionNo, Note 필드 지원
/// </summary>
public sealed partial class LessonLogEditDialog : ContentDialog
{
    private LessonLog? _lessonLog;
    private readonly string _teacherId;
    private readonly string _subject;
    private readonly string _room;
    private readonly int _grade;
    private readonly int _classNum;
    private readonly int? _courseNo;
    private readonly bool _isEdit;

    private List<CourseSection> _sections = new();

    /// <summary>저장/삭제 후 결과</summary>
    public LessonLog? ResultLog { get; private set; }
    public bool IsDeleted { get; private set; }

    /// <summary>
    /// 새 수업 기록 추가
    /// </summary>
    public LessonLogEditDialog(
        string teacherId, string subject, string room,
        int grade = 0, int classNum = 0, int? courseNo = null,
        int? defaultPeriod = null)
    {
        this.InitializeComponent();

        _teacherId = teacherId;
        _subject = subject;
        _room = room;
        _grade = grade;
        _classNum = classNum;
        _courseNo = courseNo;
        _isEdit = false;

        Title = "수업 기록 추가";
        InitializeControls(defaultPeriod);
        _ = LoadSectionsAsync();
    }

    /// <summary>
    /// 기존 수업 기록 수정
    /// </summary>
    public LessonLogEditDialog(LessonLog lessonLog, int? courseNo = null)
    {
        this.InitializeComponent();

        _lessonLog = lessonLog;
        _teacherId = lessonLog.TeacherID;
        _subject = lessonLog.Subject;
        _room = lessonLog.Room;
        _grade = lessonLog.Grade;
        _classNum = lessonLog.Class;
        _courseNo = courseNo;
        _isEdit = true;

        Title = "수업 기록 수정";
        InitializeControls(null);
        LoadLessonLogData();
        _ = LoadSectionsAsync();
    }

    /// <summary>
    /// 컨트롤 초기화
    /// </summary>
    private void InitializeControls(int? defaultPeriod)
    {
        TxtSubject.Text = _subject;
        TxtRoom.Text = _room;

        // 학급 표시
        TxtClass.Text = (_grade > 0 && _classNum > 0) ? $"{_grade}-{_classNum}" : "";

        // 날짜 기본값
        DatePicker.Date = DateTimeOffset.Now;

        // 교시 기본값
        if (defaultPeriod.HasValue && defaultPeriod.Value >= 1 && defaultPeriod.Value <= 7)
        {
            CBoxPeriod.SelectedIndex = defaultPeriod.Value - 1;
        }
        else
        {
            int currentPeriod = LessonLogService.GetCurrentPeriod();
            CBoxPeriod.SelectedIndex = (currentPeriod >= 1 && currentPeriod <= 7)
                ? currentPeriod - 1 : 0;
        }

        BtnDelete.Visibility = _isEdit ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// 기존 데이터 로드 (수정 모드)
    /// </summary>
    private void LoadLessonLogData()
    {
        if (_lessonLog == null) return;

        DatePicker.Date = _lessonLog.Date;

        if (_lessonLog.Period >= 1 && _lessonLog.Period <= 7)
            CBoxPeriod.SelectedIndex = _lessonLog.Period - 1;

        TxtRoom.Text = _lessonLog.Room;
        TxtTopic.Text = _lessonLog.Topic;
        TxtContent.Text = _lessonLog.Content;
        TxtNote.Text = _lessonLog.Note;
    }

    /// <summary>
    /// 단원 목록 로드
    /// </summary>
    private async Task LoadSectionsAsync()
    {
        if (!_courseNo.HasValue || _courseNo.Value <= 0)
        {
            CBoxSection.IsEnabled = false;
            CBoxSection.PlaceholderText = "수업(Course) 정보 없음";
            return;
        }

        try
        {
            var repo = new Repositories.CourseSectionRepository(SchoolDatabase.DbPath);
            _sections = await repo.GetByCourseAsync(_courseNo.Value);

            CBoxSection.Items.Clear();

            foreach (var s in _sections.OrderBy(x => x.SortOrder))
            {
                CBoxSection.Items.Add(new ComboBoxItem
                {
                    Content = $"{s.FullPath} {s.SectionName}",
                    Tag = s.No
                });
            }

            // 수정 모드에서 기존 단원 선택
            if (_isEdit && _lessonLog?.CourseSectionNo.HasValue == true)
            {
                var match = CBoxSection.Items
                    .OfType<ComboBoxItem>()
                    .FirstOrDefault(i => (int)i.Tag == _lessonLog.CourseSectionNo.Value);

                if (match != null)
                    CBoxSection.SelectedItem = match;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LessonLogEditDialog] 단원 로드 실패: {ex.Message}");
            CBoxSection.IsEnabled = false;
            CBoxSection.PlaceholderText = "단원 로드 실패";
        }
    }

    #region Event Handlers

    private void DatePicker_DateChanged(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args)
    {
        HideError();
    }

    private void CBoxSection_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // 단원 선택 시 자동으로 주제 채우기 (주제가 비어있을 때만)
        if (CBoxSection.SelectedItem is ComboBoxItem item && item.Tag is int sectionNo)
        {
            var section = _sections.FirstOrDefault(s => s.No == sectionNo);
            if (section != null && string.IsNullOrWhiteSpace(TxtTopic.Text))
            {
                TxtTopic.Text = section.SectionName;
            }
        }
    }

    private void BtnClearSection_Click(object sender, RoutedEventArgs e)
    {
        CBoxSection.SelectedIndex = -1;
    }

    #endregion

    #region Error Handling

    private void ShowError(string message)
    {
        ErrorInfoBar.Message = message;
        ErrorInfoBar.IsOpen = true;
    }

    private void HideError()
    {
        ErrorInfoBar.IsOpen = false;
    }

    #endregion

    /// <summary>
    /// 저장 버튼 클릭
    /// </summary>
    private async void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var deferral = args.GetDeferral();

        try
        {
            HideError();

            if (!DatePicker.Date.HasValue)
            {
                ShowError("날짜를 선택해주세요.");
                args.Cancel = true;
                return;
            }

            if (CBoxPeriod.SelectedItem == null)
            {
                ShowError("교시를 선택해주세요.");
                args.Cancel = true;
                return;
            }

            if (string.IsNullOrWhiteSpace(TxtTopic.Text))
            {
                ShowError("주제를 입력해주세요.");
                args.Cancel = true;
                return;
            }

            int period = int.Parse(((ComboBoxItem)CBoxPeriod.SelectedItem).Tag.ToString()!);

            // 단원 정보 추출
            int? sectionNo = null;
            string sectionName = string.Empty;
            if (CBoxSection.SelectedItem is ComboBoxItem selectedSection && selectedSection.Tag is int sNo)
            {
                sectionNo = sNo;
                var section = _sections.FirstOrDefault(s => s.No == sNo);
                sectionName = section?.SectionName ?? "";
            }

            using var service = new LessonLogService();

            if (_isEdit && _lessonLog != null)
            {
                _lessonLog.Date = DatePicker.Date.Value.DateTime;
                _lessonLog.Period = period;
                _lessonLog.Room = TxtRoom.Text.Trim();
                _lessonLog.Grade = _grade;
                _lessonLog.Class = _classNum;
                _lessonLog.CourseSectionNo = sectionNo;
                _lessonLog.SectionName = sectionName;
                _lessonLog.Topic = TxtTopic.Text.Trim();
                _lessonLog.Content = TxtContent.Text.Trim();
                _lessonLog.Note = TxtNote.Text.Trim();
                _lessonLog.UpdatedAt = DateTime.Now;

                var (isValid, errorMessage) = service.ValidateLog(_lessonLog);
                if (!isValid)
                {
                    ShowError(errorMessage);
                    args.Cancel = true;
                    return;
                }

                int result = await service.UpdateAsync(_lessonLog);
                if (result <= 0)
                {
                    ShowError("수업 기록 수정에 실패했습니다.");
                    args.Cancel = true;
                    return;
                }

                ResultLog = _lessonLog;
            }
            else
            {
                var newLog = new LessonLog
                {
                    TeacherID = _teacherId,
                    Year = Settings.WorkYear.Value,
                    Semester = Settings.WorkSemester.Value,
                    Date = DatePicker.Date.Value.DateTime,
                    Period = period,
                    Subject = _subject,
                    Grade = _grade,
                    Class = _classNum,
                    Room = TxtRoom.Text.Trim(),
                    CourseSectionNo = sectionNo,
                    SectionName = sectionName,
                    Topic = TxtTopic.Text.Trim(),
                    Content = TxtContent.Text.Trim(),
                    Note = TxtNote.Text.Trim(),
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now
                };

                var (isValid, errorMessage) = service.ValidateLog(newLog);
                if (!isValid)
                {
                    ShowError(errorMessage);
                    args.Cancel = true;
                    return;
                }

                int newNo = await service.InsertAsync(newLog);
                if (newNo <= 0)
                {
                    ShowError("수업 기록 추가에 실패했습니다.");
                    args.Cancel = true;
                    return;
                }

                newLog.No = newNo;
                ResultLog = newLog;
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

    /// <summary>
    /// 삭제 버튼 클릭
    /// </summary>
    private async void BtnDelete_Click(object sender, RoutedEventArgs e)
    {
        if (_lessonLog == null) return;

        var confirmed = await MessageBox.ShowConfirmAsync(
            "이 수업 기록을 삭제하시겠습니까?\n이 작업은 되돌릴 수 없습니다.",
            "수업 기록 삭제", "삭제", "취소");
        if (!confirmed) return;

        try
        {
            using var service = new LessonLogService();
            int deleteResult = await service.DeleteAsync(_lessonLog.No);

            if (deleteResult > 0)
            {
                IsDeleted = true;
                this.Hide();
            }
            else
            {
                ShowError("삭제에 실패했습니다.");
            }
        }
        catch (Exception ex)
        {
            ShowError($"삭제 중 오류가 발생했습니다.\n{ex.Message}");
        }
    }
}
