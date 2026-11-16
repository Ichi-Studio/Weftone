using System.Text;
using NUnit.Framework;
using Weftone.Adapters.ProjectStore;
using Weftone.Domain;
using Weftone.Domain.Commands;

namespace Weftone.Domain.Tests;

/// <summary>SPEC M4 竖切 2：切片点命令往返/替换/撤销 + schema 往返与校验。</summary>
public class SlicePointTests
{
    private static ProjectDocument NewDocument()
    {
        var document = new ProjectDocument { Tempo = 120, SampleRate = 48_000 };
        document.Assets.Add(new AssetRef { Id = "a", Path = "assets/a.wav", Sha256 = new string('0', 64) });
        return document;
    }

    [Test]
    public void Add_keeps_points_sorted_and_replaces_same_frame()
    {
        ProjectDocument document = NewDocument();
        var history = new CommandHistory();
        history.Execute(new AddSlicePointCommand("a", 800), document);
        history.Execute(new AddSlicePointCommand("a", 100), document);
        history.Execute(new AddSlicePointCommand("a", 800), document);

        Assert.That(document.Assets[0].SlicePoints, Is.EqualTo(new long[] { 100, 800 }));

        history.Undo(document);
        Assert.That(document.Assets[0].SlicePoints, Is.EqualTo(new long[] { 100, 800 }), "同帧替换撤销还原");
    }

    [Test]
    public void Remove_and_move_roundtrip()
    {
        ProjectDocument document = NewDocument();
        var history = new CommandHistory();
        history.Execute(new AddSlicePointCommand("a", 100), document);
        history.Execute(new AddSlicePointCommand("a", 200), document);
        history.Execute(new RemoveSlicePointCommand("a", 100), document);
        Assert.That(document.Assets[0].SlicePoints, Is.EqualTo(new long[] { 200 }));

        history.Undo(document);
        Assert.That(document.Assets[0].SlicePoints, Is.EqualTo(new long[] { 100, 200 }));

        history.Execute(new MoveSlicePointCommand("a", 100, 150), document);
        Assert.That(document.Assets[0].SlicePoints, Is.EqualTo(new long[] { 150, 200 }));
        history.Undo(document);
        Assert.That(document.Assets[0].SlicePoints, Is.EqualTo(new long[] { 100, 200 }));
    }

    [Test]
    public void Missing_asset_or_frame_throws()
    {
        ProjectDocument document = NewDocument();
        var history = new CommandHistory();
        Assert.That(() => history.Execute(new AddSlicePointCommand("nope", 10), document),
            Throws.InvalidOperationException);
        Assert.That(() => history.Execute(new RemoveSlicePointCommand("a", 10), document),
            Throws.InvalidOperationException);
    }

    [Test]
    public void Slice_points_roundtrip_through_schema()
    {
        string workDir = Path.Combine(Path.GetTempPath(), "weftone-slice-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDir);
        try
        {
            var store = new WeftProjectStore();
            ProjectDocument document = NewDocument();
            document.Assets[0].SlicePoints.AddRange([0, 800, 1600]);
            string assetPath = Path.Combine(workDir, "a.wav");
            File.WriteAllBytes(assetPath, Encoding.ASCII.GetBytes("fake-wav-payload-0123456789"));
            string package = Path.Combine(workDir, "slice.weft");
            store.Save(package, document, new Dictionary<string, string> { ["assets/a.wav"] = assetPath });

            ProjectDocument loaded = store.Load(package).Document;
            Assert.That(loaded.Assets[0].SlicePoints, Is.EqualTo(new long[] { 0, 800, 1600 }));
        }
        finally
        {
            Directory.Delete(workDir, recursive: true);
        }
    }
}
