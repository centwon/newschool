using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;

namespace NewSchool.Board
{
    public class Comment : INotifyPropertyChanged
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

        private int _post;
        public int Post
        {
            get => _post;
            set
            {
                if (_post != value)
                {
                    _post = value;
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

        private string _fileName = string.Empty;
        public string FileName
        {
            get => _fileName;
            set
            {
                if (_fileName != value)
                {
                    _fileName = value;
                    OnPropertyChanged();
                }
            }
        }

        private int _fileSize;
        public int FileSize
        {
            get => _fileSize;
            set
            {
                if (_fileSize != value)
                {
                    _fileSize = value;
                    OnPropertyChanged();
                }
            }
        }

        // UI 바인딩용 속성
        public Visibility FileIconVisibility => HasFile ? Visibility.Visible : Visibility.Collapsed;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
