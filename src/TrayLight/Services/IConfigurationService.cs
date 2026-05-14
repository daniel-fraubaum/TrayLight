using System.ComponentModel;
using TrayLight.Models;

namespace TrayLight.Services;

public interface IConfigurationService : INotifyPropertyChanged, IDisposable
{
    /// <summary>The currently active configuration. Replaced atomically on reload.</summary>
    AppConfiguration Current { get; }

    /// <summary>Resolved absolute path of the config file being watched.</summary>
    string ConfigPath { get; }

    /// <summary>Timestamp of the last successful load (UTC).</summary>
    DateTime? LastLoadedUtc { get; }

    /// <summary>Force a synchronous reload from disk.</summary>
    AppConfiguration Load();

    /// <summary>
    /// Begin watching the config file for changes. Subsequent successful
    /// reloads raise <see cref="INotifyPropertyChanged.PropertyChanged"/>
    /// for <see cref="Current"/> and <see cref="LastLoadedUtc"/>.
    /// </summary>
    void StartWatching();
}
