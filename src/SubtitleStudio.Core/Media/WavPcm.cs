using System.Text;

namespace SubtitleStudio.Core.Media;

/// <summary>从 wav 里取 PCM 数据（跳过 RIFF 头）。只支持 PCM 16bit 单声道这类简单文件。</summary>
public static class WavPcm
{
    /// <summary>返回 data 块里的原始字节；不是 RIFF 时整文件返回。</summary>
    public static byte[] ReadPcm16(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length < 12
            || Encoding.ASCII.GetString(bytes, 0, 4) != "RIFF"
            || Encoding.ASCII.GetString(bytes, 8, 4) != "WAVE")
        {
            return bytes;
        }

        var offset = 12;
        while (offset + 8 <= bytes.Length)
        {
            var id = Encoding.ASCII.GetString(bytes, offset, 4);
            var size = BitConverter.ToInt32(bytes, offset + 4);
            var dataStart = offset + 8;

            if (id == "data")
            {
                if (size <= 0 || dataStart + size > bytes.Length)
                    size = bytes.Length - dataStart;

                var pcm = new byte[size];
                Buffer.BlockCopy(bytes, dataStart, pcm, 0, size);
                return pcm;
            }

            if (size < 0)
                break;

            offset = dataStart + size + (size % 2);
        }

        // 没找到 data 块，就当整个文件是裸 PCM
        return bytes;
    }

    /// <summary>把 PCM16 单声道数据写成标准 RIFF wav（切片上传云端时用）。</summary>
    public static void WritePcm16(string path, byte[] pcm, int sampleRate)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        using var writer = new BinaryWriter(stream);

        const ushort bitsPerSample = 16;
        const ushort channels = 1;
        var byteRate = sampleRate * channels * bitsPerSample / 8;
        var blockAlign = (ushort)(channels * bitsPerSample / 8);

        writer.Write("RIFF"u8);
        writer.Write(36 + pcm.Length);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);                  // PCM 的 fmt 块固定 16 字节
        writer.Write((ushort)1);           // PCM
        writer.Write(channels);
        writer.Write(sampleRate);
        writer.Write(byteRate);
        writer.Write(blockAlign);
        writer.Write(bitsPerSample);
        writer.Write("data"u8);
        writer.Write(pcm.Length);
        writer.Write(pcm);
    }
}
