#:property TargetFramework=net10.0-windows10.0.22621.0
#:property PublishAot=false
#:property NoWarn=CA1416

// Sonda somente leitura do controle de mídia do Windows (GSMTC). Não aperta nenhum botão.
using Windows.Media.Control;

var sw = System.Diagnostics.Stopwatch.StartNew();
var mgr = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
Console.WriteLine($"RequestAsync: {sw.ElapsedMilliseconds} ms");
var cur = mgr.GetCurrentSession();
Console.WriteLine($"atual: {cur?.SourceAppUserModelId ?? "(nenhuma)"}");
foreach (var s in mgr.GetSessions())
{
    var info = s.GetPlaybackInfo();
    var c = info.Controls;
    string titulo = "?", artista = "?";
    try
    {
        var p = await s.TryGetMediaPropertiesAsync();
        titulo = p.Title; artista = p.Artist;
    }
    catch (Exception ex) { titulo = "ERRO " + ex.GetType().Name; }
    Console.WriteLine($"AUMID='{s.SourceAppUserModelId}' estado={info.PlaybackStatus} tipo={info.PlaybackType} " +
        $"play={c.IsPlayEnabled} pause={c.IsPauseEnabled} toggle={c.IsPlayPauseToggleEnabled} prox={c.IsNextEnabled} ant={c.IsPreviousEnabled} | '{titulo}' / '{artista}'");
}
