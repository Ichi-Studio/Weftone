using Weftone.Abstractions.Plugins;

namespace Weftone.Adapters.Plugins.Clap;

/// <summary>CLAP 实例工厂（SPEC M3 D5）：Application 经端口注入，不直连实现。</summary>
public sealed class ClapInstanceFactory : IPluginInstanceFactory
{
    public IAudioPluginInstance Create(string clapPath, string pluginId, double sampleRate)
        => ClapPluginInstance.Create(clapPath, pluginId, sampleRate);
}
