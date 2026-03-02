// ========================================
// SchoolDB.cs
// ========================================

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Controls;

namespace NewSchool.Models;


#region 기초 데이터 형식

/// <summary>
/// 식단정보
/// </summary>
[WinRT.GeneratedBindableCustomProperty]
public sealed partial class SchoolMeal
{
    public string ATPT_OFCDC_SC_CODE { get; set; } = string.Empty; //        1	    ATPT_OFCDC_SC_CODE 시도교육청코드
    public string ATPT_OFCDC_SC_NM { get; set; } = string.Empty;        //2	    ATPT_OFCDC_SC_NM 시도교육청명

    public string SD_SCHUL_CODE { get; set; } = string.Empty;        //3	    SD_SCHUL_CODE 표준학교코드

    public string SCHUL_NM { get; set; } = string.Empty;        //4	    SCHUL_NM 학교명

    public string MMEAL_SC_NM { get; set; } = string.Empty;        //6	    MMEAL_SC_NM 식사명

    public DateTime MLSV_YMD { get; set; } = DateTime.Today;       //7	    MLSV_YMD 급식일자

    public string DDISH_NM { get; set; } = string.Empty;         //9	    DDISH_NM 요리명

    /// <summary>
    /// 한 줄 표시용 텍스트 (예: "중식: 현미밥, 된장찌개, 김치")
    /// </summary>
    public string DisplayText => $"{MMEAL_SC_NM}: {DDISH_NM.Replace("\r\n", ", ").Replace("\n", ", ").Replace("\r", ", ")}";

    //5	    MMEAL_SC_CODE 식사코드
    //8	    MLSV_FGR 급식인원수
    //10	    ORPLC_INFO 원산지정보
    //11	    CAL_INFO 칼로리정보
    //12	    NTR_INFO 영양정보
    //13	    MLSV_FROM_YMD 급식시작일자
    //14	    MLSV_TO_YMD 급식종료일자
}

public class Classroom(int x, int y)
{
    public int Grade { get; set; } = x;
    public int Class { get; set; } = y;
}

public class Period
{
    public int Index { get; set; }
    public string Name { get; set; } = string.Empty;
    public TimeSpan Time { get; set; }
    public TimeSpan Duration { get; set; }
}

#endregion

#region Core Models


public class ClassAssignment : NotifyPropertyChangedBase, IEntity
{
    private int _no = -1;
    private int _year;
    private int _grade;
    private int _class;
    private int _number;
    private string _student = string.Empty;
    private string _name = string.Empty;

    public int No { get => _no; set => SetProperty(ref _no, value); }
    public int Year { get => _year; set => SetProperty(ref _year, value); }
    public int Grade { get => _grade; set => SetProperty(ref _grade, value); }
    public int Class { get => _class; set => SetProperty(ref _class, value); }
    public int Number { get => _number; set => SetProperty(ref _number, value); }
    public string Student { get => _student; set => SetProperty(ref _student, value); }
    public string Name { get => _name; set => SetProperty(ref _name, value); }
}

public class Subject : NotifyPropertyChangedBase, IEntity
{
    private int _no = -1;
    private string _curriculum = string.Empty;
    private string _name = string.Empty;
    private int _unit;
    private string _remark = string.Empty;

    public int No { get => _no; set => SetProperty(ref _no, value); }
    public string Curriculum { get => _curriculum; set => SetProperty(ref _curriculum, value); }
    public string Name { get => _name; set => SetProperty(ref _name, value); }
    public int Unit { get => _unit; set => SetProperty(ref _unit, value); }
    public string Remark { get => _remark; set => SetProperty(ref _remark, value); }
}


public class CourseAssignment : NotifyPropertyChangedBase, IEntity
{
    private int _no = -1;
    private string _student = string.Empty;
    private int _course;
    private string _remark = string.Empty;

    public int No { get => _no; set => SetProperty(ref _no, value); }
    public string Student { get => _student; set => SetProperty(ref _student, value); }
    public int Course { get => _course; set => SetProperty(ref _course, value); }
    public string Remark { get => _remark; set => SetProperty(ref _remark, value); }
}



#endregion
