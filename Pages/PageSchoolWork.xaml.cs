using System;
using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NewSchool.Board.Pages;
using NewSchool.Models;

namespace NewSchool.Pages;

/// <summary>
/// 학교 업무 관리 페이지 (대시보드형)
/// - 좌측: 할 일 + 일정 (KAgendaControl) + 메모 (MemoBoard)
/// - 우측: 업무 게시판 (PostListPage 임베드, 카테고리=업무, 주제 필터 표시)
/// </summary>
public sealed partial class PageSchoolWork : Page, NewSchool.Controls.IUnsavedWork
{
    private bool _isBoardInitialized;

    // 메뉴 이동·앱 닫기는 WorkFrame 에 놓인 이 페이지만 본다. 품은 게시판에서 [새 글 쓰기]·[수정] 을
    // 누르면 글 편집 화면이 BoardFrame 안에서 열리므로, 그 판정을 그대로 넘겨야 한다 — 예전에는
    // 쓰던 글을 두고 메뉴를 눌러도 묻지 않고 사라졌다(2026-09-25 실측).
    public bool HasUnsavedWork =>
        BoardFrame.Content is NewSchool.Controls.IUnsavedWork work && work.HasUnsavedWork;

    public string UnsavedWorkMessage =>
        (BoardFrame.Content as NewSchool.Controls.IUnsavedWork)?.UnsavedWorkMessage ?? "";

    public PageSchoolWork()
    {
        InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        // 페이지 헤더 날짜 표시
        TxtPageDate.Text = DateTime.Today.ToString("yyyy년 M월 d일 (ddd)");

        // 할 일 목록 로드
        try
        {
            await AgendaControl.LoadPendingAndFutureAsync();
        }
        catch (Exception ex)
        {
            NewSchool.Logging.Log.Error("PageSchoolWork", "할 일을 읽지 못했다 — 할 일이 없는 것처럼 보인다", ex);
        }

        // 업무 게시판 초기화 (한 번만)
        if (!_isBoardInitialized)
        {
            _isBoardInitialized = true;

            BoardFrame.Navigate(typeof(PostListPage), new PostListPageParameter
            {
                Category = CategoryNames.Work,
                IsEmbedded = true,
                AllowCategoryChange = false,
                AllowViewModeChange = true,
                ShowSubjectFilter = true,
                ViewMode = Board.Models.BoardViewMode.Table
            });
        }
    }
}
