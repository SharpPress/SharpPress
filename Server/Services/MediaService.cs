using SharpPress.Events;
using SharpPress.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace SharpPress.Services
{
    public class MediaService
    {
        private readonly FeatherDatabase _db;
        private readonly Logger _logger;
        private readonly IEventBus _eventBus;
        private readonly string _mediaFolder;
        private readonly string _thumbnailFolder;

        private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp", ".svg", ".ico"
        };

        private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".mp4", ".webm", ".ogg", ".avi", ".mov", ".mkv"
        };

        private static readonly HashSet<string> DocumentExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".txt", ".csv"
        };

        private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".mp3", ".wav", ".ogg", ".flac", ".aac", ".m4a"
        };

        public MediaService(FeatherDatabase db, Logger logger, IEventBus eventBus)
        {
            _db = db;
            _logger = logger;
            _eventBus = eventBus;
            _mediaFolder = Path.Combine(AppContext.BaseDirectory, "media");
            _thumbnailFolder = Path.Combine(_mediaFolder, "thumbnails");

            Directory.CreateDirectory(_mediaFolder);
            Directory.CreateDirectory(_thumbnailFolder);
        }

        public async Task<MediaItem> UploadAsync(Stream fileStream, string fileName, string contentType, string uploadedBy)
        {
            var safeName = GetSafeFileName(fileName);
            var ext = Path.GetExtension(safeName);
            var uniqueName = $"{Guid.NewGuid():N}{ext}";
            var filePath = Path.Combine(_mediaFolder, uniqueName);

            using (var fs = new FileStream(filePath, FileMode.Create))
            {
                await fileStream.CopyToAsync(fs);
            }

            var fileInfo = new FileInfo(filePath);
            var mediaType = GetMediaType(ext);
            string thumbnailPath = "";
            int width = 0;
            int height = 0;

            if (mediaType == "image")
            {
                try
                {
                    thumbnailPath = GenerateThumbnail(filePath, uniqueName);
                    var dims = GetImageDimensions(filePath);
                    width = dims.Width;
                    height = dims.Height;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Thumbnail generation failed for {fileName}: {ex.Message}");
                }
            }

            var mediaItem = new MediaItem
            {
                FileName = safeName,
                ContentType = contentType,
                FileSize = fileInfo.Length,
                FilePath = uniqueName,
                ThumbnailPath = thumbnailPath,
                UploadedBy = uploadedBy,
                MediaType = mediaType,
                Width = width,
                Height = height,
                CreatedAt = DateTime.UtcNow
            };

            await _db.SaveData(mediaItem);
            _logger.Log($"Media uploaded: {safeName} ({mediaType})");

            await _eventBus.PublishAsync(new MediaUploadedEvent(mediaItem));
            return mediaItem;
        }

        public async Task<List<MediaItem>> GetAllAsync()
        {
            return await _db.GetAll<MediaItem>();
        }

        public async Task<List<MediaItem>> GetByMediaTypeAsync(string mediaType)
        {
            return await _db.GetListByLinq<MediaItem>(m => m.MediaType == mediaType);
        }

        public async Task<MediaItem?> GetByIdAsync(int id)
        {
            return await _db.GetData<MediaItem>(id);
        }

        public async Task UpdateAltTextAsync(int id, string altText)
        {
            var item = await _db.GetData<MediaItem>(id);
            if (item == null) return;
            item.AltText = altText;
            await _db.SaveData(item);
        }

        public async Task DeleteAsync(int id)
        {
            var item = await _db.GetData<MediaItem>(id);
            if (item == null) return;

            var fullPath = Path.Combine(_mediaFolder, item.FilePath);
            if (File.Exists(fullPath))
            {
                try { File.Delete(fullPath); }
                catch { }
            }

            if (!string.IsNullOrEmpty(item.ThumbnailPath))
            {
                var thumbPath = Path.Combine(_thumbnailFolder, Path.GetFileName(item.ThumbnailPath));
                if (File.Exists(thumbPath))
                {
                    try { File.Delete(thumbPath); }
                    catch { }
                }
            }

            await _db.Delete<MediaItem>(id);
            _logger.Log($"Media deleted: {item.FileName}");

            await _eventBus.PublishAsync(new MediaDeletedEvent(item.Id, item.FileName));
        }

        public string GetMediaFilePath(string fileName)
        {
            if (fileName.Contains("..") || fileName.Contains("/") || fileName.Contains("\\"))
                return null;

            var filePath = Path.Combine(_mediaFolder, fileName);
            return File.Exists(filePath) ? filePath : null;
        }

        public string GetThumbnailFilePath(string fileName)
        {
            if (fileName.Contains("..") || fileName.Contains("/") || fileName.Contains("\\"))
                return null;

            var filePath = Path.Combine(_thumbnailFolder, fileName);
            return File.Exists(filePath) ? filePath : null;
        }

        public string GetContentType(string filePath)
        {
            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            return ext switch
            {
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".gif" => "image/gif",
                ".webp" => "image/webp",
                ".svg" => "image/svg+xml",
                ".bmp" => "image/bmp",
                ".ico" => "image/x-icon",
                ".mp4" => "video/mp4",
                ".webm" => "video/webm",
                ".ogg" => "video/ogg",
                ".avi" => "video/x-msvideo",
                ".mov" => "video/quicktime",
                ".mkv" => "video/x-matroska",
                ".mp3" => "audio/mpeg",
                ".wav" => "audio/wav",
                ".flac" => "audio/flac",
                ".aac" => "audio/aac",
                ".pdf" => "application/pdf",
                ".txt" => "text/plain",
                ".csv" => "text/csv",
                ".doc" => "application/msword",
                ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                ".xls" => "application/vnd.ms-excel",
                ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                _ => "application/octet-stream"
            };
        }

        private string GetMediaType(string extension)
        {
            if (ImageExtensions.Contains(extension)) return "image";
            if (VideoExtensions.Contains(extension)) return "video";
            if (AudioExtensions.Contains(extension)) return "audio";
            if (DocumentExtensions.Contains(extension)) return "document";
            return "other";
        }

        private string GetSafeFileName(string fileName)
        {
            var name = Path.GetFileName(fileName);
            var safeName = string.Join("_", name.Split(Path.GetInvalidFileNameChars()));
            return string.IsNullOrEmpty(safeName) ? "unnamed_file" : safeName;
        }

        private string GenerateThumbnail(string sourcePath, string uniqueName)
        {
            var thumbFileName = $"thumb_{uniqueName}";
            var thumbPath = Path.Combine(_thumbnailFolder, thumbFileName);

            using var image = Image.Load(sourcePath);
            var ratio = Math.Min((double)150 / image.Width, (double)150 / image.Height);
            var newWidth = (int)(image.Width * ratio);
            var newHeight = (int)(image.Height * ratio);

            image.Mutate(x => x.Resize(newWidth, newHeight));
            image.SaveAsJpeg(thumbPath);

            return thumbFileName;
        }

        private (int Width, int Height) GetImageDimensions(string filePath)
        {
            try
            {
                using var image = Image.Load(filePath);
                return (image.Width, image.Height);
            }
            catch
            {
                return (0, 0);
            }
        }
    }
}
