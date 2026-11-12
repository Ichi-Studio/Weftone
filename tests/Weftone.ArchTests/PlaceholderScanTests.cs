using NUnit.Framework;

namespace Weftone.ArchTests;

/// <summary>
/// AGENTS.md §2：仓库内不得残留旧项目前缀占位符，CI 强制检查。
/// 只扫描代码文件；文档（md）中描述规则本身不算占位符。
/// 检索 token 运行时拼接，避免本文件自匹配。
/// </summary>
public class PlaceholderScanTests
{
    private static readonly string[] CodeExtensions =
        [".cs", ".csproj", ".props", ".targets", ".c", ".h", ".cpp", ".cmake", ".yml", ".yaml"];

    private static readonly string[] IgnoredDirs =
        [".git", "bin", "obj", "build", "artifacts", "out", "vendor", ".vs", ".idea", ".vscode"];

    private static readonly string PrefixToken = "Da" + "w.";
    private static readonly string ProjectToken = "Open" + "DAW";

    [Test]
    public void No_LegacyProjectPlaceholders_in_source()
    {
        string root = FindRepoRoot();
        var violations = new List<string>();
        Scan(new DirectoryInfo(root), violations);
        Assert.That(violations, Is.Empty,
            "发现旧命名占位符:\n" + string.Join("\n", violations));
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
        {
            dir = dir.Parent!;
        }
        return dir?.FullName ?? throw new InvalidOperationException("未找到仓库根（.git）");
    }

    private static void Scan(DirectoryInfo dir, List<string> violations)
    {
        foreach (DirectoryInfo sub in dir.EnumerateDirectories())
        {
            if (IgnoredDirs.Contains(sub.Name))
            {
                continue;
            }
            Scan(sub, violations);
        }
        foreach (FileInfo file in dir.EnumerateFiles())
        {
            if (!CodeExtensions.Contains(file.Extension))
            {
                continue;
            }
            int line = 0;
            foreach (string text in File.ReadLines(file.FullName))
            {
                line++;
                if (text.Contains(PrefixToken, StringComparison.Ordinal)
                    || text.Contains(ProjectToken, StringComparison.Ordinal))
                {
                    violations.Add($"{file.FullName}:{line}");
                }
            }
        }
    }
}
