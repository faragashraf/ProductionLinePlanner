using System.Buffers.Binary;

namespace ProductionLinePlanner.Tests;

public static class WorkerPhotoTestData
{
    public static byte[] CreateJpeg(byte marker = 0x01) =>
    [
        0xFF, 0xD8,
        0xFF, 0xC0, 0x00, 0x0B, 0x08, 0x00, 0x01, 0x00, 0x01, 0x01, 0x01, 0x11, marker,
        0xFF, 0xDA, 0x00, 0x08, 0x01, 0x01, 0x00, 0x00, 0x3F, 0x00,
        0xFF, 0xD9
    ];

    public static byte[] CreateBitmap(byte marker = 0x01)
    {
        var bytes = new byte[58];
        bytes[0] = 0x42;
        bytes[1] = 0x4D;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(2, 4), (uint)bytes.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(10, 4), 54);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(14, 4), 40);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(18, 4), 1);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(22, 4), 1);
        bytes[54] = marker;
        return bytes;
    }

    public static byte[] CreatePng() => Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");
}
