using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Orchestra.Api.Common;
using Orchestra.Api.Data;
using Orchestra.Api.Modules.Users;
using SkiaSharp;
using Xunit;

namespace Orchestra.Api.Tests;

public sealed class AvatarEndpointsTests
{
    private const string Route = "/api/v1/me/avatar";

    [Fact]
    public async Task Upload_replace_read_and_delete_are_scoped_to_the_authenticated_owner()
    {
        await using var host = new ApiHost();
        await host.SeedUserAsync("owner@example.test", "USER");
        await host.SeedUserAsync("other@example.test", "USER");
        using var owner = await host.LoginClientAsync("owner@example.test");
        using var other = await host.LoginClientAsync("other@example.test");
        using var png = Upload(ImageBytes(12, 20));
        Assert.Equal(HttpStatusCode.OK, (await owner.PutAsync(Route, png)).StatusCode);
        var saved = await owner.GetAsync(Route);
        Assert.Equal("image/png", saved.Content.Headers.ContentType!.MediaType);
        Assert.True(saved.Headers.CacheControl!.NoStore);
        using var decoded = SKBitmap.Decode(await saved.Content.ReadAsByteArrayAsync());
        Assert.Equal(12, decoded.Width);
        Assert.Equal(20, decoded.Height);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync(Route)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync(Route)).StatusCode);

        using var jpeg = Upload(ImageBytes(30, 40, SKEncodedImageFormat.Jpeg), "portrait.JPEG", "image/jpeg");
        Assert.Equal(HttpStatusCode.OK, (await owner.PutAsync(Route, jpeg)).StatusCode);
        using var replaced = SKBitmap.Decode(await owner.GetByteArrayAsync(Route));
        Assert.Equal(30, replaced.Width);
        Assert.Equal(40, replaced.Height);
        using (var scope = host.Services.CreateScope())
            Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<OrchestraDbContext>().AppUsers.CountAsync(u => u.AvatarData != null));
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync(Route)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.DeleteAsync(Route)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync(Route)).StatusCode);
    }

    [Theory]
    [InlineData("empty", HttpStatusCode.BadRequest)]
    [InlineData("signature", HttpStatusCode.BadRequest)]
    [InlineData("truncated", HttpStatusCode.BadRequest)]
    [InlineData("mismatch", HttpStatusCode.BadRequest)]
    [InlineData("width", HttpStatusCode.BadRequest)]
    [InlineData("height", HttpStatusCode.BadRequest)]
    [InlineData("extension", HttpStatusCode.UnsupportedMediaType)]
    [InlineData("mime", HttpStatusCode.UnsupportedMediaType)]
    [InlineData("large", HttpStatusCode.RequestEntityTooLarge)]
    [InlineData("large-request", HttpStatusCode.RequestEntityTooLarge)]
    public async Task Invalid_uploads_are_rejected_without_replacing_the_existing_avatar(string kind, HttpStatusCode expected)
    {
        await using var host = new ApiHost();
        await host.SeedUserAsync("owner@example.test", "USER");
        using var client = await host.LoginClientAsync("owner@example.test");
        using var original = Upload(ImageBytes());
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsync(Route, original)).StatusCode);
        var before = await client.GetByteArrayAsync(Route);
        var bytes = kind switch
        {
            "empty" => [],
            "signature" => new byte[] { 1, 2, 3 },
            "truncated" => ImageBytes()[..33],
            "mismatch" => ImageBytes(format: SKEncodedImageFormat.Jpeg),
            "width" => ImageBytes(2049, 1),
            "height" => ImageBytes(1, 2049),
            "large" => new byte[AvatarValidator.MaxBytes + 1],
            "large-request" => new byte[AvatarValidator.MaxRequestBytes + 1],
            _ => ImageBytes()
        };
        using var invalid = Upload(bytes, kind == "extension" ? "avatar.svg" : "avatar.png",
            kind == "mime" ? "image/jpeg" : "image/png");
        var response = await client.PutAsync(Route, invalid);
        Assert.Equal(expected, response.StatusCode);
        Assert.False(string.IsNullOrEmpty((await response.Content.ReadFromJsonAsync<ApiError>())!.RequestId));
        Assert.Equal(before, await client.GetByteArrayAsync(Route));
        using (var scope = host.Services.CreateScope())
            Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<OrchestraDbContext>().AppUsers.CountAsync(u => u.AvatarData != null));
    }

    [Fact]
    public async Task Missing_wrong_multiple_and_malformed_form_files_return_400()
    {
        await using var host = new ApiHost();
        await host.SeedUserAsync("owner@example.test", "USER");
        using var client = await host.LoginClientAsync("owner@example.test");
        using var missing = new MultipartFormDataContent();
        using var wrong = new MultipartFormDataContent { { new ByteArrayContent(ImageBytes()), "wrong", "avatar.png" } };
        using var multiple = Upload(ImageBytes());
        multiple.Add(new ByteArrayContent(ImageBytes()), "file", "second.png");
        using var malformed = new StringContent("invalid form");
        malformed.Headers.ContentType = new MediaTypeHeaderValue("multipart/form-data");
        using var truncated = new StringContent("--broken\r\nContent-Disposition: form-data; name=\"file\"; filename=\"a.png\"\r\n\r\nmissing ending");
        truncated.Headers.ContentType = MediaTypeHeaderValue.Parse("multipart/form-data; boundary=broken");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsync(Route, missing)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsync(Route, wrong)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsync(Route, multiple)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsync(Route, malformed)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsync(Route, truncated)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync(Route, new { file = "photo" })).StatusCode);
    }

    [Fact]
    public async Task Chunked_oversized_requests_return_413()
    {
        await using var host = new ApiHost();
        await host.SeedUserAsync("owner@example.test", "USER");
        using var client = await host.LoginClientAsync("owner@example.test");
        using var content = new ChunkedContent(new byte[AvatarValidator.MaxRequestBytes + 1]);
        content.Headers.ContentType = MediaTypeHeaderValue.Parse("multipart/form-data; boundary=test");
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await client.PutAsync(Route, content)).StatusCode);
    }

    [Theory]
    [InlineData(1, 2048)]
    [InlineData(2048, 1)]
    public async Task Exact_size_and_dimension_limits_are_accepted(int width, int height)
    {
        await using var host = new ApiHost();
        await host.SeedUserAsync("owner@example.test", "USER");
        using var client = await host.LoginClientAsync("owner@example.test");
        var bytes = new byte[AvatarValidator.MaxBytes];
        ImageBytes(width, height).CopyTo(bytes, 0);
        using var content = Upload(bytes);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsync(Route, content)).StatusCode);
        Assert.True((await client.GetByteArrayAsync(Route)).Length < bytes.Length);
    }

    [Fact]
    public async Task Avatar_endpoints_require_authentication()
    {
        await using var host = new ApiHost();
        using var client = host.CreateClient();
        using var image = Upload(ImageBytes());
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PutAsync(Route, image)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.DeleteAsync(Route)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Route)).StatusCode);
    }

    [Fact]
    public async Task Storage_and_deletion_failures_return_safe_500()
    {
        await using var host = new ApiHost();
        await host.SeedUserAsync("owner@example.test", "USER");
        using var client = await host.LoginClientAsync("owner@example.test");
        using var original = Upload(ImageBytes());
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsync(Route, original)).StatusCode);
        var before = await client.GetByteArrayAsync(Route);
        host.FailAvatarSave = true;
        using var replacement = Upload(ImageBytes(20, 30));
        var response = await client.PutAsync(Route, replacement);
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("INTERNAL_ERROR", (await response.Content.ReadFromJsonAsync<ApiError>())!.Code);
        Assert.Equal(before, await client.GetByteArrayAsync(Route));
        var deletion = await client.DeleteAsync(Route);
        Assert.Equal(HttpStatusCode.InternalServerError, deletion.StatusCode);
        Assert.Equal(before, await client.GetByteArrayAsync(Route));
    }

    private sealed class ChunkedContent(byte[] bytes) : HttpContent
    {
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(bytes).AsTask();
    }

    private static MultipartFormDataContent Upload(byte[] bytes, string name = "avatar.png", string mime = "image/png")
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(mime);
        return new MultipartFormDataContent { { content, "file", name } };
    }

    private static byte[] ImageBytes(int width = 8, int height = 8, SKEncodedImageFormat format = SKEncodedImageFormat.Png)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(SKColors.CornflowerBlue);
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(format, 90);
        return encoded.ToArray();
    }
}
