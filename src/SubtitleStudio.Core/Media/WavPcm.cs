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
}
