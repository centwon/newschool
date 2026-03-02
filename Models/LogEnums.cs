namespace NewSchool.Models
{
    /// <summary>
    /// 학생 정보 표시 모드
    /// LogListViewer에서 학생 정보를 어떻게 표시할지 결정
    /// </summary>
    public enum StudentInfoMode
    {
        /// <summary>학생 정보 모두 숨김 (개인별 보기)</summary>
        HideAll,

        /// <summary>학년도, 학기, 학년, 반, 번호, 이름 모두 표시</summary>
        ShowAll,

        /// <summary>학년, 반, 번호, 이름 표시</summary>
        GradeClassNumName,

        /// <summary>반, 번호, 이름 표시</summary>
        ClassNumName,

        /// <summary>번호, 이름 표시</summary>
        NumName,

        /// <summary>이름만 표시</summary>
        NameOnly
    }

    /// <summary>
    /// 학생 기록 카테고리
    /// </summary>
    public enum LogCategory
    {
        /// <summary>전체 (모든 카테고리)</summary>
        전체,

        /// <summary>교과활동 (과목별 세부능력 및 특기사항)</summary>
        교과활동,

        /// <summary>개인별 세특 (개인별 세부능력 및 특기사항)</summary>
        개인별세특,

        /// <summary>창의적 체험활동 - 자율활동</summary>
        자율활동,

        /// <summary>창의적 체험활동 - 동아리활동</summary>
        동아리활동,

        /// <summary>창의적 체험활동 - 봉사활동</summary>
        봉사활동,

        /// <summary>창의적 체험활동 - 진로활동</summary>
        진로활동,

        /// <summary>행동특성 및 종합의견</summary>
        종합의견,

        /// <summary>상담기록</summary>
        상담기록,

        /// <summary>기타</summary>
        기타
    }
}
