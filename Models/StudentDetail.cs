using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace NewSchool.Models;

/// <summary>
/// 학생 상세 정보
/// Student와 1:1 관계 (선택적)
/// 보호자, 가족, 진로, 특기사항 등
/// </summary>
public class StudentDetail : NotifyPropertyChangedBase
{
    #region Fields

    private int _no = -1;
    private string _studentId = string.Empty;
    private string _fatherName = string.Empty;
    private string _fatherPhone = string.Empty;
    private string _fatherJob = string.Empty;
    private string _motherName = string.Empty;
    private string _motherPhone = string.Empty;
    private string _motherJob = string.Empty;
    private string _guardianName = string.Empty;
    private string _guardianPhone = string.Empty;
    private string _guardianRelation = string.Empty;
    private string _familyInfo = string.Empty;
    private string _friends = string.Empty;
    private string _interests = string.Empty;
    private string _talents = string.Empty;
    private string _careerGoal = string.Empty;
    private string _healthInfo = string.Empty;
    private string _allergies = string.Empty;
    private string _specialNeeds = string.Empty;
    private string _memo = string.Empty;
    private DateTime _createdAt = DateTime.Now;
    private DateTime _updatedAt = DateTime.Now;

    #endregion

    #region Properties - 기본 정보

    /// <summary>PK (자동 증가)</summary>
    public int No
    {
        get => _no;
        set => SetProperty(ref _no, value);
    }

    /// <summary>학생 ID (FK: Student.StudentID, UNIQUE)</summary>
    public string StudentID
    {
        get => _studentId;
        set => SetProperty(ref _studentId, value);
    }

    #endregion

    #region Properties - 부모 정보

    /// <summary>아버지 성함</summary>
    public string FatherName
    {
        get => _fatherName;
        set => SetProperty(ref _fatherName, value);
    }

    /// <summary>아버지 전화번호</summary>
    public string FatherPhone
    {
        get => _fatherPhone;
        set => SetProperty(ref _fatherPhone, value);
    }

    /// <summary>아버지 직업</summary>
    public string FatherJob
    {
        get => _fatherJob;
        set => SetProperty(ref _fatherJob, value);
    }

    /// <summary>어머니 성함</summary>
    public string MotherName
    {
        get => _motherName;
        set => SetProperty(ref _motherName, value);
    }

    /// <summary>어머니 전화번호</summary>
    public string MotherPhone
    {
        get => _motherPhone;
        set => SetProperty(ref _motherPhone, value);
    }

    /// <summary>어머니 직업</summary>
    public string MotherJob
    {
        get => _motherJob;
        set => SetProperty(ref _motherJob, value);
    }

    #endregion

    #region Properties - 보호자 정보

    /// <summary>보호자 성함 (부모가 아닌 경우)</summary>
    public string GuardianName
    {
        get => _guardianName;
        set => SetProperty(ref _guardianName, value);
    }

    /// <summary>보호자 전화번호</summary>
    public string GuardianPhone
    {
        get => _guardianPhone;
        set => SetProperty(ref _guardianPhone, value);
    }

    /// <summary>보호자 관계 (조부모, 친척 등)</summary>
    public string GuardianRelation
    {
        get => _guardianRelation;
        set => SetProperty(ref _guardianRelation, value);
    }

    #endregion

    #region Properties - 가정 환경

    /// <summary>가족 구성 및 환경</summary>
    public string FamilyInfo
    {
        get => _familyInfo;
        set => SetProperty(ref _familyInfo, value);
    }

    #endregion

    #region Properties - 교우 관계

    /// <summary>친한 친구들</summary>
    public string Friends
    {
        get => _friends;
        set => SetProperty(ref _friends, value);
    }

    #endregion

    #region Properties - 학생 특성

    /// <summary>관심사 및 취미</summary>
    public string Interests
    {
        get => _interests;
        set => SetProperty(ref _interests, value);
    }

    /// <summary>특기</summary>
    public string Talents
    {
        get => _talents;
        set => SetProperty(ref _talents, value);
    }

    /// <summary>진로 희망</summary>
    public string CareerGoal
    {
        get => _careerGoal;
        set => SetProperty(ref _careerGoal, value);
    }

    #endregion

    #region Properties - 건강 정보

    /// <summary>건강 상태 및 주의사항</summary>
    public string HealthInfo
    {
        get => _healthInfo;
        set => SetProperty(ref _healthInfo, value);
    }

    /// <summary>알레르기 정보</summary>
    public string Allergies
    {
        get => _allergies;
        set => SetProperty(ref _allergies, value);
    }

    /// <summary>특수 교육 대상 여부 및 내용</summary>
    public string SpecialNeeds
    {
        get => _specialNeeds;
        set => SetProperty(ref _specialNeeds, value);
    }

    #endregion

    #region Properties - 기타

    /// <summary>메모 (기타 상세 사항)</summary>
    public string Memo
    {
        get => _memo;
        set => SetProperty(ref _memo, value);
    }

    #endregion

    #region Properties - 메타 정보

    /// <summary>생성일시</summary>
    public DateTime CreatedAt
    {
        get => _createdAt;
        set => SetProperty(ref _createdAt, value);
    }

    /// <summary>수정일시</summary>
    public DateTime UpdatedAt
    {
        get => _updatedAt;
        set => SetProperty(ref _updatedAt, value);
    }

    #endregion

    #region Methods

    public override string ToString()
    {
        return $"StudentDetail for {StudentID}";
    }

    /// <summary>
    /// 명렬표의 "보호자" 칸에 넣을 <b>한 사람</b>을 고른다.
    ///
    /// <para><b>보호자는 보호자고 부모는 부모다 — 보호자가 부모가 아닐 수 있다.</b>
    /// 조부모·친척·위탁 가정·시설처럼 부모가 아닌 사람이 보호자인 경우가 실제로 있고,
    /// 그럴 때 교사는 <c>GuardianName</c> 칸에 그 사람을 적어 둔다. 그러므로
    /// <b>따로 적어 둔 보호자가 있으면 그 사람이 보호자다.</b></para>
    ///
    /// <para>⚠ 예전 순서는 <c>어머니 → 아버지 → 보호자</c> 였다. 그래서 보호자를 명시해
    /// 두어도 어머니 이름만 적혀 있으면 <b>그 보호자를 통째로 무시하고</b> 어머니를
    /// "보호자" 로 내보냈다 — 정확히 뒤집힌 순서였다. 부모는 <b>보호자가 비어 있을 때만</b>
    /// 물러설 자리다.</para>
    ///
    /// <para>⚠ 이름과 연락처를 <b>따로</b> 고르면 안 된다. 예전에는 두 함수가 각자 훑어서
    /// "이름은 어머니, 연락처는 보호자" 처럼 <b>서로 다른 사람</b>이 한 줄에 실릴 수 있었다.
    /// 여기서 사람을 한 번 고르고 두 값을 그 사람에게서 가져온다.</para>
    /// </summary>
    /// <returns>고른 사람의 이름·연락처·관계. 아무것도 없으면 전부 빈 문자열.</returns>
    public (string Name, string Phone, string Relation) ResolvePrimaryGuardian()
    {
        // 이름이든 연락처든 하나라도 적혀 있으면 "적어 둔 사람" 으로 본다.
        if (!string.IsNullOrEmpty(GuardianName) || !string.IsNullOrEmpty(GuardianPhone))
            return (GuardianName, GuardianPhone,
                    string.IsNullOrEmpty(GuardianRelation) ? "보호자" : GuardianRelation);

        if (!string.IsNullOrEmpty(MotherName) || !string.IsNullOrEmpty(MotherPhone))
            return (MotherName, MotherPhone, "모");

        if (!string.IsNullOrEmpty(FatherName) || !string.IsNullOrEmpty(FatherPhone))
            return (FatherName, FatherPhone, "부");

        return (string.Empty, string.Empty, string.Empty);
    }

    /// <summary>주 보호자의 연락처. 판단은 <see cref="ResolvePrimaryGuardian"/> 한 곳에 있다.</summary>
    public string GetPrimaryContact() => ResolvePrimaryGuardian().Phone;

    /// <summary>주 보호자의 이름. 판단은 <see cref="ResolvePrimaryGuardian"/> 한 곳에 있다.</summary>
    public string GetPrimaryGuardianName() => ResolvePrimaryGuardian().Name;

    /// <summary>
    /// 특이사항 여부 확인
    /// </summary>
    public bool HasSpecialConsiderations()
    {
        return !string.IsNullOrEmpty(HealthInfo)
            || !string.IsNullOrEmpty(Allergies)
            || !string.IsNullOrEmpty(SpecialNeeds);
    }

    #endregion
}
