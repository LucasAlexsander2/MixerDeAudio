#:property TargetFramework=net10.0-windows10.0.22621.0
#:property PublishAot=false
#:property NoWarn=CA1416

// Sonda: um processo de teste que se registra no controle de mídia do Windows e anota os botões recebidos.
using Windows.Media;
using Windows.Media.Playback;

var player = new MediaPlayer();
player.CommandManager.IsEnabled = false;
var smtc = player.SystemMediaTransportControls;
smtc.IsEnabled = true;
smtc.IsPlayEnabled = true;
smtc.IsPauseEnabled = true;
smtc.IsNextEnabled = true;
smtc.IsPreviousEnabled = true;
smtc.PlaybackStatus = MediaPlaybackStatus.Playing;
smtc.DisplayUpdater.Type = MediaPlaybackType.Music;
smtc.DisplayUpdater.MusicProperties.Title = "Faixa 1";
smtc.DisplayUpdater.MusicProperties.Artist = "MX-teste";
smtc.DisplayUpdater.Update();
var faixa = 1;
smtc.ButtonPressed += (s, e) =>
{
    Console.WriteLine($"botao: {e.Button}");
    switch (e.Button)
    {
        case SystemMediaTransportControlsButton.Play: smtc.PlaybackStatus = MediaPlaybackStatus.Playing; break;
        case SystemMediaTransportControlsButton.Pause: smtc.PlaybackStatus = MediaPlaybackStatus.Paused; break;
        case SystemMediaTransportControlsButton.Next: faixa++; break;
        case SystemMediaTransportControlsButton.Previous: faixa = Math.Max(1, faixa - 1); break;
    }
    smtc.DisplayUpdater.MusicProperties.Title = $"Faixa {faixa}";
    smtc.DisplayUpdater.Update();
};
Console.WriteLine("pronto");
await Task.Delay(TimeSpan.FromSeconds(int.Parse(args.Length > 0 ? args[0] : "20")));
