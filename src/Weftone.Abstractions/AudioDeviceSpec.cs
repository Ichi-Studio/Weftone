namespace Weftone.Abstractions;

public sealed record AudioDeviceSpec(int SampleRate, int Channels, int BufferFrames)
{
    public static AudioDeviceSpec Default { get; } = new(48_000, 2, 256);
}
