using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NewSchool.Models;
using NewSchool.ViewModels;

namespace NewSchool.Controls;

/// <summary>
/// SpecListViewer - 학생부 특이사항 목록 뷰어
/// StudentSpecial 목록을 표시하고 편집
/// </summary>
public sealed partial class SpecListViewer : UserControl
{
    #region Fields

    private StudentInfoMode _studentInfoMode = StudentInfoMode.HideAll;
    private LogCategory _category = LogCategory.전체;

    #endregion

    #region Properties

    /// <summary>
    /// 학생 정보 표시 모드
    /// </summary>
    public StudentInfoMode StudentInfoMode
    {
        get => _studentInfoMode;
        set
        {
            if (_studentInfoMode != value)
            {
                _studentInfoMode = value;
                SetStudentInfoVisibility();
            }
        }
    }

    /// <summary>
    /// 카테고리 (유형별 컬럼 조정용)
    /// </summary>
    public LogCategory Category
    {
        get => _category;
        set
        {
            if (_category != value)
            {
                _category = value;
                ChangeCategory();
            }
        }
    }

    /// <summary>
    /// StudentSpecial 목록 (ViewModel)
    /// </summary>
    public ObservableCollection<StudentSpecialViewModel> Specs { get; } = new();

    /// <summary>
    /// 저장 대상 항목들 (체크되었거나 내용이 변경된 항목)
    /// </summary>
    public IEnumerable<StudentSpecialViewModel> SelectedSpecs => 
        Specs.Where(s => s.IsSelected || s.IsModified);

    #endregion

    #region Constructor

    public SpecListViewer()
    {
        this.InitializeComponent();
        SpecItemsRepeater.ItemsSource = Specs;
    }

    #endregion

    #region Event Handlers

    /// <summary>
    /// 데이터 행 Grid Loaded 이벤트 - 열 너비를 헤더와 동기화
    /// </summary>
    private void OnDataRowGridLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is Grid grid && grid.ColumnDefinitions.Count >= 10)
        {
            // 헤더와 동일한 너비 적용
            // 컬럼 순서: 학년도 · 유형 · 학년 · 반 · 번호 · 이름 · 과목/분야
            // (과목/분야는 학생 이름 바로 오른쪽에 둔다)
            grid.ColumnDefinitions[1].Width = ColYearHeader.Width;
            grid.ColumnDefinitions[2].Width = ColTypeHeader.Width;
            grid.ColumnDefinitions[3].Width = ColGradeHeader.Width;
            grid.ColumnDefinitions[4].Width = ColClassHeader.Width;
            grid.ColumnDefinitions[5].Width = ColNumberHeader.Width;
            grid.ColumnDefinitions[6].Width = ColNameHeader.Width;
            grid.ColumnDefinitions[7].Width = ColSubjectHeader.Width;
        }
    }

    /// <summary>
    /// ItemsRepeater의 각 데이터 행 컬럼을 헤더와 동기화
    /// </summary>
    private void UpdateDataRowColumns()
    {
        // ItemsRepeater가 렌더링된 후에 각 항목을 찾아서 업데이트
        this.DispatcherQueue.TryEnqueue(() =>
        {
            for (int i = 0; i < Specs.Count; i++)
            {
                var element = SpecItemsRepeater.TryGetElement(i);
                if (element is Border border && border.Child is Grid grid)
                {
                    if (grid.ColumnDefinitions.Count >= 10)
                    {
                        grid.ColumnDefinitions[1].Width = ColYearHeader.Width;
                        grid.ColumnDefinitions[2].Width = ColTypeHeader.Width;
                        grid.ColumnDefinitions[3].Width = ColGradeHeader.Width;
                        grid.ColumnDefinitions[4].Width = ColClassHeader.Width;
                        grid.ColumnDefinitions[5].Width = ColNumberHeader.Width;
                        grid.ColumnDefinitions[6].Width = ColNameHeader.Width;
                        grid.ColumnDefinitions[7].Width = ColSubjectHeader.Width;
                    }
                }
            }
        });
    }

    /// <summary>
    /// 전체 선택 체크박스
    /// </summary>
    private void OnSelectAllChecked(object sender, RoutedEventArgs e)
    {
        foreach (var spec in Specs)
        {
            spec.IsSelected = true;
        }
    }

    /// <summary>
    /// 전체 선택 해제
    /// </summary>
    private void OnSelectAllUnchecked(object sender, RoutedEventArgs e)
    {
        foreach (var spec in Specs)
        {
            spec.IsSelected = false;
        }
    }

    /// <summary>
    /// 내용 변경 시
    /// </summary>
    private void OnContentChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox textBox && textBox.DataContext is StudentSpecialViewModel vm)
        {
            // x:Bind TwoWay는 LostFocus시만 Source 반영 → 즉시 동기화
            vm.Content = textBox.Text;
            vm.IsSelected = true;
        }
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// "과목/분야" 칸의 헤더 라벨과 폭을 결정하는 <b>유일한</b> 지점.
    ///
    /// 기준은 선택된 카테고리이고, '전체'일 때만 로드된 목록에서 단일 영역을 추론한다.
    /// 카테고리 변경(조회 전)에도 헤더가 맞게 바뀌어야 하므로 목록 내용만으로 판단하지 않는다.
    /// 라벨 문구는 <see cref="Helpers.NeisHelper.Areas"/> 정의표에서 가져온다.
    /// </summary>
    private void UpdateSubjectColumn()
    {
        if (TxtSubjectHeader == null) return;

        string? type = _category != LogCategory.전체 ? _category.ToString() : SingleLoadedSpecType();

        string? label;
        if (type == null)
        {
            label = "세부영역";                                    // 영역이 섞여 있음
        }
        else if (Helpers.NeisHelper.TitleCountsInBytes(type))
        {
            label = Helpers.NeisHelper.GetTitleLabel(type);        // 진로활동 → "희망분야"(입력칸)
        }
        else if (Helpers.NeisHelper.GetTitleLabel(type) != null)
        {
            // 과목에 묶인 영역 — 교과 세특은 학기까지 함께 보여준다
            label = Helpers.NeisHelper.IsSemesterScoped(type) ? "과목/학기" : "과목";
        }
        else if (type == nameof(LogCategory.동아리활동))
        {
            label = "동아리";
        }
        else
        {
            label = null;                                         // 보여줄 것 없음 → 칸을 접는다
        }

        bool show = label != null;
        TxtSubjectHeader.Text = label ?? string.Empty;
        TxtSubjectHeader.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        ColSubjectHeader.Width = show ? new GridLength(130) : new GridLength(0);

        UpdateDataRowColumns();
    }

    /// <summary>로드된 항목이 모두 같은 영역이면 그 영역, 섞여 있거나 비어 있으면 null.</summary>
    private string? SingleLoadedSpecType()
    {
        string? single = null;
        foreach (var vm in Specs)
        {
            if (single == null) single = vm.Type;
            else if (single != vm.Type) return null;
        }
        return single;
    }


    /// <summary>
    /// 학생 정보 컬럼 가시성 설정
    /// </summary>
    private void SetStudentInfoVisibility()
    {
        switch (_studentInfoMode)
        {
            case StudentInfoMode.HideAll:
                // 개인별 보기 - 모든 학생 정보 숨김
                ColYearHeader.Width = new GridLength(0);
                ColGradeHeader.Width = new GridLength(0);
                ColClassHeader.Width = new GridLength(0);
                ColNumberHeader.Width = new GridLength(0);
                ColNameHeader.Width = new GridLength(0);
                
                TxtYearHeader.Visibility = Visibility.Collapsed;
                TxtGradeHeader.Visibility = Visibility.Collapsed;
                TxtClassHeader.Visibility = Visibility.Collapsed;
                TxtNumberHeader.Visibility = Visibility.Collapsed;
                TxtNameHeader.Visibility = Visibility.Collapsed;
                break;

            case StudentInfoMode.ShowAll:
                // 모든 정보 표시
                ColYearHeader.Width = new GridLength(60);
                ColGradeHeader.Width = new GridLength(40);
                ColClassHeader.Width = new GridLength(40);
                ColNumberHeader.Width = new GridLength(40);
                ColNameHeader.Width = new GridLength(60);
                
                TxtYearHeader.Visibility = Visibility.Visible;
                TxtGradeHeader.Visibility = Visibility.Visible;
                TxtClassHeader.Visibility = Visibility.Visible;
                TxtNumberHeader.Visibility = Visibility.Visible;
                TxtNameHeader.Visibility = Visibility.Visible;
                break;

            case StudentInfoMode.GradeClassNumName:
                // 학년/반/번호/이름
                ColYearHeader.Width = new GridLength(0);
                ColGradeHeader.Width = new GridLength(40);
                ColClassHeader.Width = new GridLength(40);
                ColNumberHeader.Width = new GridLength(40);
                ColNameHeader.Width = new GridLength(60);
                
                TxtYearHeader.Visibility = Visibility.Collapsed;
                TxtGradeHeader.Visibility = Visibility.Visible;
                TxtClassHeader.Visibility = Visibility.Visible;
                TxtNumberHeader.Visibility = Visibility.Visible;
                TxtNameHeader.Visibility = Visibility.Visible;
                break;

            case StudentInfoMode.ClassNumName:
                // 반/번호/이름
                ColYearHeader.Width = new GridLength(0);
                ColGradeHeader.Width = new GridLength(0);
                ColClassHeader.Width = new GridLength(40);
                ColNumberHeader.Width = new GridLength(40);
                ColNameHeader.Width = new GridLength(60);
                
                TxtYearHeader.Visibility = Visibility.Collapsed;
                TxtGradeHeader.Visibility = Visibility.Collapsed;
                TxtClassHeader.Visibility = Visibility.Visible;
                TxtNumberHeader.Visibility = Visibility.Visible;
                TxtNameHeader.Visibility = Visibility.Visible;
                break;

            case StudentInfoMode.NumName:
                // 번호/이름
                ColYearHeader.Width = new GridLength(0);
                ColGradeHeader.Width = new GridLength(0);
                ColClassHeader.Width = new GridLength(0);
                ColNumberHeader.Width = new GridLength(40);
                ColNameHeader.Width = new GridLength(60);
                
                TxtYearHeader.Visibility = Visibility.Collapsed;
                TxtGradeHeader.Visibility = Visibility.Collapsed;
                TxtClassHeader.Visibility = Visibility.Collapsed;
                TxtNumberHeader.Visibility = Visibility.Visible;
                TxtNameHeader.Visibility = Visibility.Visible;
                break;

            case StudentInfoMode.NameOnly:
                // 이름만
                ColYearHeader.Width = new GridLength(0);
                ColGradeHeader.Width = new GridLength(0);
                ColClassHeader.Width = new GridLength(0);
                ColNumberHeader.Width = new GridLength(0);
                ColNameHeader.Width = new GridLength(60);

                TxtYearHeader.Visibility = Visibility.Collapsed;
                TxtGradeHeader.Visibility = Visibility.Collapsed;
                TxtClassHeader.Visibility = Visibility.Collapsed;
                TxtNumberHeader.Visibility = Visibility.Collapsed;
                TxtNameHeader.Visibility = Visibility.Visible;
                break;
        }

        // ItemsRepeater의 각 항목 업데이트
        UpdateDataRowColumns();
    }

    /// <summary>
    /// 카테고리에 따른 컬럼 조정.
    /// 과목/분야 칸은 <see cref="UpdateSubjectColumn"/> 한 곳에서만 결정한다 —
    /// 예전에는 이 메서드와 목록 로드가 각각 같은 칸의 폭·헤더를 건드려,
    /// 호출 순서에 따라 "입력칸은 보이는데 헤더는 비어 있는" 상태가 됐다.
    /// </summary>
    private void ChangeCategory()
    {
        // 유형(영역) 칸은 '전체'에서만 의미가 있다
        bool showType = _category == LogCategory.전체;
        ColTypeHeader.Width = showType ? new GridLength(100) : new GridLength(0);
        TxtTypeHeader.Visibility = showType ? Visibility.Visible : Visibility.Collapsed;

        UpdateSubjectColumn();
        UpdateDataRowColumns();
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// StudentSpecial 목록 로드
    /// </summary>
    public void LoadSpecs(IEnumerable<StudentSpecial> specials)
    {
        Specs.Clear();

        foreach (var special in specials)
        {
            Specs.Add(new StudentSpecialViewModel(special));
        }

        // 전체 선택 체크박스 초기화
        if (ChkSelectAll != null)
        {
            ChkSelectAll.IsChecked = false;
        }

        UpdateSubjectColumn();
    }

    /// <summary>
    /// StudentSpecial 목록과 학생 정보를 함께 로드
    /// </summary>
    /// <param name="specials">StudentSpecial 목록</param>
    /// <param name="studentInfoLookup">StudentID -> (Grade, ClassNum, Number, Name) 매핑</param>
    public void LoadSpecs(IEnumerable<StudentSpecial> specials, Dictionary<string, (int Grade, int ClassNum, int Number, string Name)> studentInfoLookup)
    {
        Specs.Clear();

        foreach (var special in specials)
        {
            if (studentInfoLookup.TryGetValue(special.StudentID, out var studentInfo))
            {
                Specs.Add(new StudentSpecialViewModel(special, studentInfo.Grade, studentInfo.ClassNum, studentInfo.Number, studentInfo.Name));
            }
            else
            {
                Specs.Add(new StudentSpecialViewModel(special));
            }
        }

        // 전체 선택 체크박스 초기화
        if (ChkSelectAll != null)
        {
            ChkSelectAll.IsChecked = false;
        }

        UpdateSubjectColumn();
    }

    /// <summary>
    /// 선택된 항목 초기화
    /// </summary>
    public void ClearSelection()
    {
        foreach (var spec in Specs)
        {
            spec.IsSelected = false;
        }
        
        if (ChkSelectAll != null)
        {
            ChkSelectAll.IsChecked = false;
        }
    }

    #endregion
}
