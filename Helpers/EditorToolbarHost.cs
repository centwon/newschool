using System;
using NewSchool.Controls;
using WinUIRichEditor.Controls;

namespace NewSchool.Helpers;

/// <summary>
/// 서식 편집기 툴바의 파일 단추(그림 넣기·내보내기·가져오기·인쇄)를 살린다.
///
/// <para>툴바는 창 핸들(<c>WindowHandle</c>)이 없으면 파일 선택 창을 띄우지 못해 내보내기·가져오기가
/// 눌러도 아무 일도 하지 않고 그림 단추는 꺼져 있다. 인쇄 단추는 <c>PrintRequested</c> 처리기가
/// 붙어야 보인다. 예전에는 둘 다 넘기지 않아 단추가 먹통이었다(2026-10-18).</para>
/// </summary>
public static class EditorToolbarHost
{
    /// <param name="jobTitle">인쇄 작업 이름(프린터 대기열에 보이는 것) — 누를 때의 제목을 쓰도록 함수로 받는다.</param>
    public static void Attach(RichEditorToolbar toolbar, RichEditor editor, nint hwnd, Func<string> jobTitle)
    {
        toolbar.WindowHandle = hwnd;
        toolbar.PrintRequested += async (_, _) =>
        {
            try
            {
                if (!await RichEditorPrintHelper.ShowPrintUIAsync(editor, hwnd, jobTitle()))
                    await MessageBox.ShowErrorAsync("이 PC 에서는 인쇄 창을 열 수 없습니다.");
            }
            catch (Exception ex)
            {
                await MessageBox.ShowErrorAsync($"인쇄 중 오류가 발생했습니다.\n{ex.Message}", ex);
            }
        };
    }
}
