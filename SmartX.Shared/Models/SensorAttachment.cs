namespace SmartX.Shared.Models;

public class SensorAttachment
{
    // Unique identifier for the attachment
    public string FileName { get; set; } = string.Empty;
    // MIME type of the attachment (e.g., "image/png", "application/pdf")
    public string ContentType { get; set; } = string.Empty;
    // Size of the attachment file in bytes
    public long FileSizeBytes { get; set; }
    // Binary data of the attachment file
    public byte[] FileData { get; set; } = Array.Empty<byte>();
    // Timestamp indicating when the attachment was uploaded
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
}