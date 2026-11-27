using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Input;
using H.NotifyIcon;
using H.NotifyIcon.EfficiencyMode;
using Microsoft.UI.Xaml.Controls;

namespace MixerDeAudio.Tray;

/// <summary>Estado do ícone da bandeja (Fase 16, D10): normal, com celular conectado (ponto verde) ou com a tela sendo vista (ponto vermelho).</summary>
internal enum TrayState
{
    Normal,
    PhoneConnected,
    ScreenShared,
}

/// <summary>
/// Ícone da bandeja (D11 a D13), criado pelo código, sem XAML. Clique esquerdo alterna a janelinha;
/// o botão direito mostra o menu nativo com "Abrir", "Configurações" e "Sair".
/// </summary>
internal sealed class TrayIconService : IDisposable
{
    private readonly TaskbarIcon _icon;
    private readonly Icon _iconImage;
    private Icon? _connectedIcon, _screenIcon; // Fase 16, D10: gerados a partir do ícone atual na primeira vez que são usados
    private TrayState _state = TrayState.Normal;

    private readonly MenuFlyout _menu;
    private MenuFlyoutItem? _cancelItem;

    public TrayIconService(Action onToggle, Action onOpen, Action onSettings, Action onExit)
    {
        _iconImage = new Icon(Path.Combine(AppContext.BaseDirectory, "Assets", "MixerDeAudio.ico"));

        var open = new MenuFlyoutItem { Text = "Abrir", Command = new ActionCommand(onOpen) };
        var settings = new MenuFlyoutItem { Text = "Configurações", Command = new ActionCommand(onSettings) };
        var exit = new MenuFlyoutItem { Text = "Sair", Command = new ActionCommand(onExit) };
        var menu = _menu = new MenuFlyout();
        menu.Items.Add(open);
        menu.Items.Add(settings);
        menu.Items.Add(exit);

        _icon = new TaskbarIcon
        {
            ToolTipText = "Controladora PC",
            Icon = _iconImage,
            ContextMenuMode = ContextMenuMode.PopupMenu,
            ContextFlyout = menu,
            NoLeftClickDelay = true,
            LeftClickCommand = new ActionCommand(onToggle),
        };
        _icon.ForceCreate(enablesEfficiencyMode: false);
    }

    /// <summary>
    /// Troca o ícone conforme o estado (Fase 16, D10). As variantes são o ícone atual com um ponto colorido no canto,
    /// desenhado em tempo de execução (sem arquivo novo). Thread da tela.
    /// </summary>
    public void SetState(TrayState state)
    {
        if (state == _state) return;
        _state = state;
        try
        {
            _icon.Icon = state switch
            {
                TrayState.PhoneConnected => _connectedIcon ??= WithDot(_iconImage, Color.FromArgb(46, 204, 113)),
                TrayState.ScreenShared => _screenIcon ??= WithDot(_iconImage, Color.FromArgb(232, 65, 65)),
                _ => _iconImage,
            };
            _icon.ToolTipText = state switch
            {
                TrayState.PhoneConnected => "Controladora PC: celular conectado",
                TrayState.ScreenShared => "Controladora PC: tela do PC sendo vista no celular",
                _ => "Controladora PC",
            };
        }
        catch (Exception ex)
        {
            Services.Log.Write($"ERRO bandeja: icone: {ex.Message}");
        }
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);

    /// <summary>O ícone com um ponto colorido (borda branca) no canto inferior direito, em 32 x 32.</summary>
    private static Icon WithDot(Icon source, Color color)
    {
        using var sized = new Icon(source, new Size(32, 32));
        using var bitmap = new Bitmap(sized.ToBitmap(), new Size(32, 32));
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var ring = new SolidBrush(Color.White);
            using var fill = new SolidBrush(color);
            g.FillEllipse(ring, 15, 15, 17, 17);
            g.FillEllipse(fill, 18, 18, 11, 11);
        }
        var handle = bitmap.GetHicon();
        try
        {
            using var fromHandle = Icon.FromHandle(handle);
            return (Icon)fromHandle.Clone(); // a cópia é dona do próprio ícone; o identificador original é solto abaixo
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    /// <summary>Balão da bandeja (Fase 9, D8). O Windows pode escondê-lo no modo "Não incomodar".</summary>
    public void ShowBalloon(string title, string text) => _icon.ShowNotification(title, text);

    /// <summary>
    /// Item "Cancelar ... programado" do menu (Fase 9, D8): aparece enquanto houver timer de energia; <paramref name="text"/> nulo remove.
    /// </summary>
    public void SetCancelItem(string? text, Action onCancel)
    {
        if (text is null)
        {
            if (_cancelItem is not null) _menu.Items.Remove(_cancelItem);
            _cancelItem = null;
            return;
        }
        if (_cancelItem is null)
        {
            _cancelItem = new MenuFlyoutItem { Command = new ActionCommand(onCancel) };
            _menu.Items.Insert(_menu.Items.Count - 1, _cancelItem); // antes de "Sair"
        }
        _cancelItem.Text = text;
    }

    /// <summary>Modo de eficiência do Windows (D13): ligado com a janela fechada.</summary>
    public static void SetEfficiencyMode(bool enabled) => EfficiencyModeUtilities.SetEfficiencyMode(enabled);

    public void Dispose()
    {
        _icon.Dispose();
        _iconImage.Dispose();
        _connectedIcon?.Dispose();
        _screenIcon?.Dispose();
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
