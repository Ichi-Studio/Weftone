using Weftone.Domain;

namespace Weftone.Domain.Commands;

/// <summary>添加效果器到通道链尾（SPEC M5 D2：通道内串联）。</summary>
public sealed class AddEffectSlotCommand(int channelId, EffectSlot slot) : ICommand
{
    private readonly int _channelId = channelId;
    private readonly EffectSlot _slot = slot;

    public string Title => $"添加效果 ch={_channelId} {_slot.Kind}";

    public void Apply(ProjectDocument document) => Find(document).Add(_slot);

    public void Revert(ProjectDocument document) => Find(document).Remove(_slot);

    private List<EffectSlot> Find(ProjectDocument document)
    {
        return (document.Channels.FirstOrDefault(c => c.Id == _channelId)
            ?? throw new InvalidOperationException($"channel {_channelId} 不存在")).Effects.Slots;
    }
}

/// <summary>移除效果器（按引用）。</summary>
public sealed class RemoveEffectSlotCommand(int channelId, EffectSlot slot) : ICommand
{
    private readonly int _channelId = channelId;
    private readonly EffectSlot _slot = slot;
    private int _index = -1;

    public string Title => $"移除效果 ch={_channelId} {_slot.Kind}";

    public void Apply(ProjectDocument document)
    {
        List<EffectSlot> slots = Find(document);
        _index = slots.IndexOf(_slot);
        if (_index < 0)
        {
            throw new InvalidOperationException("效果槽不存在");
        }
        slots.RemoveAt(_index);
    }

    public void Revert(ProjectDocument document) => Find(document).Insert(_index, _slot);

    private List<EffectSlot> Find(ProjectDocument document)
    {
        return (document.Channels.FirstOrDefault(c => c.Id == _channelId)
            ?? throw new InvalidOperationException($"channel {_channelId} 不存在")).Effects.Slots;
    }
}

/// <summary>移动效果器顺序（from/to 显式）。</summary>
public sealed class MoveEffectSlotCommand(int channelId, EffectSlot slot, int fromIndex, int toIndex) : ICommand
{
    private readonly int _channelId = channelId;
    private readonly EffectSlot _slot = slot;
    private readonly int _fromIndex = fromIndex;
    private readonly int _toIndex = toIndex;

    public string Title => $"移动效果 ch={_channelId} {_slot.Kind} #{_fromIndex}→#{_toIndex}";

    public void Apply(ProjectDocument document)
    {
        List<EffectSlot> slots = Find(document);
        if (slots.IndexOf(_slot) != _fromIndex)
        {
            throw new InvalidOperationException("效果槽位置与命令不符");
        }
        slots.RemoveAt(_fromIndex);
        slots.Insert(Math.Clamp(_toIndex, 0, slots.Count), _slot);
    }

    public void Revert(ProjectDocument document)
    {
        List<EffectSlot> slots = Find(document);
        slots.Remove(_slot);
        slots.Insert(_fromIndex, _slot);
    }

    private List<EffectSlot> Find(ProjectDocument document)
    {
        return (document.Channels.FirstOrDefault(c => c.Id == _channelId)
            ?? throw new InvalidOperationException($"channel {_channelId} 不存在")).Effects.Slots;
    }
}

/// <summary>设置效果参数（from/to 显式，拖拽预览-后提交）。</summary>
public sealed class SetEffectParameterCommand(
    int channelId, EffectSlot slot, string parameter, double fromValue, double toValue) : ICommand
{
    private readonly int _channelId = channelId;
    private readonly EffectSlot _slot = slot;
    private readonly string _parameter = parameter;
    private readonly double _fromValue = fromValue;
    private readonly double _toValue = toValue;

    public string Title => $"效果参数 ch={_channelId} {_slot.Kind}.{_parameter}: {_fromValue:0.###}→{_toValue:0.###}";

    public void Apply(ProjectDocument document) => Find(document)[_parameter] = _toValue;

    public void Revert(ProjectDocument document) => Find(document)[_parameter] = _fromValue;

    private Dictionary<string, double> Find(ProjectDocument document)
    {
        if (document.Channels.FirstOrDefault(c => c.Id == _channelId) is not { } channel
            || !channel.Effects.Slots.Contains(_slot))
        {
            throw new InvalidOperationException("效果槽不存在");
        }
        return _slot.Parameters;
    }
}

/// <summary>切换效果旁路（保留参数与位置）。</summary>
public sealed class SetEffectBypassCommand(int channelId, EffectSlot slot, bool fromBypassed, bool toBypassed)
    : ICommand
{
    private readonly int _channelId = channelId;
    private readonly EffectSlot _slot = slot;
    private readonly bool _fromBypassed = fromBypassed;
    private readonly bool _toBypassed = toBypassed;

    public string Title => $"效果旁路 ch={_channelId} {_slot.Kind}: {_toBypassed}";

    public void Apply(ProjectDocument document) => Find().Bypassed = _toBypassed;

    public void Revert(ProjectDocument document) => Find().Bypassed = _fromBypassed;

    private EffectSlot Find()
    {
        if (_slot is null)
        {
            throw new InvalidOperationException("效果槽不存在");
        }
        return _slot;
    }
}
