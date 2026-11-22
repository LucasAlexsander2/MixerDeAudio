using System;
using System.IO;
using System.Text;

namespace MixerDeAudio.Pc;

/// <summary>Regras puras do envio de arquivo do celular (Fase 9d, D26 e D28): nome limpo, nome livre, limites e pastas.</summary>
public static class FileNameRules
{
    public const int MaxNameLength = 120;
    public const string DefaultName = "arquivo";
    public const string PartialSuffix = ".parcial";
    public const long MaxBytes = 500L * 1024 * 1024;
    /// <summary>No <c>--teste-pc</c> (D28) o limite cai para 5 MB, para testar o 413 sem enviar 500 MB.</summary>
    public const long TestMaxBytes = 5L * 1024 * 1024;
    /// <summary>Espaço que tem que sobrar no disco além do tamanho do arquivo (D26).</summary>
    public const long FreeMargin = 1024L * 1024 * 1024;

    private static readonly string[] Reserved =
    {
        "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    public static long Limit(bool testMode) => testMode ? TestMaxBytes : MaxBytes;

    /// <summary>Cabe no disco: o espaço livre tem que ser pelo menos o tamanho + 1 GB.</summary>
    public static bool HasRoom(long freeBytes, long size) => freeBytes >= size + FreeMargin;

    /// <summary>
    /// Nome seguro para gravar na pasta: sem caminho (só o que vem depois da última barra), sem caracteres proibidos nem de
    /// controle, sem ponto ou espaço no fim, sem nome reservado do Windows (<c>CON</c>, <c>NUL</c>...), até 120 caracteres
    /// (mantém a extensão). Nada que sobre vira <c>arquivo</c>.
    /// </summary>
    public static string Clean(string? raw)
    {
        var s = raw ?? "";
        var slash = Math.Max(s.LastIndexOf('/'), s.LastIndexOf((char)92));
        if (slash >= 0) s = s.Substring(slash + 1);
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            if (c < 32 || c is '<' or '>' or ':' or '"' or '|' or '?' or '*' or '/' or (char)92) continue;
            sb.Append(c);
        }
        var name = sb.ToString().Trim().TrimEnd('.', ' ').TrimStart(' ');
        if (name.Length == 0) return DefaultName;
        // ".gitignore" e afins: sem nome antes do ponto não é problema; só os reservados
        var dot = name.IndexOf('.');
        var stem = dot < 0 ? name : name.Substring(0, dot);
        foreach (var r in Reserved)
            if (string.Equals(stem.TrimEnd(), r, StringComparison.OrdinalIgnoreCase)) { name = "_" + name; break; }
        if (name.Length > MaxNameLength)
        {
            var ext = Path.GetExtension(name);
            if (ext.Length > 20) ext = ext.Substring(0, 20);
            var keep = Math.Max(1, MaxNameLength - ext.Length);
            name = name.Substring(0, keep) + ext;
            if (name.Length > MaxNameLength) name = name.Substring(0, MaxNameLength);
            name = name.TrimEnd('.', ' ');
            if (name.Length == 0) name = DefaultName;
        }
        return name;
    }

    /// <summary>O nome se estiver livre; senão "nome (2).ext", "nome (3).ext"... (<paramref name="exists"/> diz se o nome já existe).</summary>
    public static string Unique(string name, Func<string, bool> exists)
    {
        if (!exists(name)) return name;
        var ext = Path.GetExtension(name);
        var stem = name.Substring(0, name.Length - ext.Length);
        for (var n = 2; n < 10000; n++)
        {
            var candidate = $"{stem} ({n}){ext}";
            if (!exists(candidate)) return candidate;
        }
        return $"{stem} ({Guid.NewGuid():N}){ext}";
    }

    /// <summary>Pasta dos recebidos: <c>Downloads\Do celular</c>; com <c>--teste-pc</c> (D28) <c>%TEMP%\mixer-check\recebidos</c>.</summary>
    public static string ReceivedFolder(string downloads, string tempPath, bool testMode) =>
        testMode ? Path.Combine(tempPath, "mixer-check", "recebidos") : Path.Combine(downloads, "Do celular");

    /// <summary>O valor de <c>nome</c> na query (<c>nome=a%20b&amp;x=1</c>), já decodificado; nulo se não houver.</summary>
    public static string? NameFromQuery(string? query)
    {
        if (string.IsNullOrEmpty(query)) return null;
        foreach (var part in query.Split('&'))
        {
            var eq = part.IndexOf('=');
            if (eq <= 0 || part.Substring(0, eq) != "nome") continue;
            try { return Uri.UnescapeDataString(part.Substring(eq + 1).Replace('+', ' ')); }
            catch (UriFormatException) { return null; }
        }
        return null;
    }
}
