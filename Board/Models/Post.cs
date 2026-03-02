using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;

namespace NewSchool.Board
{
    /// <summary>
    /// Post 모델 - 게시글 데이터
    /// </summary>
    [Microsoft.UI.Xaml.Data.Bindable]
    public class Post : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private int _no = -1;
        public int No
        {
            get => _no;
            set
            {
                if (_no != value)
                {
                    _no = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _user = string.Empty;
        public string User
        {
            get => _user;
            set
            {
                if (_user != value)
                {
                    _user = value;
                    OnPropertyChanged();
                }
            }
        }

        private DateTime _dateTime = DateTime.Now;
        public DateTime DateTime
        {
            get => _dateTime;
            set
            {
                if (_dateTime != value)
                {
                    _dateTime = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _category = string.Empty;
        public string Category
        {
            get => _category;
            set
            {
                if (_category != value)
                {
                    _category = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _subject = string.Empty;
        public string Subject
        {
            get => _subject;
            set
            {
                if (_subject != value)
                {
                    _subject = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _title = string.Empty;
        public string Title
        {
            get => _title;
            set
            {
                if (_title != value)
                {
                    _title = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _content = string.Empty;
        public string Content
        {
            get => _content;
            set
            {
                if (_content != value)
                {
                    _content = value;
                    OnPropertyChanged();
                }
            }
        }

        private int _refNo;
        public int RefNo
        {
            get => _refNo;
            set
            {
                if (_refNo != value)
                {
                    _refNo = value;
                    OnPropertyChanged();
                }
            }
        }

        private int _replyOrder;
        public int ReplyOrder
        {
            get => _replyOrder;
            set
            {
                if (_replyOrder != value)
                {
                    _replyOrder = value;
                    OnPropertyChanged();
                }
            }
        }

        private int _depth;
        public int Depth
        {
            get => _depth;
            set
            {
                if (_depth != value)
                {
                    _depth = value;
                    OnPropertyChanged();
                }
            }
        }

        private int _readCount;
        public int ReadCount
        {
            get => _readCount;
            set
            {
                if (_readCount != value)
                {
                    _readCount = value;
                    OnPropertyChanged();
                }
            }
        }

        private bool _hasFile;
        public bool HasFile
        {
            get => _hasFile;
            set
            {
                if (_hasFile != value)
                {
                    _hasFile = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(FileIconVisibility));
                }
            }
        }

        private bool _hasComment;
        public bool HasComment
        {
            get => _hasComment;
            set
            {
                if (_hasComment != value)
                {
                    _hasComment = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(CommentIconVisibility));
                }
            }
        }

        private bool _isCompleted;
        /// <summary>
        /// 완료 여부 (메모용)
        /// </summary>
        public bool IsCompleted
        {
            get => _isCompleted;
            set
            {
                if (_isCompleted != value)
                {
                    _isCompleted = value;
                    OnPropertyChanged();
                }
            }
        }

        // UI 바인딩용 속성
        public Visibility FileIconVisibility => HasFile ? Visibility.Visible : Visibility.Collapsed;
        public Visibility CommentIconVisibility => HasComment ? Visibility.Visible : Visibility.Collapsed;
        public string DateTimeDisplay => DateTime.ToString("M/d HH:mm");

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
