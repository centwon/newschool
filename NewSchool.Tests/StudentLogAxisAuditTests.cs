using System.Collections.Generic;
using System.ComponentModel;
using NewSchool.Models;
using NewSchool.ViewModels;
using Xunit;

namespace NewSchool.Tests;

/// <summary>
/// 누가기록·학생부 축 전수 감사(2026-09-02)에서 드러난 결함을 못박는다.
///
/// <para>여기 모인 것들은 전부 <b>조용히 틀리는</b> 종류였다 — 예외도 오류 메시지도 없이
/// 칸이 비어 나가거나, 넣은 값이 아무 데도 도착하지 않았다. 그래서 규칙을 코드 주석이
/// 아니라 테스트로 남긴다.</para>
/// </summary>
public class StudentLogAxisAuditTests
{
    /// <summary><paramref name="mutate"/> 를 실행하는 동안 올라온 PropertyChanged 이름들.</summary>
    private static List<string> Capture(INotifyPropertyChanged model, System.Action mutate)
    {
        var seen = new List<string>();
        void Handler(object? s, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != null) seen.Add(e.PropertyName);
        }

        model.PropertyChanged += Handler;
        try { mutate(); }
        finally { model.PropertyChanged -= Handler; }
        return seen;
    }

    /// <summary>
    /// <b>규칙: 동아리명은 <c>ClubName</c> 한 칸에만 산다.</b>
    ///
    /// <para>예전에는 넣는 곳이 경로마다 달랐다 — 단일 입력은 <c>ClubName</c>, 일괄 입력은
    /// <c>ActivityName</c>. 그런데 읽는 곳(목록·인쇄·엑셀)은 또 <c>SubjectName</c> 이라
    /// 화면의 "동아리" 칸과 엑셀의 "동아리" 열이 언제나 비어 있었다.</para>
    /// </summary>
    [Fact]
    public void 동아리활동은_동아리명을_과목칸_대신_보여준다()
    {
        var club = new StudentLog
        {
            Category = LogCategory.동아리활동,
            ClubNo = 7,
            ClubName = "천체관측반"
        };

        Assert.Equal("천체관측반", club.SubjectOrClubDisplay);
    }

    [Fact]
    public void 동아리활동이_아니면_과목명을_보여준다()
    {
        var lesson = new StudentLog
        {
            Category = LogCategory.교과활동,
            SubjectName = "지구과학",
            ClubName = "천체관측반"   // 남아 있어도 교과에서는 쓰지 않는다
        };

        Assert.Equal("지구과학", lesson.SubjectOrClubDisplay);
    }

    /// <summary>
    /// 규칙이 세워지기 전에 저장된 동아리 기록은 <c>ClubName</c> 이 비어 있다 —
    /// 그때는 <c>SubjectName</c> 으로 물러서야 옛 기록이 빈칸으로 나가지 않는다.
    /// </summary>
    [Fact]
    public void 동아리명이_비면_과목명으로_물러선다()
    {
        var old = new StudentLog
        {
            Category = LogCategory.동아리활동,
            SubjectName = "방송반",
            ClubName = string.Empty
        };

        Assert.Equal("방송반", old.SubjectOrClubDisplay);
    }

    /// <summary>
    /// 표시값은 계산 속성이라 자기 입력이 바뀌면 알려야 한다
    /// (<see cref="ComputedPropertyNotifyTests"/> 와 같은 규칙 — <c>Mode=OneWay</c> 로
    /// 걸었을 때 조용히 안 도는 것을 막는다).
    /// </summary>
    [Fact]
    public void 표시값은_자기_입력이_바뀌면_알린다()
    {
        var log = new StudentLog { Category = LogCategory.동아리활동 };

        Assert.Contains(nameof(StudentLog.SubjectOrClubDisplay),
            Capture(log, () => log.ClubName = "천체관측반"));
        Assert.Contains(nameof(StudentLog.SubjectOrClubDisplay),
            Capture(log, () => log.SubjectName = "지구과학"));
        Assert.Contains(nameof(StudentLog.SubjectOrClubDisplay),
            Capture(log, () => log.Category = LogCategory.교과활동));
    }

    /// <summary>ViewModel 은 판단을 다시 하지 않고 모델의 한 벌을 그대로 통과시킨다.</summary>
    [Fact]
    public void ViewModel_은_모델의_표시값을_그대로_쓴다()
    {
        var log = new StudentLog
        {
            Category = LogCategory.동아리활동,
            ClubName = "천체관측반",
            SubjectName = "지구과학"
        };

        var vm = new StudentLogViewModel(log);

        Assert.Equal(log.SubjectOrClubDisplay, vm.SubjectOrClubDisplay);
        Assert.Equal("천체관측반", vm.SubjectOrClubDisplay);
    }

    /// <summary>
    /// <b>규칙: <see cref="StudentLog"/> 의 문자열 칸은 null 이 되지 않는다.</b>
    ///
    /// <para>이것을 못박는 이유는 <c>a ?? b</c> 로 물러서는 코드가 실제로 있었기 때문이다.
    /// 내보내기의 <c>Description ?? Log</c>·<c>Log ?? Description</c> 은 초기값이
    /// <c>string.Empty</c> 라 <b>한 번도 물러서지 않았고</b>, 상담 내용을 "기록" 칸에만
    /// 적은 기록은 엑셀에서 통째로 빈칸이 됐다. 빈 칸을 가려내려면 널이 아니라
    /// <c>IsNullOrWhiteSpace</c> 를 봐야 한다.</para>
    /// </summary>
    [Fact]
    public void 문자열_칸은_기본값이_빈_문자열이라_널병합이_물러서지_않는다()
    {
        var log = new StudentLog();

        Assert.NotNull(log.Log);
        Assert.NotNull(log.Description);
        Assert.NotNull(log.SubjectName);
        Assert.NotNull(log.ClubName);
        Assert.NotNull(log.ActivityName);
        Assert.NotNull(log.Topic);

        // 그래서 이 표현은 "비었을 때 물러서기"가 되지 않는다 — 늘 앞엣것을 돌려준다.
        Assert.Equal(string.Empty, log.Description ?? log.Log);
    }

    /// <summary>
    /// <b>규칙: "구조화된 기록인가"는 <see cref="StudentLog.HasStructuredData"/> 한 곳에서만 본다.</b>
    ///
    /// <para>예전에는 <c>Summary</c>·<c>DraftSummary</c> 가 앞의 세 칸(활동명·주제·활동내용)만
    /// 따로 보았다. 그래서 역할이나 기른 능력만 적은 기록은 <c>HasStructuredData()</c> 가
    /// 참인데도 요약이 <c>Log</c> 로 떨어졌고 — 내보내기·인쇄는 그 참을 믿고 이 값을
    /// "학생부초안" 칸에 넣었으므로, 교사가 적은 역할·장점이 어디에도 나타나지 않았다.</para>
    /// </summary>
    [Fact]
    public void 역할만_적어도_요약에_들어간다()
    {
        var log = new StudentLog
        {
            Category = LogCategory.자율활동,
            Role = "팀장",
            StrengthShown = "주도성"
        };

        Assert.True(log.HasStructuredData());

        // 예전에는 둘 다 빈 Log 를 돌려주어 입력이 통째로 사라졌다.
        Assert.Contains("팀장", log.Summary);
        Assert.Contains("팀장", log.DraftSummary);
        Assert.Contains("주도성", log.DraftSummary);
    }

    /// <summary>
    /// 앞머리(활동명·주제·활동내용)가 없으면 쉼표가 붕 뜬다 —
    /// "자율활동 , 팀장." 이 아니라 "자율활동 팀장…" 으로 이어야 한다.
    /// </summary>
    [Fact]
    public void 앞머리가_없으면_쉼표로_잇지_않는다()
    {
        var log = new StudentLog { Category = LogCategory.자율활동, Role = "팀장" };

        Assert.DoesNotContain(" , ", log.DraftSummary);
        Assert.DoesNotContain("  ", log.DraftSummary);
    }

    /// <summary>
    /// <b>규칙: 카테고리 색은 화면과 인쇄가 같은 표를 쓴다.</b>
    ///
    /// <para>예전에는 인쇄 서비스가 색표를 하나 더 들고 있어 종이와 화면이 어긋났다 —
    /// 자율활동은 화면에서 하늘색인데 인쇄하면 녹색, 동아리활동은 보라인데 주황,
    /// 봉사활동은 녹색인데 분홍이었다.</para>
    ///
    /// <para>여기서는 표가 <b>QuestPDF 가 받을 수 있는 꼴</b>인지만 본다 — 인쇄 쪽은
    /// 알파 두 자리만 떼어 그대로 쓰므로, 이 형식이 지켜지면 두 곳이 갈라질 수 없다.</para>
    /// </summary>
    [Theory]
    [InlineData(LogCategory.자율활동)]
    [InlineData(LogCategory.동아리활동)]
    [InlineData(LogCategory.봉사활동)]
    [InlineData(LogCategory.개인별세특)]
    [InlineData(LogCategory.전체)]
    public void 카테고리_색은_알파를_뗄_수_있는_꼴이다(LogCategory category)
    {
        var argb = StudentLogViewModel.ToCategoryColor(category);

        Assert.StartsWith("#", argb);
        Assert.Equal(9, argb.Length);                     // #AARRGGBB
        Assert.Equal(7, ("#" + argb.Substring(3)).Length); // → #RRGGBB
    }

    /// <summary>
    /// <b>규칙: 진로활동의 <c>Title</c>(희망분야)은 자동으로 채우지 않는다.</b>
    ///
    /// <para><c>NeisHelper</c> 정의표에서 진로활동만 <c>TitleCountsInBytes</c> 라,
    /// Title 에 넣은 글자가 특기사항과 합쳐 한도를 먹는다. 예전에는 누가기록 화면이
    /// 새 학생부 칸을 만들 때 "{학생이름} {영역}" 을 Title 에 넣어, 희망분야 칸에
    /// "홍길동 진로활동"이 뜨고 그만큼 분량이 깎였다.</para>
    /// </summary>
    [Fact]
    public void 진로활동은_희망분야가_분량에_합산된다()
    {
        Assert.True(Helpers.NeisHelper.TitleCountsInBytes("진로활동"));
        Assert.False(Helpers.NeisHelper.TitleCountsInBytes("교과활동"));

        int withTitle = Helpers.NeisHelper.CountSpecBytes("진로활동", "홍길동 진로활동", "내용");
        int withoutTitle = Helpers.NeisHelper.CountSpecBytes("진로활동", string.Empty, "내용");

        // 자동으로 채워 넣은 제목만큼 쓸 수 있는 분량이 줄어든다
        Assert.True(withTitle > withoutTitle);
    }
}
