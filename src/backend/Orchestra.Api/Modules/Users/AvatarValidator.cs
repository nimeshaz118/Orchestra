using Orchestra.Api.Common;
using SkiaSharp;

namespace Orchestra.Api.Modules.Users;

public static class AvatarValidator
{
    public const int MaxBytes = 2 * 1024 * 1024;
    public const int MaxDimension = 2048;
    public const int MaxRequestBytes = MaxBytes + 64 * 1024; // Allow multipart headers.

    public static async Task<byte[]> ValidateAsync(IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0) throw InvalidFile();
        if (file.Length > MaxBytes) throw TooLarge();

        var format = Path.GetExtension(file.FileName).ToLowerInvariant() switch
        {
            ".png" => SKEncodedImageFormat.Png,
            ".jpg" or ".jpeg" => SKEncodedImageFormat.Jpeg,
            _ => throw new ApiException(415, "UNSUPPORTED_AVATAR_FORMAT", "Use a PNG or JPEG image.")
        };
        var mime = format == SKEncodedImageFormat.Png ? "image/png" : "image/jpeg";
        if (!string.Equals(file.ContentType, mime, StringComparison.OrdinalIgnoreCase))
            throw new ApiException(415, "UNSUPPORTED_AVATAR_FORMAT", "The MIME type must match the file extension.");

        // Bound the actual stream too; do not trust the declared length alone.
        await using var input = file.OpenReadStream();
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await input.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > MaxBytes) throw TooLarge();
            buffer.Write(chunk, 0, read);
        }
        var bytes = buffer.ToArray();
        var signatureMatches = format == SKEncodedImageFormat.Png
            ? bytes.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })
            : bytes.AsSpan().StartsWith(new byte[] { 255, 216, 255 });
        if (!signatureMatches) throw InvalidFile();

        using var data = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(data);
        if (codec is null || codec.EncodedFormat != format) throw InvalidFile();
        var info = codec.Info;
        if (info.Width is < 1 or > MaxDimension || info.Height is < 1 or > MaxDimension)
            throw new ApiException(400, "INVALID_AVATAR_DIMENSIONS", "Image dimensions must be between 1 and 2048 pixels on each side.");

        // Decode only after checking dimensions, then save clean pixels without uploaded metadata.
        using var bitmap = new SKBitmap(new SKImageInfo(info.Width, info.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        if (codec.GetPixels(bitmap.Info, bitmap.GetPixels()) != SKCodecResult.Success) throw InvalidFile();
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        return encoded.ToArray();
    }

    public static ApiException TooLarge() => new(413, "AVATAR_TOO_LARGE", "The avatar must be at most 2 MiB.");
    private static ApiException InvalidFile() => new(400, "INVALID_AVATAR", "Upload a valid PNG or JPEG image.");
}
