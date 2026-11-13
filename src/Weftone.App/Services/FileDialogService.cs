using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace Weftone.App.Services;

public sealed class FileDialogService(Func<TopLevel?> topLevel) : IFileDialogService
{
    private static readonly FilePickerFileType ProjectType = new("Weftone 工程")
    {
        Patterns = ["*.weft"],
    };

    public async Task<string?> OpenProjectAsync()
    {
        TopLevel? top = topLevel();
        if (top is null)
        {
            return null;
        }
        IReadOnlyList<IStorageFile> files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "打开工程",
            AllowMultiple = false,
            FileTypeFilter = [ProjectType],
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public async Task<string?> SaveProjectAsync()
    {
        TopLevel? top = topLevel();
        if (top is null)
        {
            return null;
        }
        IStorageFile? file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "保存工程",
            DefaultExtension = "weft",
            FileTypeChoices = [ProjectType],
        });
        return file?.TryGetLocalPath();
    }
}