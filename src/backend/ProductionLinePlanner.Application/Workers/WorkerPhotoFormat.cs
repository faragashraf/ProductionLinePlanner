using System.Buffers.Binary;

namespace ProductionLinePlanner.Application.Workers;

public static class WorkerPhotoFormat
{
    public const int MaximumBytes = 5 * 1024 * 1024;
    public const string OctetStreamContentType = "application/octet-stream";

    public static IReadOnlyCollection<string> AllowedContentTypes { get; } =
        ["image/jpeg", "image/png", "image/bmp"];

    public static bool TryDetect(ReadOnlySpan<byte> bytes, out WorkerPhotoFormatInfo format)
    {
        format = default!;
        if (bytes.IsEmpty || bytes.Length > MaximumBytes)
        {
            return false;
        }

        if (IsPng(bytes))
        {
            format = new WorkerPhotoFormatInfo("image/png", ".png");
            return true;
        }

        if (IsJpeg(bytes))
        {
            format = new WorkerPhotoFormatInfo("image/jpeg", ".jpg");
            return true;
        }

        if (IsBitmap(bytes))
        {
            format = new WorkerPhotoFormatInfo("image/bmp", ".bmp");
            return true;
        }

        return false;
    }

    public static bool IsDeclaredContentTypeCompatible(string? declaredContentType, string detectedContentType)
    {
        if (string.IsNullOrWhiteSpace(declaredContentType))
        {
            return true;
        }

        var normalized = declaredContentType.Split(';', 2)[0].Trim();
        return normalized.Equals(OctetStreamContentType, StringComparison.OrdinalIgnoreCase)
            || normalized.Equals(detectedContentType, StringComparison.OrdinalIgnoreCase)
            || (normalized.Equals("image/jpg", StringComparison.OrdinalIgnoreCase)
                && detectedContentType.Equals("image/jpeg", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsPng(ReadOnlySpan<byte> bytes)
    {
        ReadOnlySpan<byte> signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        if (bytes.Length < 57 || !bytes[..8].SequenceEqual(signature))
        {
            return false;
        }

        var offset = 8;
        var seenHeader = false;
        var seenImageData = false;
        while (offset <= bytes.Length - 12)
        {
            var chunkLength = BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(offset, 4));
            if (chunkLength > int.MaxValue || chunkLength > bytes.Length - offset - 12)
            {
                return false;
            }

            var length = (int)chunkLength;
            var type = bytes.Slice(offset + 4, 4);
            var crcContent = bytes.Slice(offset + 4, 4 + length);
            var expectedCrc = BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(offset + 8 + length, 4));
            if (ComputePngCrc(crcContent) != expectedCrc)
            {
                return false;
            }

            if (type.SequenceEqual("IHDR"u8))
            {
                if (seenHeader || offset != 8 || length != 13)
                {
                    return false;
                }

                var data = bytes.Slice(offset + 8, length);
                if (BinaryPrimitives.ReadUInt32BigEndian(data[..4]) == 0
                    || BinaryPrimitives.ReadUInt32BigEndian(data.Slice(4, 4)) == 0
                    || data[10] != 0
                    || data[11] != 0
                    || data[12] > 1)
                {
                    return false;
                }
                seenHeader = true;
            }
            else if (type.SequenceEqual("IDAT"u8))
            {
                if (!seenHeader) return false;
                seenImageData = true;
            }
            else if (type.SequenceEqual("IEND"u8))
            {
                return seenHeader
                    && seenImageData
                    && length == 0
                    && offset + 12 == bytes.Length;
            }

            offset += 12 + length;
        }

        return false;
    }

    private static bool IsJpeg(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 14 || bytes[0] != 0xFF || bytes[1] != 0xD8)
        {
            return false;
        }

        var offset = 2;
        var seenFrame = false;
        var inScan = false;
        while (offset < bytes.Length)
        {
            if (inScan && bytes[offset] != 0xFF)
            {
                offset++;
                continue;
            }

            if (bytes[offset] != 0xFF) return false;
            while (offset < bytes.Length && bytes[offset] == 0xFF) offset++;
            if (offset >= bytes.Length) return false;

            var marker = bytes[offset++];
            if (marker == 0x00)
            {
                if (!inScan) return false;
                continue;
            }

            if (marker == 0xD9)
            {
                return seenFrame && inScan && offset == bytes.Length;
            }

            if (marker == 0xD8) return false;
            if (marker == 0x01 || marker is >= 0xD0 and <= 0xD7)
            {
                if (!inScan && marker != 0x01) return false;
                continue;
            }

            if (offset + 2 > bytes.Length) return false;
            var segmentLength = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(offset, 2));
            if (segmentLength < 2 || offset + segmentLength > bytes.Length) return false;

            if (IsStartOfFrameMarker(marker))
            {
                if (segmentLength < 8
                    || BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(offset + 3, 2)) == 0
                    || BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(offset + 5, 2)) == 0
                    || bytes[offset + 7] == 0)
                {
                    return false;
                }
                seenFrame = true;
            }
            else if (marker == 0xDA)
            {
                if (!seenFrame) return false;
                inScan = true;
            }

            offset += segmentLength;
        }

        return false;
    }

    private static bool IsBitmap(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 54 || bytes[0] != 0x42 || bytes[1] != 0x4D)
        {
            return false;
        }

        var declaredFileSize = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(2, 4));
        var pixelOffset = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(10, 4));
        var dibHeaderSize = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(14, 4));
        return declaredFileSize == bytes.Length
            && dibHeaderSize >= 40
            && pixelOffset >= 14 + dibHeaderSize
            && pixelOffset < bytes.Length
            && BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(18, 4)) != 0
            && BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(22, 4)) != 0;
    }

    private static bool IsStartOfFrameMarker(byte marker) => marker is
        0xC0 or 0xC1 or 0xC2 or 0xC3 or 0xC5 or 0xC6 or 0xC7 or
        0xC9 or 0xCA or 0xCB or 0xCD or 0xCE or 0xCF;

    private static uint ComputePngCrc(ReadOnlySpan<byte> content)
    {
        var crc = uint.MaxValue;
        foreach (var value in content)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) == 1 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }
        return ~crc;
    }
}

public sealed record WorkerPhotoFormatInfo(string ContentType, string Extension);
