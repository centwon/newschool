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
        _ = LoadSectionsAsync().ContinueWith(t =>
        {
            if (t.IsFaulted)
                System.Diagnostics.Debug.WriteLine($"[LessonLogEditDialog] {t.Exception?.InnerException?.Message}");
        }, TaskContinuationOptions.OnlyOnFaulted);
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
        _ = LoadSectionsAsync().ContinueWith(t =>
        {
            if (t.IsFaulted)
                System.Diagnostics.Debug.WriteLine($"[LessonLogEditDialog] {t.Exception?.InnerException?.Message}");
        }, TaskContinuationOptions.OnlyOnFaulted);
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

        // 새로 쓰는 중이면 지울 것이 없다 — ContentDialog 는 텍스트가 비면 그 버튼을 감춘다.
        SecondaryButtonText = _isEdit ? "삭제" : string.Empty;
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
                // 이 화면은 XAML 에서 Tag="1"(문자열)을 쓰는 콤보(교시)와
                // 코드에서 Tag = int 를 넣는 콤보(단원)가 섞여 있다. (int) 로 언박싱하면
                // 어느 쪽을 읽는지에 따라 터지므로, 문자열 경유로 통일해 읽는다.
                var match = CBoxSection.Items
                    .OfType<ComboBoxItem>()
                    .FirstOrDefault(i => int.TryParse(i.Tag?.ToString(), out int no)
                                         && no == _lessonLog.CourseSectionNo.Value);

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

            // 저장하러 왔다면 삭제 예고는 취소된 것으로 본다
            // (검증에 걸려 창이 남았을 때 예고가 살아 있으면 다음 '삭제' 한 번에 지워진다).
            _deleteArmed = false;
            DeleteConfirmInfoBar.IsOpen = false;

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

    /// <summary>삭제를 한 번 눌러 예고한 상태인가. 두 번째 누름에서 실제로 지운다.</summary>
    private bool _deleteArmed;

    /// <summary>
    /// '삭제' 버튼 — 첫 번째 누름은 경고만 띄우고, 한 번 더 누르면 지운다.
    ///
    /// <para>예전에는 본문 안의 별도 버튼에서 <c>MessageBox.ShowConfirmAsync</c> 를 불렀다. 그런데
    /// 이 대화상자가 열려 있는 동안에는 또 다른 ContentDialog 를 띄울 수 없어(WinUI 제약) 확인 창이
    /// <b>영영 뜨지 않았고</b>, 게이트의 재시도 루프가 250ms 간격으로 헛돌며 로그만 쌓였다.
    /// 지금은 확인을 대화상자 안에서 받고, 버튼도 저장·취소와 같은 줄(<c>SecondaryButton</c>)로 옮겼다
    /// — 형제인 <c>UnifiedItemDialog</c> 와 같은 배치다.</para>
    /// </summary>
    private async void OnSecondaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        args.Cancel = true;   // 지우든 말든 이 버튼으로는 창을 닫지 않는다
        if (_lessonLog == null) return;

        if (!_deleteArmed)
        {
            HideError();
            _deleteArmed = true;
            DeleteConfirmInfoBar.IsOpen = true;
            return;
        }

        var deferral = args.GetDeferral();
        try
        {
            DeleteConfirmInfoBar.IsOpen = false;

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
            ShowError("삭제 중 오류가 발생했습니다." + Environment.NewLine + ex.Message);
        }
        finally
        {
            _deleteArmed = false;
            deferral.Complete();
        }
    }

    /// <summary>경고를 닫으면 예고도 푼다 — 나중에 무심코 누른 '삭제' 가 바로 지우지 않도록.</summary>
    private void DeleteConfirmInfoBar_Closed(InfoBar sender, InfoBarClosedEventArgs args)
        => _deleteArmed = false;
}
