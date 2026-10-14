using Microsoft.AspNetCore.Http;
using SharpPress.Models;
using System.Security.Claims;

namespace SharpPress.Services
{
    public class CategoryNode
    {
        public Category Category { get; set; }
        public List<CategoryNode> Children { get; set; } = new();
    }

    public interface IPostService
    {
        Task<List<Post>> GetAllPostsAsync();
        Task<List<Post>> GetPostsByStatusAsync(PostStatus status);
        Task<List<Post>> GetPublishedPostsAsync();
        Task<Post?> GetPostByIdAsync(int id);
        Task<Post?> GetPostBySlugAsync(string slug);
        Task<Post> CreatePostAsync(Post post);
        Task<Post> UpdatePostAsync(Post post);
        Task DeletePostAsync(int id);
        Task<string> GenerateUniqueSlugAsync(string title);
        Task<List<Post>> GetScheduledPostsDueAsync();
        Task PublishScheduledPostsAsync();
        Task SetPostCategoriesAsync(int postId, List<int> categoryIds);
        Task SetPostTagsAsync(int postId, List<int> tagIds);
        Task<List<Category>> GetPostCategoriesAsync(int postId);
        Task<List<Tag>> GetPostTagsAsync(int postId);
    }

    public interface ITaxonomyService
    {
        Task<List<Category>> GetAllCategoriesAsync();
        Task<Category?> GetCategoryByIdAsync(int id);
        Task<Category?> GetCategoryBySlugAsync(string slug);
        Task<List<Category>> GetChildCategoriesAsync(int parentId);
        Task<List<Category>> GetRootCategoriesAsync();
        Task<Category> CreateCategoryAsync(Category category);
        Task<Category> UpdateCategoryAsync(Category category);
        Task DeleteCategoryAsync(int id);
        Task<List<Category>> GetCategoryHierarchyAsync();
        List<CategoryNode> BuildCategoryTree(List<Category> categories);
        Task<List<Tag>> GetAllTagsAsync();
        Task<Tag?> GetTagByIdAsync(int id);
        Task<Tag?> GetTagBySlugAsync(string slug);
        Task<Tag> CreateTagAsync(Tag tag);
        Task<Tag> UpdateTagAsync(Tag tag);
        Task DeleteTagAsync(int id);
        Task<Tag> GetOrCreateTagByNameAsync(string name);
    }

    public interface IMediaService
    {
        Task<MediaItem> UploadAsync(Stream fileStream, string fileName, string contentType, string uploadedBy);
        Task<List<MediaItem>> GetAllAsync();
        Task<List<MediaItem>> GetByMediaTypeAsync(string mediaType);
        Task<MediaItem?> GetByIdAsync(int id);
        Task UpdateAltTextAsync(int id, string altText);
        Task DeleteAsync(int id);
        string GetMediaFilePath(string fileName);
        string GetThumbnailFilePath(string fileName);
        string GetContentType(string filePath);
    }

    public interface IEmailService
    {
        Func<string, string, string, bool, Task> EmailSendAction { get; }
        void ReplaceEmailSendAction(Func<string, string, string, bool, Task> action);
        Task SendAsync(string to, string subject, string body, bool isHtml = false);
    }

    public interface ICacheService
    {
        T Get<T>(string key);
        void Set<T>(string key, T value, TimeSpan? expiry = null);
        bool Remove(string key);
        bool Exists(string key);
    }

    public interface IAuthenticationService
    {
        string HashPassword(string password);
        bool VerifyPassword(string inputPassword, string storedPassword);
        bool NeedsRehash(string storedPassword);
        string GenerateTwoFactorSecret();
        bool VerifyTotpCode(string base32Secret, string code);
        string GenerateJwtToken(User user);
        string GenerateRefreshToken();
        CookieOptions CreateAuthCookieOptions();
        string? TryRenewTokenIfExpiringSoon(string currentToken, User user, int minutesBeforeExpiry = 60);
        ClaimsPrincipal GetPrincipalFromExpiredToken(string token);
        bool ValidateJwtToken(string token);
        string GetRoleFromToken(string token);
        Task<bool> IsAccountLocked(string username);
        void RecordFailedLoginAttempt(string username);
        void ResetFailedLoginAttempts(string username);
        string GeneratePasswordResetToken();
        bool ValidatePasswordResetToken(User user, string token);
        string GenerateResetToken();
    }

    public interface IUserService
    {
        Task<User> GetUserAsync(ClaimsPrincipal User);
        void InvalidateUserCache(string username);
        Task<(User user, string message)> CreateUserAsync(RegisterRequest request);
        Task<(User user, string message)> AuthenticateUserAsync(LoginRequest request);
        Task<(User user, string message)> RefreshTokenAsync(RefreshTokenRequest request);
        Task<(bool success, string message)> ChangePasswordAsync(string username, string currentPassword, string newPassword);
        Task<(bool success, string message)> ConfirmPasswordResetAsync(ChangePasswordRequest request);
        Task<(bool success, string message)> UpdateUserProfileAsync(string username, User updatedUser);
        Task<(bool success, string message)> DeleteUserAsync(string username);
        Task<User> GetUserByUsername(string username);
        Task<User> GetUserByEmail(string email);
        Task<User> GetUserByUuid(Guid uuid);
        Task<(bool success, string message)> LogoutAsync(string username);
        Task<(bool success, string message, string secret)> EnableTwoFactorAsync(string username);
        Task<(bool success, string message)> DisableTwoFactorAsync(string username);
        Task<(bool success, string message)> VerifyTwoFactorAsync(string username, string code);
        Task<List<User>> GetUsersByRole(UserRole role);
        Task<(bool success, string message)> ChangeUserRoleAsync(string username, UserRole newRole);
        Task<(bool success, string message)> LockUserAsync(string username, int lockDurationMinutes = 30);
        Task<(bool success, string message)> UnlockUserAsync(string username);
    }

    public interface IValidationService
    {
        (bool isValid, List<string> errors) ValidateModel<T>(T model) where T : class;
        bool IsValidJson(string jsonString);
        bool IsValidPath(string path);
        bool IsValidFileName(string fileName);
        bool IsVideoFile(string filePath);
        bool IsSafeUrl(string url);
        bool IsSafeEmail(string email);
        string SanitizeInput(string input);
    }

    public interface IVideoStreamingService
    {
        string[] GetVideoFiles();
        Task<(bool success, string message)> DownloadVideoFromUrl(string videoUrl);
        bool IsVideoFile(string filePath);
        string GetVideoFilePath(string fileName);
        string GetContentType(string filePath);
    }
}
