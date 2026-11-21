using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MixerDeAudio.Audio;

/// <summary>Uma mídia a ligar: o AUMID, se está tocando e o .exe que a pasta de apps resolveu (nulo se não resolveu).</summary>
public sealed record MediaCandidate(string Aumid, bool IsPlaying, string? ShellExePath);

/// <summary>Uma linha de som: a chave, o .exe e os AUMIDs dos processos dela (apps da Loja; vazio nos comuns).</summary>
public sealed record GroupCandidate(string Key, string? ExePath, IReadOnlyList<string> ProcessAumids);

/// <summary>
/// Liga cada mídia a uma linha de som (Fase 7, D3). Regras, nesta ordem: (1) AUMID igual ao de um processo do grupo;
/// (2) .exe da pasta de apps igual ao do grupo; (3) mesmo nome de arquivo do .exe; (4) AUMID igual ao nome do .exe do grupo.
/// Uma mídia liga a uma linha só e uma linha recebe uma mídia só; em empate, fica a que está tocando. Lógica pura.
/// </summary>
public static class MediaMatcher
{
    /// <summary>Número da regra (1 a 4) em que a mídia liga ao grupo, ou 0 se não liga.</summary>
    public static int RuleOf(MediaCandidate media, GroupCandidate group)
    {
        if (group.ProcessAumids.Any(a => string.Equals(a, media.Aumid, StringComparison.OrdinalIgnoreCase))) return 1;
        var shell = media.ShellExePath;
        var exe = group.ExePath;
        if (!string.IsNullOrWhiteSpace(shell) && !string.IsNullOrWhiteSpace(exe))
        {
            if (string.Equals(shell, exe, StringComparison.OrdinalIgnoreCase)) return 2;
            if (string.Equals(FileNameOf(shell), FileNameOf(exe), StringComparison.OrdinalIgnoreCase)) return 3;
        }
        if (!string.IsNullOrWhiteSpace(exe))
        {
            var file = FileNameOf(exe);
            var bare = Path.GetFileNameWithoutExtension(file);
            if (string.Equals(media.Aumid, file, StringComparison.OrdinalIgnoreCase)
                || string.Equals(media.Aumid, bare, StringComparison.OrdinalIgnoreCase)) return 4;
        }
        return 0;
    }

    /// <summary>AUMID da mídia para a chave da linha ligada; só entram as mídias que acharam linha.</summary>
    public static Dictionary<string, string> Match(IReadOnlyList<MediaCandidate> media, IReadOnlyList<GroupCandidate> groups)
    {
        var pairs = new List<(int Rule, bool Playing, int MediaIndex, int GroupIndex)>();
        for (var m = 0; m < media.Count; m++)
            for (var g = 0; g < groups.Count; g++)
            {
                var rule = RuleOf(media[m], groups[g]);
                if (rule > 0) pairs.Add((rule, media[m].IsPlaying, m, g));
            }

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var usedGroups = new HashSet<int>();
        foreach (var p in pairs.OrderBy(p => p.Rule).ThenBy(p => p.Playing ? 0 : 1).ThenBy(p => p.MediaIndex).ThenBy(p => p.GroupIndex))
        {
            if (usedGroups.Contains(p.GroupIndex) || result.ContainsKey(media[p.MediaIndex].Aumid)) continue;
            result[media[p.MediaIndex].Aumid] = groups[p.GroupIndex].Key;
            usedGroups.Add(p.GroupIndex);
        }
        return result;
    }

    /// <summary>Nome para a linha só de mídia: o da pasta de apps, ou o do .exe sem extensão (AUMID "x.exe" vira "x").</summary>
    public static string NameFor(string aumid, string? shellName)
    {
        if (!string.IsNullOrWhiteSpace(shellName)) return shellName.Trim();
        if (aumid.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return aumid[..^4];
        var bang = aumid.IndexOf('!');
        if (bang > 0) aumid = aumid[..bang];
        var us = aumid.IndexOf('_');
        return us > 0 ? aumid[..us] : aumid;
    }

    private static string FileNameOf(string path) => path[(path.LastIndexOfAny(BarraChars) + 1)..];

    private static readonly char[] BarraChars = { (char)92, '/' };
}
