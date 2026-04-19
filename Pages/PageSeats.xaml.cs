using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using NewSchool.Controls;
using NewSchool.Dialogs;
using NewSchool.Models;
using NewSchool.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Globalization.NumberFormatting;

namespace NewSchool.Pages;

/// <summary>
/// NumberBox에 "5줄" 형식으로 표시하는 포맷터
/// </summary>
internal sealed partial class JulNumberFormatter : INumberFormatter2, INumberParser
{
    public string FormatDouble(double value) => $"{(int)value}줄";
    public string FormatInt(long value) => $"{value}줄";
    public string FormatUInt(ulong value) => $"{value}줄";
    public double? ParseDouble(string text)
    {
        var cleaned = text.Replace("줄", "").Trim();
        return double.TryParse(cleaned, out var v) ? v : null;
    }
    public long? ParseInt(string text)
    {
        var cleaned = text.Replace("줄", "").Trim();
        return long.TryParse(cleaned, out var v) ? v : null;
    }
    public ulong? ParseUInt(string text)
    {
        var cleaned = text.Replace("줄", "").Trim();
        return ulong.TryParse(cleaned, out var v) ? v : null;
    }
}

/// <summary>
/// 좌석 배치 페이지
/// Enrollment 모델 직접 사용 (StudentListItemViewModel 제거)
/// </summary>
public sealed partial class PageSeats : Page
{
    #region Fields

    private ObservableCollection<StudentCardData> students = new();
    private int TotalStudents;
    private int TotalSeats;
    private readonly List<PhotoCard> Cards = new();
    private int _jjak = 0;
    private int _jul = 0;
    private int TotalRows;
    private bool IsViewPhoto;
    private int Grade = 0;
    private int ClassRoom = 0;
    private bool isInitialized = false;

    // 클릭 기반 배치를 위한 필드 - Enrollment 사용
    private Enrollment? _selectedStudentFromList = null;

    // Spacing
    private double SpaceJul;
    private double SpaceJjak;
    private double SpaceSide;
    private double SpaceRow;

    // 짝 분리/고정 목록: (StudentID_A, StudentID_B)
    private readonly List<(string IdA, string IdB)> _exclusionPairs = new();
    private readonly List<(string IdA, string IdB)> _fixedPairs = new();

    // 배치 옵션 (이력·성별·앞자리 등) — SeatService로 저장·로드
    private SeatOptions _options = new();
    private bool _isLocked;
    private int _savedRoundsCount; // 다이얼로그 안내용

    // Services
    private EnrollmentService? enrollmentService;
    private StudentService? studentService;
    private SeatService? seatService;

    #endregion

    #region Constructor

    public PageSeats()
    {
        this.InitializeComponent();
        NBoxJul.NumberFormatter = new JulNumberFormatter();
        this.Loaded += PageSeats_Loaded;
        this.Unloaded += Page_Unloaded;
    }

    private void PageSeats_Loaded(object sender, RoutedEventArgs e)
    {
        InitializeServices();
        InitializeData();
        
        // ListStudent 선택 이벤트 등록
        StudentList.StudentSelected += StudentList_StudentSelected;
    }

    private void StudentList_StudentSelected(object? sender, Enrollment e)
    {
        _selectedStudentFromList = e;
        
        // InfoBar 표시
        SelectedStudentInfoBar.Title = $"{e.Number}. {e.Name} 선택됨";
        SelectedStudentInfoBar.Message = "좌석을 클릭하여 배치하세요. 드래그도 가능합니다.";
        SelectedStudentInfoBar.IsOpen = true;
        
        Debug.WriteLine($"[PageSeats] 학생 선택됨: {e.Name} - 좌석을 클릭하여 배치하세요");
    }

    private void InitializeServices()
    {
        try
        {
            enrollmentService = new EnrollmentService();
            studentService = new StudentService(SchoolDatabase.DbPath);
            seatService = new SeatService();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[PageSeats] InitializeServices 오류: {ex.Message}");
        }
    }

    private void InitializeData()
    {
        try
        {
            // 콤보박스 데이터 초기화
            for (int i = 1; i <= 3; i++)
            {
                var comboBoxItem = new ComboBoxItem
                {
                    Content = $"{i}학년",
                    Tag = i
                };
                CBoxGrade.Items.Add(comboBoxItem);
                if (Settings.HomeGrade.Value == i)
                {
                    CBoxGrade.SelectedItem = comboBoxItem;
                }   
            }
            _jul = 5;
            _jjak = 1;
            UpdateDotPattern();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[PageSeats] InitializeData 오류: {ex.Message}");
        }
    }

    #endregion

    #region Event Handlers - ComboBox

    private async void CBoxGrade_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CBoxGrade.SelectedIndex < 0 || enrollmentService == null) return;

        Grade = (int)((ComboBoxItem)CBoxGrade.SelectedItem).Tag;

        // 해당 학년의 학급 목록 가져오기
        CBoxRooms.Items.Clear();
        
        try
        {
            using var EnrollmentService = new EnrollmentService();
            var classList = await EnrollmentService.GetClassListAsync(
                Settings.SchoolCode.Value,
                Settings.WorkYear.Value,
                Grade);

            foreach (var classNo in classList)
            {
                var item = new ComboBoxItem
                {
                    Content = $"{classNo}반",
                    Tag = classNo
                };

                CBoxRooms.Items.Add(item);
                if (Settings.HomeRoom.Value == classNo)
                {
                    CBoxRooms.SelectedItem = item;
                }
            }
        }
        catch (Exception ex)
        {
            await MessageBox.ShowAsync(ex.Message, "학급 목록 로딩 오류");
        }
    }

    private async void CBoxRooms_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CBoxRooms.SelectedIndex < 0) return;

        ClassRoom = (int)((ComboBoxItem)CBoxRooms.SelectedItem).Tag;

        // 학생 목록 가져오기
        await LoadStudentsAsync();
    }

    private void NBoxJul_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (double.IsNaN(args.NewValue)) { sender.Value = args.OldValue; return; }
        _jul = (int)args.NewValue;
        UpdateDotPattern();
    }

    private void ChkJJak_Click(object sender, RoutedEventArgs e)
    {
        _jjak = ChkJJak.IsChecked == true ? 2 : 1;
        UpdateDotPattern();
    }

    /// <summary>
    /// 인라인 도트 패턴 업데이트: ●● ●● ●● (줄×짝 시각화)
    /// </summary>
    private void UpdateDotPattern()
    {
        if (TxtDotPattern == null || _jul == 0) return;
        var dots = string.Join("  ",
            Enumerable.Range(0, _jul).Select(_ => new string('●', _jjak)));
        TxtDotPattern.Text = dots;
    }

    #endregion

    #region Data Loading

    private async Task LoadStudentsAsync()
    {
        if (enrollmentService == null || studentService == null) return;

        try
        {
            // EnrollmentService를 통해 학급 명부 조회
            var roster = await enrollmentService.GetClassRosterAsync(
                Settings.SchoolCode.Value, 
                Settings.WorkYear.Value, 
                Grade, 
                ClassRoom);

            // students 컬렉션 초기화
            students.Clear();

            foreach (var enrollment in roster)
            {
                // StudentCardData 생성 (PhotoCard용)
                students.Add(new StudentCardData
                {
                    StudentID = enrollment.StudentID,
                    Name = enrollment.Name,
                    Number = enrollment.Number,
                    Grade = enrollment.Grade,
                    Class = enrollment.Class,
                    PhotoPath = enrollment.Photo ?? "",
                    Sex = enrollment.Sex ?? string.Empty
                });
            }

            // ListStudent 컨트롤에 Enrollment 직접 로드
            StudentList.LoadStudents(roster.OrderBy(e => e.Number).ToList());
            TotalStudents = students.Count;

            Debug.WriteLine($"[PageSeats] 학생 목록 로드 완료: {TotalStudents}명");

            // 저장된 배치 복원 시도
            await TryLoadSavedArrangementAsync();
        }
        catch (Exception ex)
        {
            await MessageBox.ShowAsync(ex.Message, "학생 목록 로딩 오류");
        }
    }

    #endregion

    #region Seat Initialization

    private void BtnInit_Click(object sender, RoutedEventArgs e)
    {
        StudentList.ClearSelection();
        InitSeats();
    }

    private void InitSeats()
    {
        if (Room == null) return;

        IsViewPhoto = ChkViewPhoto.IsChecked == true;
        Room.Children.Clear();
        Cards.Clear();

        TotalStudents = students.Count;
        SpaceSide = 10;
        SpaceJul = 20;
        SpaceJjak = 10;
        SpaceRow = 20;

        TotalRows = (int)Math.Ceiling((double)TotalStudents / (_jjak * _jul));

        // 카드 높이 계산
        double cardHeight = (Room.ActualHeight - SpaceRow - SpaceRow * TotalRows) / TotalRows;
        double cardWidth = (cardHeight - 2) / 5 * 3 + 2;

        // 카드 넓이가 room 넓이를 초과하는지 확인
        if (SpaceSide * 2 + SpaceJjak * (_jjak * _jul - 1) + SpaceJul * (_jul - 1) + cardWidth * (_jjak * _jul) > Room.ActualWidth)
        {
            cardWidth = (Room.ActualWidth - SpaceSide * 2 - SpaceJjak * (_jjak * _jul - 1) - SpaceJul * (_jul - 1)) / (_jjak * _jul);
            cardHeight = cardWidth / 3 * 5;
        }

        // 좌우 여백 재계산 (중앙 정렬)
        SpaceSide = (Room.ActualWidth - (SpaceJjak * (_jjak * _jul - 1) + SpaceJul * (_jul - 1) + cardWidth * (_jjak * _jul))) / 2;

        int idx = 0;
        for (int i = 0; i < TotalRows; i++)
        {
            for (int j = 0; j < _jjak * _jul; j++)
            {
                var card = new PhotoCard
                {
                    IsShowPhoto = IsViewPhoto,
                    No = idx,
                    Row = i,
                    Col = j,
                    StudentData = null,
                    CardWidth = cardWidth
                };

                card.StudentChanged += Card_StudentChanged;
                card.UnUsedChanged += Card_UnUsedChanged;
                card.FixedChanged += Card_FixedChanged;
                card.AllowDrop = true;
                card.DragOver += Card_DragOver;
                card.Drop += Card_Drop;
                card.Tapped += Card_Tapped;

                double top = Room.ActualHeight - SpaceRow * (i + 1) - cardHeight * (i + 1);
                double left = Room.ActualWidth - SpaceSide - (j + 1) * cardWidth - SpaceJjak * j - Math.Truncate((double)(j / _jjak)) * SpaceJul;

                Canvas.SetLeft(card, left);
                Canvas.SetTop(card, top);

                Cards.Add(card);
                Room.Children.Add(card);
                idx++;
            }
        }

        TotalSeats = Room.Children.Count;
        TBTable.Text = $"{Grade}학년 {ClassRoom}반";

        isInitialized = true;
        CheckSeat();
    }

    #endregion

    #region Click-based Assignment (클릭 기반 배치)

    /// <summary>
    /// PhotoCard 클릭 시 학생 배치
    /// </summary>
    private void Card_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is not PhotoCard card) return;

        // 학생이 선택되어 있으면 배치
        if (_selectedStudentFromList != null)
        {
            var student = students.FirstOrDefault(s => s.StudentID == _selectedStudentFromList.StudentID);
            if (student != null)
            {
                card.StudentData = student;
                _selectedStudentFromList = null;
                
                // InfoBar 닫기
                SelectedStudentInfoBar.IsOpen = false;
                
                Debug.WriteLine($"[PageSeats] 클릭으로 배치: {student.Name}");
            }
        }
    }

    #endregion

    #region Drag and Drop - PhotoCard

    private void Card_DragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = DataPackageOperation.Copy | DataPackageOperation.Move;
        
        // 드래그 시각적 피드백 개선
        if (e.DragUIOverride != null)
        {
            e.DragUIOverride.IsContentVisible = true;
            e.DragUIOverride.IsCaptionVisible = true;
            
            // Enrollment에서 드래그된 경우
            if (e.DataView.Properties.ContainsKey("Enrollment"))
            {
                e.DragUIOverride.Caption = "👉";
            }
            else if (e.DataView.Properties.ContainsKey("StudentData"))
            {
                var targetCard = (PhotoCard)sender;
                if (targetCard.StudentData != null)
                {
                    e.DragUIOverride.Caption = "🔄"; // 교환
                }
                else
                {
                    e.DragUIOverride.Caption = "👉"; // 이동
                }
            }
        }
    }

    private void Card_Drop(object sender, DragEventArgs e)
    {
        var targetCard = (PhotoCard)sender;
        Debug.WriteLine($"[PageSeats] 드롭 이벤트 발생 - 타겟 카드: {targetCard.Row},{targetCard.Col}");
        
        // 1. ListStudent에서 드래그된 경우 (Enrollment)
        if (e.DataView.Properties.TryGetValue("Enrollment", out object enrollmentObj) &&
            enrollmentObj is Enrollment enrollment)
        {
            // Enrollment를 StudentCardData로 변환
            var student = students.FirstOrDefault(s => s.StudentID == enrollment.StudentID);
            if (student != null)
            {
                targetCard.StudentData = student;
                Debug.WriteLine($"[PageSeats] Dropped from ListStudent: {student.Name}");
            }
            return;
        }

        // 2. PhotoCard에서 드래그된 경우 (학생 교환)
        if (e.DataView.Properties.TryGetValue("StudentData", out object studentObj) &&
            studentObj is StudentCardData draggedStudent)
        {
            // 원본 카드 찾기
            if (e.DataView.Properties.TryGetValue("SourceRow", out object rowObj) &&
                e.DataView.Properties.TryGetValue("SourceCol", out object colObj) &&
                rowObj is int sourceRow && colObj is int sourceCol)
            {
                var sourceCard = Cards.FirstOrDefault(c => c.Row == sourceRow && c.Col == sourceCol);
                Debug.WriteLine($"[PageSeats] 드롭된 카드: {targetCard.Row},{targetCard.Col} - 원본 카드: {sourceRow},{sourceCol}");
                
                if (sourceCard != null)
                {
                    // 타겟 카드에 학생이 있으면 교환
                    if (targetCard.StudentData != null)
                    {
                        var targetStudent = targetCard.StudentData;
                        
                        // 교환 수행
                        targetCard.StudentData = draggedStudent;
                        sourceCard.StudentData = targetStudent;
                        
                        Debug.WriteLine($"[PageSeats] 학생 교환: {draggedStudent.Name} ↔ {targetStudent.Name}");
                    }
                    else
                    {
                        // 타겟이 비어있으면 이동만
                        targetCard.StudentData = draggedStudent;
                        sourceCard.StudentData = null;
                        
                        Debug.WriteLine($"[PageSeats] 학생 이동: {draggedStudent.Name}");
                    }
                }
            }
        }
    }

    private void Room_DragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = DataPackageOperation.Copy;
    }

    private void Room_Drop(object sender, DragEventArgs e)
    {
        // Room에 직접 드롭하는 것은 무시
    }

    #endregion

    #region PhotoCard Event Handlers

    private void Card_StudentChanged(object? sender, StudentCardEventArgs e)
    {
        var card = sender as PhotoCard;
        if (card == null || card.StudentData == null) return;

        // 중복 제거
        foreach (var c in Cards)
        {
            if (c.StudentData == null) continue;
            if (c.Row == card.Row && c.Col == card.Col) continue;
            if (c.StudentData.StudentID == card.StudentData.StudentID)
            {
                c.StudentData = null;
                break;
            }
        }

        UpdateStudentList();
    }

    private void Card_UnUsedChanged(object? sender, EventArgs e)
    {
        var card = sender as PhotoCard;
        if (card == null) return;
        TotalSeats += card.IsUnUsed ? -1 : 1;
        CheckSeat();
    }

    private void Card_FixedChanged(object? sender, EventArgs e)
    {
        // 고정 좌석 변경 처리
    }

    #endregion

    #region Helper Methods

    private void UpdateStudentList()
    {
        // 학생 리스트에서 배정된 학생 선택 표시
        // ListStudent 컨트롤에서는 자동 관리되므로 특별한 처리 불필요
    }

    private bool CheckSeat()
    {
        if (TotalStudents > TotalSeats)
        {
            _ = MessageBox.ShowAsync($"학생수가 자리수보다 {TotalStudents - TotalSeats}개 많습니다.\n" +
                "자리에서 우클릭하여 미사용을 해제하거나 다시 초기화하세요.", "좌석 부족");
            return false;
        }
        else if (TotalStudents < TotalSeats)
        {
            _ = MessageBox.ShowAsync($"자리수가 학생수보다 {TotalSeats - TotalStudents}개 많습니다.\n" +
                "자리에서 우클릭하여 미사용을 설정하거나 다시 초기화하세요.", "좌석 초과");
            return false;
        }
        return true;
    }

    #endregion

    #region Auto Arrange

    private async void BtnArrange_Click(object sender, RoutedEventArgs e)
    {
        if (!isInitialized)
        {
            await MessageBox.ShowAsync("초기화되지 않았습니다.", "오류");
            return;
        }

        await ArrangeSeatAsync();
    }

    private async Task ArrangeSeatAsync()
    {
        if (!CheckSeat()) return;
        if (_isLocked)
        {
            await MessageBox.ShowAsync("배치가 잠겨 있습니다. 잠금을 해제하세요.", "잠김");
            return;
        }

        // 고정 좌석 제외하고 초기화
        foreach (var card in Cards)
        {
            if (card.IsFixed && card.StudentData != null) continue;
            if (card.StudentData == null) continue;
            card.StudentData = null;
        }

        Random random = new();

        // 이력 기반 제약 로드
        HashSet<(string, string)> recentPairs = new();
        Dictionary<string, HashSet<(int, int)>> recentPositions = new();
        if (seatService != null)
        {
            if (_options.RecentPairAvoidRounds > 0)
            {
                recentPairs = await seatService.GetRecentPairsAsync(
                    Settings.SchoolCode.Value, Settings.WorkYear.Value,
                    Grade, ClassRoom, _options.RecentPairAvoidRounds);
            }
            if (_options.RecentPositionAvoidRounds > 0)
            {
                recentPositions = await seatService.GetRecentPositionsAsync(
                    Settings.SchoolCode.Value, Settings.WorkYear.Value,
                    Grade, ClassRoom, _options.RecentPositionAvoidRounds);
            }
        }

        int maxAttempts = _options.MaxAttempts > 0 ? _options.MaxAttempts : 500;

        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            if (attempt > 0)
            {
                foreach (var card in Cards)
                {
                    if (card.IsFixed && card.StudentData != null) continue;
                    card.ReplaceStudent(null);
                }
            }

            // 1) 앞자리 우선 학생을 먼저 배치 (FrontPriorityMaxRow 이하 Row에 한정)
            var placedIds = new HashSet<string>();
            foreach (var frontId in _options.FrontPriorityStudentIds)
            {
                var student = students.FirstOrDefault(s => s.StudentID == frontId);
                if (student == null) continue;

                var frontSeats = Cards
                    .Where(c => !c.IsFixed && !c.IsUnUsed && c.StudentData == null
                                && c.Row <= _options.FrontPriorityMaxRow)
                    .Where(c => !IsForbiddenPos(frontId, c.Row, c.Col, recentPositions))
                    .ToList();
                if (frontSeats.Count == 0)
                {
                    // 위치 배제 무시하고 앞자리 우선 보장
                    frontSeats = Cards
                        .Where(c => !c.IsFixed && !c.IsUnUsed && c.StudentData == null
                                    && c.Row <= _options.FrontPriorityMaxRow)
                        .ToList();
                }
                if (frontSeats.Count == 0) continue;

                var pick = frontSeats[random.Next(frontSeats.Count)];
                pick.StudentData = student;
                placedIds.Add(student.StudentID);
            }

            // 2) 나머지 랜덤 배치
            var remaining = students.Where(s => !placedIds.Contains(s.StudentID)
                && !Cards.Any(c => c.StudentData?.StudentID == s.StudentID)).ToList();

            // 섞어서 편향 완화
            for (int i = remaining.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                (remaining[i], remaining[j]) = (remaining[j], remaining[i]);
            }

            foreach (var student in remaining)
            {
                var available = Cards.Where(c => !c.IsFixed && !c.IsUnUsed && c.StudentData == null).ToList();
                if (available.Count == 0) break;

                // 위치 이력 배제 필터
                var filtered = available.Where(c => !IsForbiddenPos(student.StudentID, c.Row, c.Col, recentPositions)).ToList();
                var pool = filtered.Count > 0 ? filtered : available;

                pool[random.Next(pool.Count)].StudentData = student;
            }

            // 3) 제약 검증 (기존 짝 제약 + 이력 짝 + 남녀 교차)
            if (IsValidArrangement(recentPairs))
                break;
        }

        UpdateStudentList();

        // 애니메이션
        await SeatAnimationAsync();

        // 정위치 배치
        await SeatAssignAsync();
    }

    /// <summary>특정 학생을 특정 좌표에 배치하면 최근 이력과 겹치는가?</summary>
    private static bool IsForbiddenPos(
        string studentId, int row, int col,
        Dictionary<string, HashSet<(int, int)>> recent)
    {
        return recent.TryGetValue(studentId, out var set) && set.Contains((row, col));
    }

    /// <summary>현재 배치가 모든 제약(분리·고정·이력·남녀)을 만족하는가?</summary>
    private bool IsValidArrangement(HashSet<(string, string)> recentPairs)
    {
        // 분리/고정 (기존)
        if ((_exclusionPairs.Count > 0 || _fixedPairs.Count > 0) && HasPairViolation())
            return false;

        // 짝 모드가 아니면 이하 제약 무관
        if (_jjak < 2) return true;

        // 인접한 짝 쌍 추출
        var pairs = new List<(PhotoCard A, PhotoCard B)>();
        foreach (var a in Cards)
        {
            foreach (var b in Cards)
            {
                if (a == b) continue;
                if (a.StudentData == null || b.StudentData == null) continue;
                if (!AreNeighbors(a, b)) continue;
                if (string.Compare(a.StudentData.StudentID, b.StudentData.StudentID, StringComparison.Ordinal) >= 0) continue;
                pairs.Add((a, b));
            }
        }

        // 이력 짝 배제
        foreach (var (a, b) in pairs)
        {
            var ia = a.StudentData!.StudentID;
            var ib = b.StudentData!.StudentID;
            var key = string.Compare(ia, ib, StringComparison.Ordinal) < 0 ? (ia, ib) : (ib, ia);
            if (recentPairs.Contains(key)) return false;
        }

        // 남녀 교차 우선: 가능한 쌍에서 동성 짝이 있으면 실패 처리
        if (_options.PreferMixedGenderPair)
        {
            foreach (var (a, b) in pairs)
            {
                var sa = a.StudentData!.Sex ?? string.Empty;
                var sb = b.StudentData!.Sex ?? string.Empty;
                if (!string.IsNullOrEmpty(sa) && !string.IsNullOrEmpty(sb) && sa == sb)
                    return false;
            }
        }

        return true;
    }

    /// <summary>저장된 배치를 로드해 카드에 반영</summary>
    private async Task TryLoadSavedArrangementAsync()
    {
        if (seatService == null || Grade == 0 || ClassRoom == 0) return;

        var saved = await seatService.LoadAsync(
            Settings.SchoolCode.Value, Settings.WorkYear.Value, Grade, ClassRoom);

        // 옵션은 항상 로드(없으면 기본값)
        _options = await seatService.LoadOptionsAsync(
            Settings.SchoolCode.Value, Settings.WorkYear.Value, Grade, ClassRoom);
        _exclusionPairs.Clear();
        _exclusionPairs.AddRange(_options.ExclusionPairs.Select(p => (p.IdA, p.IdB)));
        _fixedPairs.Clear();
        _fixedPairs.AddRange(_options.FixedPairs.Select(p => (p.IdA, p.IdB)));

        if (saved == null)
        {
            _savedRoundsCount = 0;
            _isLocked = false;
            BtnLock.IsChecked = false;
            return;
        }

        _savedRoundsCount = await CountRoundsAsync();

        // 레이아웃 복원: Jul·Jjak·Rows 반영 후 InitSeats 재호출
        _jul = saved.Jul;
        _jjak = saved.Jjak;
        NBoxJul.Value = _jul;
        ChkJJak.IsChecked = _jjak == 2;
        IsViewPhoto = saved.ShowPhoto;
        ChkViewPhoto.IsChecked = IsViewPhoto;
        TboxMessage.Text = saved.Message ?? string.Empty;
        UpdateDotPattern();

        InitSeats();

        // 셀 상태 + 학생 배정 복원
        foreach (var a in saved.Assignments)
        {
            var card = Cards.FirstOrDefault(c => c.Row == a.Row && c.Col == a.Col);
            if (card == null) continue;
            if (a.IsUnUsed) card.IsUnUsed = true;
            if (a.IsHidden) card.IsHidden = true;
            if (!string.IsNullOrEmpty(a.StudentID))
            {
                var student = students.FirstOrDefault(s => s.StudentID == a.StudentID);
                if (student != null) card.StudentData = student;
            }
            if (a.IsFixed) card.IsFixed = true;
        }

        _isLocked = saved.IsLocked;
        BtnLock.IsChecked = _isLocked;
        ApplyLockState();
    }

    private async Task<int> CountRoundsAsync()
    {
        if (seatService == null) return 0;
        // GetRecentPairsAsync(큰값) 호출하면 전체 round 수와 동등 — 간이 구현.
        var all = await seatService.GetRecentPairsAsync(
            Settings.SchoolCode.Value, Settings.WorkYear.Value, Grade, ClassRoom, int.MaxValue);
        return all.Count > 0 ? 1 : 0; // pair가 있으면 최소 1회차. 정확 카운트는 불필요.
    }

    /// <summary>저장 버튼</summary>
    private async void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        if (!isInitialized || seatService == null)
        {
            await MessageBox.ShowAsync("좌석이 초기화되지 않았습니다.", "오류");
            return;
        }
        if (Grade == 0 || ClassRoom == 0)
        {
            await MessageBox.ShowAsync("학년·반을 선택하세요.", "알림");
            return;
        }

        var arrangement = new SeatArrangement
        {
            SchoolCode = Settings.SchoolCode.Value,
            Year = Settings.WorkYear.Value,
            Grade = Grade,
            Class = ClassRoom,
            Jul = _jul,
            Jjak = _jjak,
            Rows = TotalRows,
            ShowPhoto = IsViewPhoto,
            Message = TboxMessage.Text ?? string.Empty,
            IsLocked = _isLocked,
        };

        foreach (var c in Cards)
        {
            arrangement.Assignments.Add(new SeatAssignment
            {
                Row = c.Row,
                Col = c.Col,
                StudentID = c.StudentData?.StudentID,
                IsUnUsed = c.IsUnUsed,
                IsHidden = c.IsHidden,
                IsFixed = c.IsFixed
            });
        }

        try
        {
            await seatService.SaveAsync(arrangement, _options, _jjak);
            _savedRoundsCount++;
            SelectedStudentInfoBar.Severity = InfoBarSeverity.Success;
            SelectedStudentInfoBar.Title = "저장 완료";
            SelectedStudentInfoBar.Message = $"{Grade}학년 {ClassRoom}반 좌석 배치가 저장되었습니다.";
            SelectedStudentInfoBar.IsOpen = true;
        }
        catch (Exception ex)
        {
            await MessageBox.ShowAsync($"저장 실패: {ex.Message}", "오류");
        }
    }

    /// <summary>잠금 토글</summary>
    private void BtnLock_Click(object sender, RoutedEventArgs e)
    {
        _isLocked = BtnLock.IsChecked == true;
        ApplyLockState();
    }

    private void ApplyLockState()
    {
        foreach (var c in Cards)
        {
            c.AllowDrop = !_isLocked;
            c.CanDrag = !_isLocked;
        }
        BtnArrange.IsEnabled = !_isLocked;
    }

    private async Task SeatAnimationAsync()
    {
        int repeat = 2;
        int speed = 30;
        Random r = new();

        // 랜덤 위치로 흩뿌리기
        foreach (var card in Cards)
        {
            double l = r.Next(0, (int)(Room.ActualWidth - card.ActualWidth));
            double t = r.Next(0, (int)(Room.ActualHeight - card.ActualHeight));
            Canvas.SetLeft(card, l);
            Canvas.SetTop(card, t);
        }

        for (int j = 0; j < repeat; j++)
        {
            foreach (var card in Cards)
            {
                double l = r.Next(0, (int)(Room.ActualWidth - card.ActualWidth));
                double t = r.Next(0, (int)(Room.ActualHeight - card.ActualHeight));
                Canvas.SetLeft(card, l);
                Canvas.SetTop(card, t);
                await Task.Delay(speed);
            }
        }
    }

    private async Task SeatAssignAsync()
    {
        foreach (var card in Cards)
        {
            double top = Room.ActualHeight - SpaceRow * (card.Row + 1) - card.ActualHeight * (card.Row + 1);
            double left = Room.ActualWidth - SpaceSide - (card.Col + 1) * card.ActualWidth - SpaceJjak * card.Col
                - Math.Truncate((double)(card.Col / _jjak)) * SpaceJul;

            Canvas.SetLeft(card, left);
            Canvas.SetTop(card, top);

            await Task.Delay(150);
        }
    }

    #endregion

    #region Exclusion Pairs

    private async void BtnExclusion_Click(object sender, RoutedEventArgs e)
    {
        if (students.Count == 0)
        {
            await MessageBox.ShowAsync("학생 목록이 없습니다.", "알림");
            return;
        }

        var dialog = new SeatOptionsDialog(students, _options, _savedRoundsCount)
        {
            XamlRoot = this.XamlRoot
        };
        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary) return;

        _options = dialog.Result;

        // 기존 _exclusionPairs / _fixedPairs 와 동기화 (알고리즘·HasPairViolation 호환)
        _exclusionPairs.Clear();
        _exclusionPairs.AddRange(_options.ExclusionPairs.Select(p => (p.IdA, p.IdB)));
        _fixedPairs.Clear();
        _fixedPairs.AddRange(_options.FixedPairs.Select(p => (p.IdA, p.IdB)));
    }

    /// <summary>
    /// 두 학생이 짝(같은 줄, 인접 좌석)인지 확인
    /// </summary>
    private bool AreNeighbors(PhotoCard a, PhotoCard b)
    {
        if (a.Row != b.Row) return false;
        int groupA = a.Col / _jjak;
        int groupB = b.Col / _jjak;
        if (groupA != groupB) return false;
        return Math.Abs(a.Col - b.Col) == 1;
    }

    /// <summary>
    /// 현재 배치에서 짝 제약 위반이 있는지 확인
    /// </summary>
    private bool HasPairViolation()
    {
        // 분리 쌍이 짝이면 위반
        foreach (var (idA, idB) in _exclusionPairs)
        {
            var cardA = Cards.FirstOrDefault(c => c.StudentData?.StudentID == idA);
            var cardB = Cards.FirstOrDefault(c => c.StudentData?.StudentID == idB);
            if (cardA != null && cardB != null && AreNeighbors(cardA, cardB))
                return true;
        }

        // 고정 쌍이 짝이 아니면 위반
        foreach (var (idA, idB) in _fixedPairs)
        {
            var cardA = Cards.FirstOrDefault(c => c.StudentData?.StudentID == idA);
            var cardB = Cards.FirstOrDefault(c => c.StudentData?.StudentID == idB);
            if (cardA != null && cardB != null && !AreNeighbors(cardA, cardB))
                return true;
        }

        return false;
    }

    #endregion

    #region Other Event Handlers

    private void ChkViewPhoto_Click(object sender, RoutedEventArgs e)
    {
        IsViewPhoto = ChkViewPhoto.IsChecked == true;
        foreach (var card in Cards)
        {
            card.IsShowPhoto = IsViewPhoto;
        }
    }

    private void GridBody_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // 크기 변경 시 재배치 필요 시 구현
    }

    private async void BtnPrint_Click(object sender, RoutedEventArgs e)
    {
        if (!isInitialized || Cards.Count == 0)
        {
            await MessageBox.ShowAsync("좌석이 초기화되지 않았습니다.", "오류");
            return;
        }

        try
        {
            var printService = new SeatsPrintService();
            
            var pdfPath = printService.GenerateSeatsPdf(
                Cards,
                Grade,
                ClassRoom,
                _jul,
                _jjak,
                TboxMessage.Text,
                IsViewPhoto);

            // PDF를 시스템 기본 뷰어로 열기
            var uri = new Uri($"file:///{pdfPath.Replace("\\", "/")}");
            var success = await Windows.System.Launcher.LaunchUriAsync(uri);
            
            if (!success)
            {
                await MessageBox.ShowAsync($"PDF 파일을 열 수 없습니다.\n경로: {pdfPath}", "오류");
            }
        }
        catch (Exception ex)
        {
            await MessageBox.ShowAsync($"PDF 생성 오류: {ex.Message}", "오류");
        }
    }

    #endregion

    #region Cleanup

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        // Service Dispose
        enrollmentService?.Dispose();
        studentService?.Dispose();
        seatService?.Dispose();
    }

    #endregion
}
