using Weftone.Abstractions;
using Weftone.Abstractions.Dsp;
using Weftone.Domain;

namespace Weftone.Application;

public sealed record PreparedAsset(string AssetId, float[] Interleaved, int Channels, long Frames)
{
    /// <summary>变体速度（SPEC M4）：1.0 = 原版；&gt;1 = 变体时长 ×1/speed（拉伸产物）。</summary>
    public double Speed { get; init; } = 1.0;
}

/// <summary>
/// 资产准备（ADR-0003 编译前置，全离线）：解码 → 重采样到工程采样率；
/// M4 D3=a：引用通道带非 1 档 StretchSpeed 时按档位产出拉伸变体（确定性、离线）。
/// </summary>
public sealed class AssetPreparer
{
    /// <summary>固定拉伸档位（SPEC M4 D6=a）；变体时长 = 原长 / speed。</summary>
    public static readonly double[] StretchSpeedSteps = [0.5, 0.75, 1.0, 1.25, 1.5, 2.0];

    /// <summary>任意速度取最近档（平局取较大，确定性）。</summary>
    public static double NearestStretchStep(double speed)
    {
        double clamped = Math.Clamp(speed, StretchSpeedSteps[0], StretchSpeedSteps[^1]);
        double best = StretchSpeedSteps[0];
        double bestDistance = double.MaxValue;
        foreach (double step in StretchSpeedSteps)
        {
            double distance = Math.Abs(step - clamped);
            if (distance <= bestDistance)
            {
                bestDistance = distance;
                best = step;
            }
        }
        return best;
    }

    public List<PreparedAsset> Prepare(ProjectDocument document, Func<AssetRef, Stream> openAsset,
        ISampleDecoder decoder, IResampler resampler, ITimeStretcher? stretcher = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(openAsset);
        ArgumentNullException.ThrowIfNull(decoder);
        ArgumentNullException.ThrowIfNull(resampler);

        var prepared = new List<PreparedAsset>(document.Assets.Count);
        foreach (AssetRef asset in document.Assets)
        {
            using Stream stream = openAsset(asset);
            DecodedAudio decoded;
            try
            {
                decoded = decoder.Decode(stream);
            }
            catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException)
            {
                throw new InvalidDataException($"资产解码失败: {asset.Path}: {ex.Message}", ex);
            }
            DecodedAudio resampled = resampler.Resample(decoded, document.SampleRate);
            if (resampled.Channels is < 1 or > 2)
            {
                throw new InvalidDataException($"资产声道数不支持（仅 1/2）: {asset.Path} ({resampled.Channels})");
            }
            var original = new PreparedAsset(asset.Id, resampled.Interleaved, resampled.Channels, resampled.Frames);
            prepared.Add(original);

            if (stretcher is null)
            {
                continue;
            }
            // 引用通道的非 1 档速度集合 → 档位变体（去重升序，确定性）
            var speeds = new SortedSet<double>();
            foreach (Channel channel in document.Channels)
            {
                if (channel.Kind != ChannelKind.Sampler || channel.AssetId != asset.Id)
                {
                    continue;
                }
                double step = NearestStretchStep(channel.StretchSpeed);
                if (Math.Abs(step - 1.0) > 1e-9)
                {
                    speeds.Add(step);
                }
            }
            foreach (double speed in speeds)
            {
                float[] stretched = stretcher.Stretch(
                    resampled.Interleaved, resampled.Channels, document.SampleRate, 1.0 / speed);
                prepared.Add(original with
                {
                    Interleaved = stretched,
                    Frames = stretched.Length / resampled.Channels,
                    Speed = speed,
                });
            }
        }
        return prepared;
    }
}
