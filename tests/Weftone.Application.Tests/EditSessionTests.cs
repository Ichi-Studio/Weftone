using NUnit.Framework;
using Weftone.Adapters.Codecs;
using Weftone.Adapters.ProjectStore;
using Weftone.Application;
using Weftone.Domain;
using Weftone.Domain.Commands;

namespace Weftone.Application.Tests;

public class EditSessionTests
{
    private static EditSession NewSession() =>
        new(new WeftProjectStore(), new DrWavDecoder(), new LinearResampler());

    private static string TempProjectPath() =>
        Path.Combine(Path.GetTempPath(), "weftone-edit-session-" + Guid.NewGuid().ToString("N") + ".weft");

    [Test]
    public void NewDemo_compiles_with_voices_and_asset()
    {
        using var session = NewSession();
        session.NewDemo();
        var plan = session.Compile();
        Assert.Multiple(() =>
        {
            Assert.That(plan.Voices.Length, Is.GreaterThan(0));
            Assert.That(plan.Assets.Length, Is.EqualTo(1));
            Assert.That(session.IsDirty, Is.True);
        });
    }

    [Test]
    public void Save_then_open_roundtrips_and_clears_dirty()
    {
        string path = TempProjectPath();
        try
        {
            using var session = NewSession();
            session.NewDemo();
            int voiceCount = session.Compile().Voices.Length;
            session.Save(path);
            Assert.That(session.IsDirty, Is.False);

            using var loaded = NewSession();
            loaded.Open(path);
            Assert.Multiple(() =>
            {
                Assert.That(loaded.IsDirty, Is.False);
                Assert.That(loaded.Compile().Voices.Length, Is.EqualTo(voiceCount));
                Assert.That(loaded.ProjectPath, Is.EqualTo(path));
            });
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void Execute_undo_redo_tracks_history_and_dirty()
    {
        using var session = NewSession();
        session.NewDemo();
        var note = new Note { ChannelId = 1, Tick = 768, Key = 62, Velocity = 0.9, LengthTicks = 96 };
        session.Execute(new AddNoteCommand(10, note));
        Assert.Multiple(() =>
        {
            Assert.That(session.History.CanUndo, Is.True);
            Assert.That(session.IsDirty, Is.True);
        });
        session.Undo();
        Assert.That(session.History.CanRedo, Is.True);
        session.Redo();
        Assert.That(session.History.CanUndo, Is.True);
    }

    [Test]
    public void Open_clears_undo_history()
    {
        string path = TempProjectPath();
        try
        {
            using var session = NewSession();
            session.NewDemo();
            session.Save(path);
            session.Execute(new AddNoteCommand(10,
                new Note { ChannelId = 1, Tick = 0, Key = 60, Velocity = 1.0, LengthTicks = 48 }));
            Assert.That(session.History.CanUndo, Is.True);
            session.Open(path);
            Assert.Multiple(() =>
            {
                Assert.That(session.History.CanUndo, Is.False);
                Assert.That(session.History.CanRedo, Is.False);
            });
        }
        finally
        {
            File.Delete(path);
        }
    }
}