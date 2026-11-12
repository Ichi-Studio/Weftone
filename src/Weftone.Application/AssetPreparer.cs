using Weftone.Abstractions;
using Weftone.Domain;

namespace Weftone.Application;

public sealed record PreparedAsset(string AssetId, float[] Interleaved, int Channels, long Frames);

/// <summary>资产准备：解码 → 离线重采样到工程采样率（ADR-0003 编译前置，全离线）。</summary>
public sealed class AssetPreparer
{
    public List<PreparedAsset> Prepare(ProjectDocument document, Func<AssetRef, Stream> openAsset,
        ISampleDecoder decoder, IResampler resampler)
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
            prepared.Add(new PreparedAsset(asset.Id, resampled.Interleaved, resampled.Channels, resampled.Frames));
        }
        return prepared;
    }
}
