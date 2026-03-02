using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace NewSchool.Validation
{
    /// <summary>
    /// 검증 헬퍼 - 데이터 유효성 검사 (Native AOT 호환)
    /// </summary>
    public static class ValidationHelper
    {
        #region Post Validation

        /// <summary>
        /// Post 검증
        /// </summary>
        public static ValidationResult ValidatePost(Board.Post post)
        {
            var result = new ValidationResult();

            // 제목 검증
            if (string.IsNullOrWhiteSpace(post.Title))
            {
                result.AddError("Title", "제목을 입력하세요.");
            }
            else if (post.Title.Length > 200)
            {
                result.AddError("Title", "제목은 200자를 초과할 수 없습니다.");
            }

            // 내용 검증
            if (string.IsNullOrWhiteSpace(post.Content))
            {
                result.AddError("Content", "내용을 입력하세요.");
            }
            else if (post.Content.Length > 10000)
            {
                result.AddError("Content", "내용은 10,000자를 초과할 수 없습니다.");
            }

            // 카테고리 검증
            if (string.IsNullOrWhiteSpace(post.Category))
            {
                result.AddError("Category", "카테고리를 선택하세요.");
            }

            // 사용자 검증
            if (string.IsNullOrWhiteSpace(post.User))
            {
                result.AddError("User", "작성자 정보가 없습니다.");
            }

            // 금지어 검증
            if (ContainsForbiddenWords(post.Title) || ContainsForbiddenWords(post.Content))
            {
                result.AddError("Content", "금지된 단어가 포함되어 있습니다.");
            }

            return result;
        }

        #endregion

        #region Comment Validation

        /// <summary>
        /// Comment 검증
        /// </summary>
        public static ValidationResult ValidateComment(Board.Comment comment)
        {
            var result = new ValidationResult();

            // 내용 검증
            if (string.IsNullOrWhiteSpace(comment.Content))
            {
                result.AddError("Content", "댓글 내용을 입력하세요.");
            }
            else if (comment.Content.Length > 1000)
            {
                result.AddError("Content", "댓글은 1,000자를 초과할 수 없습니다.");
            }

            // Post 번호 검증
            if (comment.Post <= 0)
            {
                result.AddError("Post", "유효하지 않은 게시글입니다.");
            }

            // 금지어 검증
            if (ContainsForbiddenWords(comment.Content))
            {
                result.AddError("Content", "금지된 단어가 포함되어 있습니다.");
            }

            return result;
        }

        #endregion

        #region File Validation

        /// <summary>
        /// 파일 검증
        /// </summary>
        public static ValidationResult ValidateFile(string fileName, long fileSize)
        {
            var result = new ValidationResult();

            // 파일명 검증
            if (string.IsNullOrWhiteSpace(fileName))
            {
                result.AddError("FileName", "파일명이 없습니다.");
                return result;
            }

            // 파일 크기 검증 (10MB 제한)
            const long maxSize = 10 * 1024 * 1024;
            if (fileSize > maxSize)
            {
                result.AddError("FileSize", $"파일 크기는 {maxSize / (1024 * 1024)}MB를 초과할 수 없습니다.");
            }

            // 확장자 검증
            string extension = System.IO.Path.GetExtension(fileName).ToLowerInvariant();
            var allowedExtensions = new[]
            {
                ".jpg", ".jpeg", ".png", ".gif", ".bmp",
                ".pdf", ".doc", ".docx", ".xls", ".xlsx",
                ".txt", ".zip", ".rar", ".7z"
            };

            if (!allowedExtensions.Contains(extension))
            {
                result.AddError("FileType", $"허용되지 않는 파일 형식입니다: {extension}");
            }

            // 위험한 파일명 문자 검증
            if (ContainsDangerousCharacters(fileName))
            {
                result.AddError("FileName", "파일명에 사용할 수 없는 문자가 포함되어 있습니다.");
            }

            return result;
        }

        #endregion

        #region String Validation

        /// <summary>
        /// 이메일 검증
        /// </summary>
        public static bool IsValidEmail(string email)
        {
            if (string.IsNullOrWhiteSpace(email))
                return false;

            try
            {
                var regex = new Regex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$");
                return regex.IsMatch(email);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// URL 검증
        /// </summary>
        public static bool IsValidUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return false;

            return Uri.TryCreate(url, UriKind.Absolute, out var uriResult)
                && (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps);
        }

        /// <summary>
        /// 전화번호 검증 (한국)
        /// </summary>
        public static bool IsValidPhoneNumber(string phoneNumber)
        {
            if (string.IsNullOrWhiteSpace(phoneNumber))
                return false;

            var regex = new Regex(@"^01[0-9]-?[0-9]{3,4}-?[0-9]{4}$");
            return regex.IsMatch(phoneNumber.Replace("-", ""));
        }

        #endregion

        #region Security Validation

        /// <summary>
        /// 금지어 포함 여부
        /// </summary>
        private static bool ContainsForbiddenWords(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            // 실제 프로젝트에서는 데이터베이스나 설정 파일에서 로드
            var forbiddenWords = new[] { "금지어1", "금지어2", "스팸" };

            text = text.ToLowerInvariant();
            return forbiddenWords.Any(word => text.Contains(word.ToLowerInvariant()));
        }

        /// <summary>
        /// 위험한 문자 포함 여부
        /// </summary>
        private static bool ContainsDangerousCharacters(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            var dangerousChars = new[] { '<', '>', ':', '"', '/', '\\', '|', '?', '*' };
            return text.Any(c => dangerousChars.Contains(c));
        }

        /// <summary>
        /// SQL Injection 패턴 검사
        /// </summary>
        public static bool ContainsSqlInjectionPattern(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            var patterns = new[]
            {
                @"(\bOR\b|\bAND\b).*[=<>]",
                @"--",
                @";.*DROP",
                @";.*DELETE",
                @";.*UPDATE",
                @";.*INSERT",
                @"UNION.*SELECT",
                @"SELECT.*FROM"
            };

            text = text.ToUpperInvariant();
            return patterns.Any(pattern => Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase));
        }

        /// <summary>
        /// XSS 패턴 검사
        /// </summary>
        public static bool ContainsXssPattern(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            var patterns = new[]
            {
                @"<script[^>]*>",
                @"javascript:",
                @"onerror\s*=",
                @"onload\s*=",
                @"<iframe[^>]*>",
                @"<embed[^>]*>"
            };

            return patterns.Any(pattern => Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase));
        }

        #endregion

        #region Range Validation

        /// <summary>
        /// 범위 검증
        /// </summary>
        public static bool IsInRange(int value, int min, int max)
        {
            return value >= min && value <= max;
        }

        /// <summary>
        /// 길이 검증
        /// </summary>
        public static bool IsValidLength(string text, int minLength, int maxLength)
        {
            if (text == null)
                return false;

            return text.Length >= minLength && text.Length <= maxLength;
        }

        #endregion

        #region Custom Validators

        /// <summary>
        /// 카테고리 유효성 검증
        /// </summary>
        public static ValidationResult ValidateCategory(string category, List<string> allowedCategories)
        {
            var result = new ValidationResult();

            if (string.IsNullOrWhiteSpace(category))
            {
                result.AddError("Category", "카테고리를 선택하세요.");
            }
            else if (!allowedCategories.Contains(category))
            {
                result.AddError("Category", "유효하지 않은 카테고리입니다.");
            }

            return result;
        }

        #endregion
    }

    #region Validation Result

    /// <summary>
    /// 검증 결과
    /// </summary>
    public class ValidationResult
    {
        private readonly Dictionary<string, List<string>> _errors;

        public bool IsValid => _errors.Count == 0;
        public Dictionary<string, List<string>> Errors => _errors;

        public ValidationResult()
        {
            _errors = new Dictionary<string, List<string>>();
        }

        public void AddError(string field, string message)
        {
            if (!_errors.ContainsKey(field))
            {
                _errors[field] = new List<string>();
            }

            _errors[field].Add(message);
        }

        public string GetFirstError()
        {
            if (_errors.Count == 0)
                return string.Empty;

            var firstError = _errors.First();
            return firstError.Value.First();
        }

        public string GetAllErrors()
        {
            if (_errors.Count == 0)
                return string.Empty;

            var messages = new List<string>();
            foreach (var error in _errors)
            {
                foreach (var message in error.Value)
                {
                    messages.Add($"{error.Key}: {message}");
                }
            }

            return string.Join("\n", messages);
        }

        public List<string> GetErrors(string field)
        {
            return _errors.ContainsKey(field) ? _errors[field] : new List<string>();
        }
    }

    #endregion

    #region Usage Example

    public class ValidationUsageExample
    {
        public void ValidatePostExample()
        {
            var post = new Board.Post
            {
                Title = "테스트 게시글",
                Content = "내용",
                Category = "공지사항",
                User = "홍길동"
            };

            var result = ValidationHelper.ValidatePost(post);

            if (!result.IsValid)
            {
                Console.WriteLine("검증 실패:");
                Console.WriteLine(result.GetAllErrors());
            }
            else
            {
                Console.WriteLine("검증 성공!");
            }
        }

        public void ValidateFileExample()
        {
            string fileName = "document.pdf";
            long fileSize = 1024 * 1024 * 5; // 5MB

            var result = ValidationHelper.ValidateFile(fileName, fileSize);

            if (!result.IsValid)
            {
                Console.WriteLine($"파일 검증 실패: {result.GetFirstError()}");
            }
        }
    }

    #endregion
}
