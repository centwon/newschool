namespace NewSchool.Helpers;

/// <summary>
/// QuestPDF 전역 설정 — 앱 시작(<c>App</c> 생성자)과 시험이 <b>같은 것</b>을 부른다.
///
/// <para>시험이 앱과 다른 설정으로 PDF 를 만들면 "시험은 통과, 앱은 실패" 가 된다. 실제로 그랬다:
/// QuestPDF 2026.9 로 올린 뒤 <b>한글이 든 PDF 가 전부 만들어지지 않았는데</b>(인쇄 버튼마다
/// "PDF 생성 오류 … Missing glyphs" 와 0바이트 파일) 시험 893개는 한글 PDF 를 만들어 보지 않아
/// 모두 통과했다. 모래상자에서 학생카드를 실제로 뽑아 보고서야 드러났다(2026-09-24).</para>
/// </summary>
public static class PdfLibrarySetup
{
    public static void Apply()
    {
        // Community 자격 근거(LICENSE.md v3.0, 2026-07-06 시행): 개인이 만드는
        // 프로젝트이고 연 매출이 100만 달러에 못 미친다(1항). 사용자인 학교는
        // 앱을 쓸 뿐 QuestPDF API 를 직접 부르지 않으므로 7항(전이 의존)에 든다.
        // ⚠ 이 라이선스는 OSI 승인 오픈소스가 아니며 MIT 가 적용되지 않는다.
        // 자격을 잃으면 90일 안에 유료 라이선스를 사야 한다.
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

        // ⚠ QuestPDF 2026.9 의 파괴적 변경 두 가지를 예전 동작으로 되돌린다.
        //
        // 1) 시스템 글꼴을 기본으로 쓰지 않게 됐다. 내장 글꼴은 Lato 뿐이라 한글 글리프가 없다 —
        //    예전에는 시스템 글꼴에서 맑은 고딕을 찾아 자동으로 임베드했다(53차 실측). 이 앱은
        //    한국어 Windows 데스크톱에서만 돌므로 "서버·컨테이너에 글꼴이 없다" 는 걱정이 없다.
        QuestPDF.Settings.UseSystemFonts = true;

        // 2) 글꼴에 없는 글자가 하나라도 있으면 생성을 멈추게 됐다(예전에는 디버거가 붙었을 때만).
        //    학생 메모·기록은 사람이 쓴 글이라 이모지나 드문 한자가 섞인다 — 그 한 글자 때문에
        //    학급 전체 인쇄가 실패하는 것보다, 그 글자만 빈 칸으로 찍히는 쪽이 낫다.
        QuestPDF.Settings.ThrowOnMissingTextGlyphs = false;
    }
}
