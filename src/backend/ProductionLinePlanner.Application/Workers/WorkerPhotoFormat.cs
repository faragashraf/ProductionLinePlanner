namespace ProductionLinePlanner.Application.Workers;

public static class WorkerPhotoFormat
{
    public const int MaximumBytes = 5 * 1024 * 1024;

    public static bool TryGetContentType(byte[]? bytes, out string contentType)
    {
        contentType = string.Empty;
        if (bytes is null || bytes.Length == 0 || bytes.Length > MaximumBytes)
        {
            return false;
        }

        if (bytes.Length >= 54 && bytes[0] == 0x42 && bytes[1] == 0x4D)
        {
            contentType = "image/bmp";
            return true;
        }

        if (bytes.Length >= 24 &&
            bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47 &&
            bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A)
        {
            contentType = "image/png";
            return true;
        }

        if (bytes.Length >= 4 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
        {
            contentType = "image/jpeg";
            return true;
        }

        if (bytes.Length >= 13 &&
            bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46 &&
            bytes[3] == 0x38 && (bytes[4] == 0x37 || bytes[4] == 0x39) && bytes[5] == 0x61)
        {
            contentType = "image/gif";
            return true;
        }

        return false;
    }
}
