using System;
using System.Drawing;
using System.IO;
using System.Windows.Input;
using H.NotifyIcon;
using H.NotifyIcon.EfficiencyMode;
using Microsoft.UI.Xaml.Controls;

namespace MixerDeAudio.Tray;

/// <summary>
/// Ícone da bandeja (D11 a D13), criado pelo código, sem XAML. Clique esquerdo alterna a janelinha;
/// o botão direito mostra o menu nativo com "Abrir", "Configurações" e "Sair".
/// </summary>
internal sealed class TrayIconService : IDisposable
{
    private readonly TaskbarIcon _icon;
    private readonly Icon _iconImage;

    public TrayIconService(Action onToggle, Action onOpen, Action onSettings, Action onExit)
    {
        _iconImage = new Icon(Path.Combine(AppContext.BaseDirectory, "Assets", "MixerDeAudio.ico"));

        var open = new MenuFlyoutItem { Text = "Abrir", Command = new ActionCommand(onOpen) };
        var settings = new MenuFlyoutItem { Text = "Configurações", Command = new ActionCommand(onSettings) };
        var exit = new MenuFlyoutItem { Text = "Sair", Command = new ActionCommand(onExit) };
        var menu = new MenuFlyout();
        menu.Items.Add(open);
        menu.Items.Add(settings);
        menu.Items.Add(exit);

        _icon = new TaskbarIcon
        {
            ToolTipText = "Mixer de Áudio",
            Icon = _iconImage,
            ContextMenuMode = ContextMenuMode.PopupMenu,
            ContextFlyout = menu,
            NoLeftClickDelay = true,
            LeftClickCommand = new ActionCommand(onToggle),
        };
        _icon.ForceCreate(enablesEfficiencyMode: false);
    }

    /// <summary>Modo de eficiência do Windows (D13): ligado com a janela fechada.</summary>
    public static void SetEfficiencyMode(bool enabled) => EfficiencyModeUtilities.SetEfficiencyMode(enabled);

    public void Dispose()
    {
        _icon.Dispose();
        _iconImage.Dispose();
    }

    private sealed class ActionCommand(Action action) : ICommand
    {
#pragma warning disable CS0067 // o comando está sempre habilitado
        public event EventHandler? CanExecuteChanged;
#pragma warning restore CS0067
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => action();
    }
}
