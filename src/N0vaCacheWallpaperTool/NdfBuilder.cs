using System.Buffers.Binary;

namespace N0vaCacheWallpaperTool;

internal static class NdfBuilder
{
    public const int NdfHeaderLength = 2;
    private const int MinimumFreeBoxLength = 8;

    public static long GetMaximumSourceLength(long targetLength)
    {
        return targetLength - NdfHeaderLength - MinimumFreeBoxLength;
    }

    public static void ValidateSource(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("找不到 MP4 文件。", path);
        }

        FileInfo info = new(path);
        if (info.Length <= 0)
        {
            throw new InvalidDataException("MP4 文件为空。");
        }
        if (!HasFtypBox(path))
        {
            throw new InvalidDataException("文件中没有找到 MP4 的 ftyp 盒子，无法识别为 MP4。");
        }
    }

    public static void ValidateSource(string path, long targetLength)
    {
        ValidateSource(path);
        if (targetLength <= NdfHeaderLength + MinimumFreeBoxLength)
        {
            throw new InvalidDataException("当前 N0va 缓存槽太小，不是有效的视频缓存槽。");
        }

        FileInfo info = new(path);
        long maximumSourceLength = GetMaximumSourceLength(targetLength);
        if (info.Length > maximumSourceLength)
        {
            throw new InvalidDataException(
                "MP4 太大，无法放入当前 N0va 缓存槽。\n" +
                $"当前槽最多容纳约 {maximumSourceLength / 1024d / 1024d:F1} MiB，视频约 " +
                $"{info.Length / 1024d / 1024d:F1} MiB。\n" +
                "请在人工桌面中选择一个时长更长、缓存更大的官方动态壁纸后重试。");
        }
    }

    public static void Build(string sourcePath, string destinationPath, long targetLength)
    {
        ValidateSource(sourcePath, targetLength);

        long sourceLength = new FileInfo(sourcePath).Length;
        long freeBoxLength = targetLength - NdfHeaderLength - sourceLength;
        if (freeBoxLength < MinimumFreeBoxLength)
        {
            throw new InvalidDataException("源视频过大，无法生成对齐的 NDF 文件。");
        }
        if (freeBoxLength > uint.MaxValue)
        {
            throw new InvalidDataException("N0va 缓存槽过大，超过当前 NDF 格式可安全支持的范围。");
        }

        string? directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using FileStream output = new(
            destinationPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            1024 * 1024,
            FileOptions.SequentialScan);
        output.Write(new byte[NdfHeaderLength]);

        using (FileStream source = new(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1024 * 1024,
            FileOptions.SequentialScan))
        {
            source.CopyTo(output, 1024 * 1024);
        }

        Span<byte> freeHeader = stackalloc byte[8];
        BinaryPrimitives.WriteUInt32BigEndian(freeHeader, checked((uint)freeBoxLength));
        "free"u8.CopyTo(freeHeader[4..]);
        output.Write(freeHeader);

        byte[] zeros = new byte[1024 * 1024];
        long remaining = freeBoxLength - freeHeader.Length;
        while (remaining > 0)
        {
            int count = (int)Math.Min(remaining, zeros.Length);
            output.Write(zeros, 0, count);
            remaining -= count;
        }

        if (output.Length != targetLength)
        {
            throw new InvalidDataException("生成的 NDF 文件长度不正确。");
        }
    }

    public static bool IsVideoNdf(string path)
    {
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length <= NdfHeaderLength + MinimumFreeBoxLength)
            {
                return false;
            }

            using FileStream stream = new(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                64 * 1024,
                FileOptions.SequentialScan);
            Span<byte> header = stackalloc byte[NdfHeaderLength];
            stream.ReadExactly(header);
            if (header[0] != 0 || header[1] != 0)
            {
                return false;
            }
            return HasFtypBox(stream);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool HasFtypBox(string path)
    {
        using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.SequentialScan);
        return HasFtypBox(stream);
    }

    private static bool HasFtypBox(FileStream stream)
    {
        const int scanLimit = 1024 * 1024;
        long remainingLength = Math.Max(0, stream.Length - stream.Position);
        byte[] buffer = new byte[
            (int)Math.Min(scanLimit, Math.Min(remainingLength, int.MaxValue))];
        int read = 0;
        while (read < buffer.Length)
        {
            int current = stream.Read(buffer, read, buffer.Length - read);
            if (current == 0) break;
            read += current;
        }

        ReadOnlySpan<byte> data = buffer.AsSpan(0, read);
        for (int index = 0; index <= data.Length - 8; index++)
        {
            uint size = BinaryPrimitives.ReadUInt32BigEndian(data[index..]);
            if (size >= 8 && data.Slice(index + 4, 4).SequenceEqual("ftyp"u8))
            {
                return true;
            }
        }
        return false;
    }
}
