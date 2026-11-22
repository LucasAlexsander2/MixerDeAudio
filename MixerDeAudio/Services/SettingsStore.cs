using System;
using System.IO;
using System.Text;

namespace MixerDeAudio.Services;

/// <summary>
/// Configuração em memória (D2): o arquivo é lido uma vez, na partida; cada mudança grava na hora, de forma atômica
/// (<c>config.json.tmp</c> e depois <c>File.Move</c>). Erro de leitura ou gravação vai para o log e o app segue.
/// </summary>
public sealed class SettingsStore
{
    private AppSettings _current = AppSettings.Default;

    public AppSettings Current => _current;

    /// <summary>O <c>config.json</c> existia ao carregar (Fase 11, D8): sem ele, é instalação nova e o cartão de novidades não aparece.</summary>
    public bool ExistedOnLoad { get; private set; }

    /// <summary>Avisa depois de cada mudança (na thread de quem mudou, a da tela).</summary>
    public event Action? Changed;

    public static SettingsStore Load()
    {
        var store = new SettingsStore();
        try
        {
            if (File.Exists(AppPaths.ConfigFile))
            {
                store.ExistedOnLoad = true;
                var text = File.ReadAllText(AppPaths.ConfigFile, Encoding.UTF8);
                store._current = AppSettings.Parse(text, out var invalid);
                if (invalid) Log.Write("ERRO config invalida, usando o padrao");
            }
        }
        catch (Exception ex)
        {
            Log.Write($"ERRO config invalida, usando o padrao ({ex.Message})");
            store._current = AppSettings.Default;
        }
        return store;
    }

    /// <summary>Aplica a mudança, grava e avisa. Se a gravação falhar, o valor em memória vale.</summary>
    public void Update(Func<AppSettings, AppSettings> change)
    {
        _current = change(_current).Normalized();
        Save();
        Changed?.Invoke();
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DataFolder);
            var tmp = AppPaths.ConfigFile + ".tmp";
            File.WriteAllText(tmp, _current.Serialize(), new UTF8Encoding(false));
            File.Move(tmp, AppPaths.ConfigFile, overwrite: true);
        }
        catch (Exception ex)
        {
            Log.Write($"ERRO ao gravar a config: {ex.Message}");
        }
    }
}
