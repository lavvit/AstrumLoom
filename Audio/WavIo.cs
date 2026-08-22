namespace AstrumLoom.Audio;

/// <summary>float[]（-1..1）と 16bit PCM WAV の相互変換。モノラル・ステレオ（インターリーブ）両対応。</summary>
public static class WavIo
{
    /// <summary>モノラル PCM を 16bit WAV バイト列にする。</summary>
    public static byte[] EncodeMono(float[] samples, int sampleRate) => Encode(samples, sampleRate, channels: 1);

    /// <summary>
    /// ステレオ PCM（L,R,L,R,... のインターリーブ済み）を 16bit WAV バイト列にする。
    /// samples.Length は偶数でなければならない。
    /// </summary>
    public static byte[] EncodeStereo(float[] interleaved, int sampleRate) => Encode(interleaved, sampleRate, channels: 2);

    private static byte[] Encode(float[] samples, int sampleRate, int channels)
    {
        const int bitsPerSample = 16;
        int blockAlign = channels * (bitsPerSample / 8);
        int byteRate = sampleRate * blockAlign;
        int dataSize = samples.Length * (bitsPerSample / 8);

        using var ms = new MemoryStream(44 + dataSize);
        using var w = new BinaryWriter(ms);

        // RIFF ヘッダ
        w.Write("RIFF"u8);
        w.Write(36 + dataSize);
        w.Write("WAVE"u8);

        // fmt チャンク
        w.Write("fmt "u8);
        w.Write(16);                 // fmt チャンクサイズ（PCM は 16 固定）
        w.Write((short)1);           // PCM
        w.Write((short)channels);
        w.Write(sampleRate);
        w.Write(byteRate);
        w.Write((short)blockAlign);
        w.Write((short)bitsPerSample);

        // data チャンク
        w.Write("data"u8);
        w.Write(dataSize);
        foreach (float s in samples)
            w.Write(FloatToInt16(s));

        return ms.ToArray();
    }

    private static short FloatToInt16(float s)
    {
        double clamped = Math.Clamp(s, -1.0, 1.0);
        return (short)Math.Round(clamped * short.MaxValue);
    }

    /// <summary>16bit WAV バイト列を float[] へ戻す。往復誤差の検算（AudioSelfCheck）に使う。チャンネル数は out で返す。</summary>
    public static float[] Decode(byte[] wav, out int sampleRate, out int channels)
    {
        using var ms = new MemoryStream(wav);
        using var r = new BinaryReader(ms);

        if (new string(r.ReadChars(4)) != "RIFF") throw new InvalidDataException("RIFF ヘッダがありません。");
        r.ReadInt32(); // ファイルサイズ
        if (new string(r.ReadChars(4)) != "WAVE") throw new InvalidDataException("WAVE 形式ではありません。");

        sampleRate = 44100;
        channels = 1;
        short bits = 16;
        float[]? data = null;

        while (ms.Position < ms.Length - 8)
        {
            string chunkId = new(r.ReadChars(4));
            int chunkSize = r.ReadInt32();
            long chunkEnd = ms.Position + chunkSize;

            if (chunkId == "fmt ")
            {
                r.ReadInt16(); // format tag
                channels = r.ReadInt16();
                sampleRate = r.ReadInt32();
                r.ReadInt32(); // byte rate
                r.ReadInt16(); // block align
                bits = r.ReadInt16();
            }
            else if (chunkId == "data")
            {
                int count = chunkSize / (bits / 8);
                data = new float[count];
                for (int i = 0; i < count; i++)
                    data[i] = bits == 16 ? r.ReadInt16() / (float)short.MaxValue : r.ReadByte() / 128f - 1f;
            }
            else
            {
                // 未知のチャンクは読み飛ばす。
            }

            ms.Position = Math.Min(chunkEnd, ms.Length);
        }

        return data ?? [];
    }
}
