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
        Model = new SettingsViewModel(store, devices, remote, App.Updates, App.CurrentVersion);
        InitializeComponent();
        FavoriteList.ItemsSource = Model.Favorites;
        NotesList.ItemsSource = Model.NoteItems;
    }

    private void OnNewCodeClick(object sender, RoutedEventArgs e) => Model.NewCode();

    private void OnDisconnectClick(object sender, RoutedEventArgs e) => Model.Disconnect();

    private void OnForgetPhoneClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is string hash) Model.ForgetPhone(hash);
    }

    private void OnShortcutsFolderClick(object sender, RoutedEventArgs e) => Model.OpenShortcutsFolder();

    private void OnUpdateCheckClick(object sender, RoutedEventArgs e) => Model.CheckNow();

    private void OnUpdateDownloadClick(object sender, RoutedEventArgs e) => Model.DownloadUpdate();

    private void OnUpdateRestartClick(object sender, RoutedEventArgs e) => Model.RestartToUpdate();

    private void OnBackClick(object sender, RoutedEventArgs e) => BackRequested?.Invoke();

    /// <summary>Solta os ouvintes do modelo ao voltar ou fechar.</summary>
    public void Release()
    {
        FavoriteList.ItemsSource = null;
        NotesList.ItemsSource = null;
        Model.Dispose();
    }
}
