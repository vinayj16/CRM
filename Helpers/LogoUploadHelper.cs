using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Threading.Tasks;

namespace CRM.Helpers
{
    /// <summary>
    /// Helper for persisting uploaded logo images to the wwwroot/uploads folder
    /// (instead of storing large base64 blobs in the database). The stored value
    /// is a web-accessible relative path such as "/uploads/logos/company-123.png".
    /// </summary>
    public static class LogoUploadHelper
    {
        private static readonly string[] AllowedTypes =
            { "image/png", "image/jpeg", "image/jpg", "image/gif" };

        private const long MaxBytes = 2 * 1024 * 1024; // 2MB

        /// <summary>
        /// Saves the uploaded logo to wwwroot/uploads/logos and returns the relative
        /// URL path to store in the database. Returns null if no file was provided.
        /// Throws an InvalidOperationException with a user-friendly message on validation failure.
        /// </summary>
        public static async Task<string?> SaveLogoAsync(
            IFormFile? file,
            IWebHostEnvironment env,
            string prefix,
            ILogger? logger = null)
        {
            if (file == null || file.Length == 0)
                return null;

            if (file.Length > MaxBytes)
                throw new InvalidOperationException("Logo file must be less than 2MB");

            var contentType = file.ContentType?.ToLowerInvariant() ?? "";
            if (!AllowedTypes.Contains(contentType))
                throw new InvalidOperationException("Only PNG, JPG, and GIF images are allowed");

            var uploadsRoot = Path.Combine(env.WebRootPath, "uploads", "logos");
            Directory.CreateDirectory(uploadsRoot);

            var extension = Path.GetExtension(file.FileName)?.ToLowerInvariant() switch
            {
                ".png" => ".png",
                ".jpg" => ".jpg",
                ".jpeg" => ".jpg",
                ".gif" => ".gif",
                _ => ".png"
            };

            var safePrefix = string.IsNullOrWhiteSpace(prefix) ? "logo" : prefix;
            var fileName = $"{safePrefix}-{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}{extension}";
            var fullPath = Path.Combine(uploadsRoot, fileName);

            using (var stream = new FileStream(fullPath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            logger?.LogInformation("Saved logo to {Path}", fullPath);

            // Relative web path (forward slashes for URLs)
            return "/uploads/logos/" + fileName;
        }
    }
}