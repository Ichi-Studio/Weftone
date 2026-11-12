using NUnit.Framework;
using Weftone.Adapters.ProjectStore;
using Weftone.Domain;

namespace Weftone.Domain.Tests;

/// <summary>工程加载模糊测试（AGENTS.md §10/§11）：位翻转变异不得引发未分类异常。</summary>
public class ProjectLoadFuzzTests
{
    private string _workDir = "";

    [SetUp]
    public void SetUp()
    {
        _workDir = Path.Combine(Path.GetTempPath(), "weftone-fuzz-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workDir);
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_workDir, recursive: true);

    private string CreateValidPackage()
    {
        var document = new ProjectDocument();
        document.Channels.Add(new Channel { Id = 1, Kind = ChannelKind.Sine });
        var pattern = new Pattern { Id = 10 };
        pattern.Notes.Add(new Note { ChannelId = 1, Tick = 0, Key = 60, LengthTicks = 96 });
        document.Patterns.Add(pattern);
        document.Playlist.Add(new Clip { PatternId = 10, StartTick = 0 });

        string package = Path.Combine(_workDir, "fuzz.weft");
        new WeftProjectStore().Save(package, document, new Dictionary<string, string>());
        return package;
    }

    [Test]
    public void Mutated_packages_only_raise_expected_exceptions()
    {
        byte[] original = File.ReadAllBytes(CreateValidPackage());
        string mutatedPath = Path.Combine(_workDir, "mutated.weft");
        var rng = new Random(2026_11_12);
        var store = new WeftProjectStore();

        int opened = 0;
        int rejected = 0;
        for (int iteration = 0; iteration < 300; iteration++)
        {
            byte[] copy = (byte[])original.Clone();
            int flips = rng.Next(1, 8);
            for (int flip = 0; flip < flips; flip++)
            {
                copy[rng.Next(copy.Length)] ^= (byte)(1 << rng.Next(8));
            }
            File.WriteAllBytes(mutatedPath, copy);

            try
            {
                store.Load(mutatedPath);
                opened++;
            }
            catch (Exception ex) when (ex is InvalidProjectException or InvalidDataException)
            {
                rejected++;
            }
        }
        TestContext.Out.WriteLine($"fuzz 结果: 打开 {opened}，拒绝 {rejected} / 300");
        Assert.That(opened + rejected, Is.EqualTo(300), "存在未分类异常逃逸（已在 catch 外抛出）");
    }
}
