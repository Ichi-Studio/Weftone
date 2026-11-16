using System.IO.Compression;
using System.Text;
using NUnit.Framework;
using Weftone.Adapters.ProjectStore;
using Weftone.Domain;

namespace Weftone.Domain.Tests;

public class WeftProjectStoreTests
{
    private string _workDir = "";

    [SetUp]
    public void SetUp()
    {
        _workDir = Path.Combine(Path.GetTempPath(), "weftone-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workDir);
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_workDir, recursive: true);

    private static ProjectDocument NewDocument()
    {
        var document = new ProjectDocument { Tempo = 128, SampleRate = 48_000 };
        document.Channels.Add(new Channel
        {
            Id = 1, Name = "Kick", Kind = ChannelKind.Sampler, AssetId = "asset-kick", Volume = 0.9, Pan = -0.1,
        });
        document.Channels.Add(new Channel { Id = 2, Name = "Lead", Kind = ChannelKind.Sine });
        var pattern = new Pattern { Id = 10, Name = "P1" };
        pattern.Notes.Add(new Note { ChannelId = 1, Tick = 0, Key = 60, Velocity = 0.9, LengthTicks = 48 });
        pattern.Notes.Add(new Note { ChannelId = 2, Tick = 96, Key = 69, Velocity = 0.8, LengthTicks = 192 });
        document.Patterns.Add(pattern);
        document.Playlist.Add(new Clip { PatternId = 10, StartTick = 0 });
        document.Assets.Add(new AssetRef { Id = "asset-kick", Path = "assets/kick.wav" });
        return document;
    }

    private string WriteAsset()
    {
        string path = Path.Combine(_workDir, "kick.wav");
        File.WriteAllBytes(path, Encoding.ASCII.GetBytes("fake-wav-payload-0123456789"));
        return path;
    }

    [Test]
    public void Save_load_roundtrip_is_equivalent()
    {
        var store = new WeftProjectStore();
        var document = NewDocument();
        string asset = WriteAsset();
        string package = Path.Combine(_workDir, "a.weft");
        store.Save(package, document, new Dictionary<string, string> { ["assets/kick.wav"] = asset });

        LoadedProject loaded = store.Load(package);
        ProjectDocument doc = loaded.Document;
        Assert.Multiple(() =>
        {
            Assert.That(doc.Tempo, Is.EqualTo(128));
            Assert.That(doc.SampleRate, Is.EqualTo(48_000));
            Assert.That(doc.Channels.Count, Is.EqualTo(2));
            Assert.That(doc.Channels[0].Kind, Is.EqualTo(ChannelKind.Sampler));
            Assert.That(doc.Channels[0].Volume, Is.EqualTo(0.9));
            Assert.That(doc.Channels[0].Pan, Is.EqualTo(-0.1));
            Assert.That(doc.Channels[0].AssetId, Is.EqualTo("asset-kick"));
            Assert.That(doc.Patterns[0].Notes.Count, Is.EqualTo(2));
            Assert.That(doc.Patterns[0].Notes[1].Tick, Is.EqualTo(96));
            Assert.That(doc.Playlist.Count, Is.EqualTo(1));
        });
        Assert.That(doc.Assets[0].Sha256, Has.Length.EqualTo(64));

        using var stream = loaded.OpenAsset(doc.Assets[0]);
        using var reader = new StreamReader(stream);
        Assert.That(reader.ReadToEnd(), Is.EqualTo("fake-wav-payload-0123456789"));
    }

    [Test]
    public void Load_rejects_zip_slip()
    {
        string package = Path.Combine(_workDir, "evil.weft");
        using (var zip = ZipFile.Open(package, ZipArchiveMode.Create))
        {
            ZipArchiveEntry entry = zip.CreateEntry("../evil.txt");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("boom");
        }
        Assert.That(() => new WeftProjectStore().Load(package),
            Throws.TypeOf<InvalidProjectException>().With.Message.Contains("非法 zip 条目"));
    }

    [Test]
    public void Load_rejects_wrong_schema_version()
    {
        string package = Path.Combine(_workDir, "v2.weft");
        using (var zip = ZipFile.Open(package, ZipArchiveMode.Create))
        {
            Write(zip, "metadata.json", """{"schemaVersion":2,"generator":"x"}""");
            Write(zip, "project.json", """{"schemaVersion":2}""");
        }
        Assert.That(() => new WeftProjectStore().Load(package),
            Throws.TypeOf<InvalidProjectException>().With.Message.Contains("schema 版本不支持"));
    }

    [Test]
    public void Load_rejects_missing_project_json()
    {
        string package = Path.Combine(_workDir, "nojson.weft");
        using (var zip = ZipFile.Open(package, ZipArchiveMode.Create))
        {
            Write(zip, "metadata.json", """{"schemaVersion":1,"generator":"x"}""");
        }
        Assert.That(() => new WeftProjectStore().Load(package),
            Throws.TypeOf<InvalidProjectException>().With.Message.Contains("project.json"));
    }

    [Test]
    public void Load_rejects_tampered_asset()
    {
        var store = new WeftProjectStore();
        string asset = WriteAsset();
        string package = Path.Combine(_workDir, "tampered.weft");
        store.Save(package, NewDocument(), new Dictionary<string, string> { ["assets/kick.wav"] = asset });

        string tampered = Path.Combine(_workDir, "tampered2.weft");
        using (var source = ZipFile.OpenRead(package))
        using (var target = ZipFile.Open(tampered, ZipArchiveMode.Create))
        {
            foreach (ZipArchiveEntry entry in source.Entries)
            {
                ZipArchiveEntry clone = target.CreateEntry(entry.FullName);
                using Stream input = entry.Open();
                using Stream output = clone.Open();
                if (entry.FullName == "assets/kick.wav")
                {
                    byte[] evil = Encoding.ASCII.GetBytes("tampered!!");
                    output.Write(evil);
                }
                else
                {
                    input.CopyTo(output);
                }
            }
        }
        Assert.That(() => store.Load(tampered),
            Throws.TypeOf<InvalidProjectException>().With.Message.Contains("SHA256 不匹配"));
    }

    [Test]
    public void Load_rejects_note_referencing_unknown_channel()
    {
        string package = Path.Combine(_workDir, "badref.weft");
        using (var zip = ZipFile.Open(package, ZipArchiveMode.Create))
        {
            Write(zip, "metadata.json", """{"schemaVersion":1,"generator":"x"}""");
            Write(zip, "project.json", """
                {"schemaVersion":1,"tempo":140,"sampleRate":48000,
                 "channels":[{"id":1,"name":"S","kind":"sine","volume":1.0,"pan":0.0}],
                 "patterns":[{"id":10,"name":"P","notes":[{"channelId":99,"tick":0,"key":60,"velocity":1.0,"lengthTicks":96}]}],
                 "playlist":[],"assets":[]}
                """);
        }
        Assert.That(() => new WeftProjectStore().Load(package),
            Throws.TypeOf<InvalidProjectException>().With.Message.Contains("未知 channel"));
    }

    [Test]
    public void Automation_lanes_roundtrip_through_schema()
    {
        var store = new WeftProjectStore();
        var document = NewDocument();
        AutomationLanes.Insert(document.Channels[0].VolumePoints, new AutomationPoint { Tick = 0, Value = 0.5 });
        AutomationLanes.Insert(document.Channels[0].VolumePoints, new AutomationPoint { Tick = 192, Value = 1.25 });
        AutomationLanes.Insert(document.Channels[0].PanPoints, new AutomationPoint { Tick = 96, Value = -0.75 });
        AutomationLanes.Insert(document.Channels[1].VolumePoints, new AutomationPoint { Tick = 48, Value = 2.0 });
        AutomationLanes.Insert(document.MasterVolumePoints, new AutomationPoint { Tick = 384, Value = 0.8 });
        string package = Path.Combine(_workDir, "auto.weft");
        store.Save(package, document, new Dictionary<string, string> { ["assets/kick.wav"] = WriteAsset() });

        ProjectDocument loaded = store.Load(package).Document;
        Assert.Multiple(() =>
        {
            Assert.That(loaded.Channels[0].VolumePoints.Select(p => (p.Tick, p.Value)),
                Is.EqualTo(document.Channels[0].VolumePoints.Select(p => (p.Tick, p.Value))), "volume lane 往返等价");
            Assert.That(loaded.Channels[0].PanPoints.Select(p => (p.Tick, p.Value)),
                Is.EqualTo(document.Channels[0].PanPoints.Select(p => (p.Tick, p.Value))), "pan lane 往返等价");
            Assert.That(loaded.Channels[1].VolumePoints, Has.Count.EqualTo(1));
            Assert.That(loaded.Channels[1].PanPoints, Is.Empty, "空 lane 不写入");
            Assert.That(loaded.MasterVolumePoints.Select(p => (p.Tick, p.Value)),
                Is.EqualTo(document.MasterVolumePoints.Select(p => (p.Tick, p.Value))), "master lane 往返等价");
        });
    }

    [Test]
    public void Load_without_automation_fields_yields_empty_lanes()
    {
        var store = new WeftProjectStore();
        string package = Path.Combine(_workDir, "legacy.weft");
        store.Save(package, NewDocument(), new Dictionary<string, string> { ["assets/kick.wav"] = WriteAsset() });

        ProjectDocument loaded = store.Load(package).Document;
        Assert.Multiple(() =>
        {
            Assert.That(loaded.Channels[0].VolumePoints, Is.Empty);
            Assert.That(loaded.Channels[0].PanPoints, Is.Empty);
            Assert.That(loaded.MasterVolumePoints, Is.Empty);
        });
    }

    [TestCase("""{"tick":192,"value":0.5},{"tick":96,"value":0.3}""", "未严格升序")]
    [TestCase("""{"tick":96,"value":0.5},{"tick":96,"value":0.3}""", "未严格升序")]
    [TestCase("""{"tick":96,"value":2.5}""", "值越界")]
    [TestCase("""{"tick":-1,"value":0.5}""", "tick 越界")]
    public void Load_rejects_invalid_volume_lane(string laneJson, string expectedError)
    {
        string package = Path.Combine(_workDir, "badlane.weft");
        using (var zip = ZipFile.Open(package, ZipArchiveMode.Create))
        {
            Write(zip, "metadata.json", """{"schemaVersion":1,"generator":"x"}""");
            Write(zip, "project.json", $$"""
                {"schemaVersion":1,"tempo":140,"sampleRate":48000,
                 "channels":[{"id":1,"name":"S","kind":"sine","volume":1.0,"pan":0.0,"volumePoints":[{{laneJson}}]}],
                 "patterns":[],"playlist":[],"assets":[]}
                """);
        }
        Assert.That(() => new WeftProjectStore().Load(package),
            Throws.TypeOf<InvalidProjectException>().With.Message.Contains(expectedError));
    }

    [Test]
    public void Plugin_refs_roundtrip_and_channel_binds_by_id()
    {
        var store = new WeftProjectStore();
        var document = NewDocument();
        document.Plugins.Add(new PluginRef { Id = "test.sine", Path = @"C:\plug\test.clap" });
        document.Channels.Add(new Channel
        {
            Id = 3, Name = "Syn", Kind = ChannelKind.Plugin, PluginId = "test.sine",
        });
        string package = Path.Combine(_workDir, "plugin.weft");
        store.Save(package, document, new Dictionary<string, string> { ["assets/kick.wav"] = WriteAsset() });

        ProjectDocument loaded = store.Load(package).Document;
        PluginRef plugin = loaded.Plugins.Single();
        Channel bound = loaded.Channels.Single(c => c.Kind == ChannelKind.Plugin);
        Assert.Multiple(() =>
        {
            Assert.That(plugin.Id, Is.EqualTo("test.sine"));
            Assert.That(plugin.Path, Is.EqualTo(@"C:\plug\test.clap"));
            Assert.That(bound.PluginId, Is.EqualTo("test.sine"));
            Assert.That(loaded.Channels[0].PluginId, Is.Null, "非 plugin 通道不写字段");
        });
    }

    [Test]
    public void Plugin_param_lanes_roundtrip_through_schema()
    {
        var store = new WeftProjectStore();
        var document = NewDocument();
        document.Plugins.Add(new PluginRef { Id = "p", Path = "p.clap" });
        document.Channels.Add(new Channel { Id = 3, Kind = ChannelKind.Plugin, PluginId = "p" });
        List<AutomationPoint> lane = document.Channels[^1].GetPluginParamPoints(7);
        AutomationLanes.Insert(lane, new AutomationPoint { Tick = 0, Value = 0.25 });
        AutomationLanes.Insert(lane, new AutomationPoint { Tick = 192, Value = 0.75 });
        string package = Path.Combine(_workDir, "paramlane.weft");
        store.Save(package, document, new Dictionary<string, string> { ["assets/kick.wav"] = WriteAsset() });

        ProjectDocument loaded = store.Load(package).Document;
        List<AutomationPoint> restored = loaded.Channels.Single(c => c.Id == 3).GetPluginParamPoints(7);
        Assert.That(restored.Select(p => (p.Tick, p.Value)),
            Is.EqualTo(new (long, double)[] { (0, 0.25), (192, 0.75) }));
    }

    [Test]
    public void Stretch_speed_roundtrips_and_defaults_to_unity()
    {
        var store = new WeftProjectStore();
        var document = NewDocument();
        document.Channels[0].StretchSpeed = 1.5;
        string package = Path.Combine(_workDir, "stretch.weft");
        store.Save(package, document, new Dictionary<string, string> { ["assets/kick.wav"] = WriteAsset() });

        ProjectDocument loaded = store.Load(package).Document;
        Assert.Multiple(() =>
        {
            Assert.That(loaded.Channels[0].StretchSpeed, Is.EqualTo(1.5));
            Assert.That(loaded.Channels[1].StretchSpeed, Is.EqualTo(1.0), "缺省 1.0");
        });
    }

    [Test]
    public void Effect_chain_roundtrips_through_schema()
    {
        var store = new WeftProjectStore();
        var document = NewDocument();
        var eq = new EffectSlot { Kind = EffectKind.Eq3Band };
        eq.Parameters["low"] = 4.5;
        eq.Parameters["high"] = -2.0;
        var delay = new EffectSlot { Kind = EffectKind.Delay, Bypassed = true };
        document.Channels[0].Effects.Slots.Add(eq);
        document.Channels[0].Effects.Slots.Add(delay);
        string package = Path.Combine(_workDir, "fx.weft");
        store.Save(package, document, new Dictionary<string, string> { ["assets/kick.wav"] = WriteAsset() });

        ProjectDocument loaded = store.Load(package).Document;
        List<EffectSlot> chain = loaded.Channels[0].Effects.Slots;
        Assert.Multiple(() =>
        {
            Assert.That(chain, Has.Count.EqualTo(2));
            Assert.That(chain[0].Kind, Is.EqualTo(EffectKind.Eq3Band));
            Assert.That(chain[0].Parameters["low"], Is.EqualTo(4.5));
            Assert.That(chain[0].Parameters["high"], Is.EqualTo(-2.0));
            Assert.That(chain[0].Bypassed, Is.False);
            Assert.That(chain[1].Kind, Is.EqualTo(EffectKind.Delay));
            Assert.That(chain[1].Bypassed, Is.True);
            Assert.That(loaded.Channels[1].Effects.Slots, Is.Empty, "无效果通道不写字段");
        });
    }

    [Test]
    public void Load_rejects_unknown_plugin_reference()
    {
        string package = Path.Combine(_workDir, "badplugin.weft");
        using (var zip = ZipFile.Open(package, ZipArchiveMode.Create))
        {
            Write(zip, "metadata.json", """{"schemaVersion":1,"generator":"x"}""");
            Write(zip, "project.json", """
                {"schemaVersion":1,"tempo":140,"sampleRate":48000,
                 "channels":[{"id":1,"name":"S","kind":"plugin","volume":1.0,"pan":0.0,"pluginId":"missing"}],
                 "patterns":[],"playlist":[],"assets":[]}
                """);
        }
        Assert.That(() => new WeftProjectStore().Load(package),
            Throws.TypeOf<InvalidProjectException>().With.Message.Contains("未知 plugin"));
    }

    private static void Write(ZipArchive zip, string name, string content)
    {
        ZipArchiveEntry entry = zip.CreateEntry(name);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
    }
}
