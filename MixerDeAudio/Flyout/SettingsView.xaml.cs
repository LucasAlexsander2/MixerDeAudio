using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MixerDeAudio.Audio;
using MixerDeAudio.Services;
using MixerDeAudio.ViewModels;

namespace MixerDeAudio.Flyout;

/// <summary>Página de configurações dentro da janelinha (P1, D9). Criada só quando abre e solta ao voltar.</summary>
public sealed partial class SettingsView : UserControl
{
    internal SettingsViewModel Model { get; }

    /// <summary>Pedido de voltar à página principal (botão voltar ou Esc).</summary>
    public event Action? BackRequested;

    internal SettingsView(SettingsStore store, DeviceTracker devices, Remote.RemoteServer? remote = null)
    {
        Model = new SettingsViewModel(store, devices, remote);
        InitializeComponent();
        FavoriteList.ItemsSource = Model.Favorites;
    }

    private void OnNewCodeClick(object sender, RoutedEventArgs e) => Model.NewCode();

    private void OnDisconnectClick(object sender, RoutedEventArgs e) => Model.Disconnect();

    private void OnShortcutsFolderClick(object sender, RoutedEventArgs e) => Model.OpenShortcutsFolder();

    private void OnBackClick(object sender, RoutedEventArgs e) => BackRequested?.Invoke();

    /// <summary>Solta os ouvintes do modelo ao voltar ou fechar.</summary>
    public void Release()
    {
        FavoriteList.ItemsSource = null;
        Model.Dispose();
    }
}
