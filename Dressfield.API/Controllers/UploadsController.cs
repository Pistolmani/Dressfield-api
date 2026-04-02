using Dressfield.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Dressfield.API.Controllers;

[ApiController]
[Route("api/upload")]
public class UploadsController : ControllerBase
{
    private static readonly string[] AllowedContentTypes = ["image/jpeg", "image/png", "image/webp"];
    private static readonly string[] AllowedExtensions = [".jpg", ".jpeg", ".png", ".webp"];
    private const long MaxFileSizeBytes = 10 * 1024 * 1024; // 10 MB

    private readonly IStorageService _storage;
    private readonly IFileSecurityScanner _fileSecurityScanner;

    public UploadsController(IStorageService storage, IFileSecurityScanner fileSecurityScanner)
    {
        _storage = storage;
        _fileSecurityScanner = fileSecurityScanner;
    }

    /// <summary>
    /// Upload a design image (JPG/PNG/WebP, max 10 MB).
    /// Returns the public URL for use in custom order submissions.
    /// Open to all — no authentication required (guests need to upload too).
    /// </summary>
    [HttpPost("design")]
    [EnableRateLimiting("upload")]
    [RequestSizeLimit(MaxFileSizeBytes + 1024)] // slight buffer for multipart headers
    public async Task<ActionResult<UploadDesignResponse>> UploadDesign(IFormFile file)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { message = "ფაილი არ არის მოწოდებული." });

        if (file.Length > MaxFileSizeBytes)
            return BadRequest(new { message = "ფაილის ზომა არ უნდა აღემატებოდეს 10 MB-ს." });

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(ext))
            return BadRequest(new { message = "მხოლოდ JPG, PNG ან WebP ფორმატი დასაშვებია." });

        if (!AllowedContentTypes.Contains(file.ContentType.ToLowerInvariant()))
            return BadRequest(new { message = "ფაილის ტიპი დასაშვები არ არის." });

        await using var stream = file.OpenReadStream();
        var header = new byte[12];
        var bytesRead = await stream.ReadAsync(header.AsMemory(0, header.Length));
        if (!HasValidSignature(ext, header.AsSpan(0, bytesRead)))
            return BadRequest(new { message = "Unsupported or malformed image file." });

        stream.Position = 0;
        var scanResult = await _fileSecurityScanner.ScanAsync(
            stream,
            file.FileName,
            file.ContentType,
            HttpContext.RequestAborted);
        if (!scanResult.IsClean)
            return BadRequest(new { message = "File failed security scan.", threat = scanResult.ThreatName });

        stream.Position = 0;
        var url = await _storage.UploadAsync(stream, file.FileName, file.ContentType);

        return Ok(new UploadDesignResponse(url));
    }

    private static bool HasValidSignature(string extension, ReadOnlySpan<byte> header)
    {
        return extension switch
        {
            ".jpg" or ".jpeg" => IsJpeg(header),
            ".png" => IsPng(header),
            ".webp" => IsWebp(header),
            _ => false
        };
    }

    private static bool IsJpeg(ReadOnlySpan<byte> header) =>
        header.Length >= 3
        && header[0] == 0xFF
        && header[1] == 0xD8
        && header[2] == 0xFF;

    private static bool IsPng(ReadOnlySpan<byte> header) =>
        header.Length >= 8
        && header[0] == 0x89
        && header[1] == 0x50
        && header[2] == 0x4E
        && header[3] == 0x47
        && header[4] == 0x0D
        && header[5] == 0x0A
        && header[6] == 0x1A
        && header[7] == 0x0A;

    private static bool IsWebp(ReadOnlySpan<byte> header) =>
        header.Length >= 12
        && header[0] == 0x52
        && header[1] == 0x49
        && header[2] == 0x46
        && header[3] == 0x46
        && header[8] == 0x57
        && header[9] == 0x45
        && header[10] == 0x42
        && header[11] == 0x50;
}

public record UploadDesignResponse(string Url);
