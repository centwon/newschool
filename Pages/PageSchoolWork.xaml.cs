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
public sealed partial class PageSchoolWork : Page
{
    private bool _isBoardInitialized;

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
            Debug.WriteLine($"[PageSchoolWork] 할 일 로드 실패: {ex.Message}");
        }

        // 업무 게시판 초기화 (한 번만)
        if (!_isBoardInitialized)
        {
            _isBoardInitialized = true;

            BoardFrame.Navigate(typeof(PostListPage), new PostListPageParameter
            {
                Category = CategoryNames.Work,
                Title = "업무 게시판",
                IsEmbedded = true,
                AllowCategoryChange = false,
                AllowViewModeChange = true,
                ShowSubjectFilter = true,
                ViewMode = Board.Models.BoardViewMode.Table
            });
        }
    }
}
