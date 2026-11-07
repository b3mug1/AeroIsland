using System.Security.Cryptography;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AeroIsland.Core;
using AeroIsland.Infrastructure;
using Application = System.Windows.Application;
using Clipboard = System.Windows.Clipboard;
using DataFormats = System.Windows.DataFormats;

namespace AeroIsland.Windows;

public sealed class ClipboardCapture
{
    private readonly LocalStorage _storage;
    private readonly ClipboardFeedbackGuard _feedback = new();
    public ClipboardCapture(LocalStorage storage) => _storage = storage;

    public async Task<HistoryItem?> CaptureAsync(AppSettings settings, CancellationToken token)
    {
        if (!settings.ClipboardEnabled || settings.ClipboardPaused) return null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                return await Application.Current.Dispatcher.InvokeAsync(() => Read(settings));
            }
            catch (System.Runtime.InteropServices.COMException) { await Task.Delay(40 * (attempt + 1), token); }
            catch (System.Runtime.InteropServices.ExternalException) { await Task.Delay(40 * (attempt + 1), token); }
        }
        return null;
    }

    private HistoryItem? Read(AppSettings settings)
    {
        var data = Clipboard.GetDataObject();
        if (data is null || data.GetDataPresent("ExcludeClipboardContentFromMonitorProcessing")) return null;
        if (data.GetDataPresent("CanIncludeInClipboardHistory") &&
            data.GetData("CanIncludeInClipboardHistory") is byte[] signal && signal.Length > 0 && signal[0] == 0) return null;

        if (data.GetDataPresent(DataFormats.UnicodeText))
        {
            var text = data.GetData(DataFormats.UnicodeText) as string;
            if (text is null || text.Length > settings.MaxTextLength || HistoryRules.ShouldSkipText(text)) return null;
            var fingerprint = HistoryRules.TextFingerprint(text);
            if (_feedback.ShouldIgnore(fingerprint)) return null;
            var kind = Uri.TryCreate(text.Trim(), UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http" ? HistoryKind.Url : HistoryKind.Text;
            return new HistoryItem(Guid.NewGuid(), kind, text.Length > 140 ? text[..140] + "…" : text, text, null,
                DateTimeOffset.UtcNow, false, fingerprint);
        }

        if (!data.GetDataPresent(DataFormats.Bitmap)) return null;
        var image = Clipboard.GetImage();
        if (image is null) return null;
        var width = image.PixelWidth;
        var height = image.PixelHeight;
        if (width > 8192 || height > 8192) return null;
        var ratio = Math.Min(1.0, (double)settings.MaxImageDimension / Math.Max(width, height));
        BitmapSource scaled = ratio < 1 ? new TransformedBitmap(image, new ScaleTransform(ratio, ratio)) : image;
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(scaled));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        if (stream.Length > settings.MaxImageCacheMiB * 1024L * 1024L) return null;
        var bytes = stream.ToArray();
        var fingerprintImage = Convert.ToHexString(SHA256.HashData(bytes));
        if (_feedback.ShouldIgnore(fingerprintImage)) return null;
        var path = _storage.NewImagePath();
        File.WriteAllBytes(path, bytes);
        return new HistoryItem(Guid.NewGuid(), HistoryKind.Image, $"Image · {scaled.PixelWidth} × {scaled.PixelHeight}", null,
            path, DateTimeOffset.UtcNow, false, fingerprintImage);
    }

    public void Restore(HistoryItem item)
    {
        if (item.Kind == HistoryKind.Image && item.ImageFile is { } file && File.Exists(file))
        {
            using var stream = File.OpenRead(file);
            var image = BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            Clipboard.SetImage(image);
        }
        else if (item.Text is { } text) Clipboard.SetText(text);
        _feedback.MarkRestored(item.Fingerprint);
    }
}
