using System;
using System.Collections.Generic;
using System.IO;

namespace MixerDeAudio.Pc;

/// <summary>Atalhos favoritos (Fase 9c, D23): regras puras sobre a lista de nomes de arquivo da pasta.</summary>
public static class ShortcutRules
{
    public const int MaxShortcuts = 30;

    /// <summary>Só <c>.lnk</c> (programa) e <c>.url</c> (site).</summary>
    public static bool IsShortcutFile(string? fileName)
    {
        if (string.IsNullOrEmpty(fileName)) return false;
        var ext = Path.GetExtension(fileName);
        return string.Equals(ext, ".lnk", StringComparison.OrdinalIgnoreCase) || string.Equals(ext, ".url", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Nome mostrado: o do arquivo sem a extensão.</summary>
    public static string DisplayName(string fileName) => Path.GetFileNameWithoutExtension(fileName);

    /// <summary>
    /// Os arquivos da pasta que são atalhos, por nome (ordem alfabética sem diferenciar maiúsculas), sem repetir o nome mostrado
    /// (<c>X.lnk</c> e <c>X.url</c>: fica o <c>.lnk</c>) e até <see cref="MaxShortcuts"/>. Devolve os nomes de arquivo.
    /// </summary>
    public static List<string> Select(IEnumerable<string> fileNames)
    {
        var files = new List<string>();
        foreach (var f in fileNames)
        {
            var name = Path.GetFileName(f);
            if (IsShortcutFile(name) && !string.IsNullOrWhiteSpace(DisplayName(name))) files.Add(name);
        }
        files.Sort((a, b) =>
        {
            var c = string.Compare(DisplayName(a), DisplayName(b), StringComparison.OrdinalIgnoreCase);
            if (c != 0) return c;
            c = string.Compare(Path.GetExtension(a), Path.GetExtension(b), StringComparison.OrdinalIgnoreCase);
            return c != 0 ? c : string.CompareOrdinal(a, b);
        });
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in files)
        {
            if (!seen.Add(DisplayName(f))) continue;
            result.Add(f);
            if (result.Count >= MaxShortcuts) break;
        }
        return result;
    }

    /// <summary>Os nomes mostrados no celular.</summary>
    public static List<string> DisplayNames(IEnumerable<string> shortcutFiles)
    {
        var names = new List<string>();
        foreach (var f in shortcutFiles) names.Add(DisplayName(f));
        return names;
    }

    /// <summary>
    /// O arquivo da lista que tem exatamente esse nome mostrado, ou nulo (nome fora da lista, com caminho, vazio...). O caminho é sempre
    /// montado no PC a partir do nome de arquivo da lista, nunca do que o celular mandou.
    /// </summary>
    public static string? Resolve(IReadOnlyList<string> shortcutFiles, string? requestedName)
    {
        if (string.IsNullOrEmpty(requestedName)) return null;
        foreach (var f in shortcutFiles)
            if (string.Equals(DisplayName(f), requestedName, StringComparison.Ordinal)) return f;
        return null;
    }
}

/// <summary>A pasta de atalhos no disco (Fase 9c, D23 e D28).</summary>
public static class ShortcutFolder
{
    /// <summary>Normal: <c>%LOCALAPPDATA%\MixerDeAudio\Atalhos</c>. Com <c>--teste-pc</c> (D28): <c>%TEMP%\mixer-check\atalhos</c>.</summary>
    public static string PathFor(string dataFolder, bool testMode) =>
        testMode ? Path.Combine(Path.GetTempPath(), "mixer-check", "atalhos") : Path.Combine(dataFolder, "Atalhos");

    /// <summary>Cria a pasta (ao ligar o grupo). Falha silenciosa: sem pasta, a lista é vazia.</summary>
    public static void Ensure(string folder)
    {
        try { Directory.CreateDirectory(folder); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Os arquivos de atalho da pasta (ver <see cref="ShortcutRules.Select"/>); lista vazia se a pasta não existir.</summary>
    public static List<string> Files(string folder)
    {
        try
        {
            if (!Directory.Exists(folder)) return new List<string>();
            var names = new List<string>();
            foreach (var f in Directory.EnumerateFiles(folder)) names.Add(f);
            return ShortcutRules.Select(names);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new List<string>();
        }
    }
}
