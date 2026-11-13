using Weftone.Abstractions;
using Weftone.App.Services;

namespace Weftone.App.Tests;

internal sealed class FakeBackend : IAudioBackend
{
    public void Open(AudioDeviceSpec spec, AudioRenderCallback callback) { }
    public void Start() { }
    public void Stop() { }
    public void Dispose() { }
}

internal sealed class FakeDialogs : IFileDialogService
{
    public string? WavToOpen { get; set; }

    public string? WavToSave { get; set; }

    public Task<string?> OpenProjectAsync() => Task.FromResult<string?>(null);

    public Task<string?> SaveProjectAsync() => Task.FromResult<string?>(null);

    public Task<string?> OpenWavAsync() => Task.FromResult(WavToOpen);

    public Task<string?> SaveWavAsync() => Task.FromResult(WavToSave);
}
