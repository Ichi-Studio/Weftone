namespace Weftone.App.Services;

/// <summary>文件对话框抽象：ViewModel 不直接依赖 Avalonia，便于 headless 单测。</summary>
public interface IFileDialogService
{
    Task<string?> OpenProjectAsync();

    Task<string?> SaveProjectAsync();

    Task<string?> OpenWavAsync();

    Task<string?> SaveWavAsync();
}
