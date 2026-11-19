using System;
using System.Collections.Generic;
using System.Text;

namespace MixerDeAudio.Services;

/// <summary>Comando pedido pela linha de comando (Fase 1, D8).</summary>
public enum AppCommand
{
    /// <summary>Sem argumento: abrir (como o clique no ícone); na primeira cópia, só cria o ícone.</summary>
    Default,
    /// <summary><c>--abrir</c></summary>
    Open,
    /// <summary><c>--abrir-sem-foco</c> (uso nos roteiros de teste)</summary>
    OpenNoFocus,
    /// <summary><c>--fechar</c></summary>
    Close,
    /// <summary><c>--sair</c></summary>
    Exit,
    /// <summary>Argumento desconhecido: não faz nada.</summary>
    None,
}

/// <summary>Resultado da leitura da linha de comando.</summary>
public readonly record struct CommandLineResult(AppCommand Command, bool NoTrim);

/// <summary>Lê a linha de comando do app. Lógica pura, sem WinUI.</summary>
public static class CommandLine
{
    /// <summary>Lê uma lista de argumentos (o <c>args</c> do <c>Main</c>).</summary>
    public static CommandLineResult Parse(IEnumerable<string> args)
    {
        var command = AppCommand.Default;
        var noTrim = false;
        var unknown = false;
        var found = false;

        foreach (var raw in args)
        {
            var arg = raw.Trim().Trim('"');
            if (arg.Length == 0) continue;
            // O caminho do .exe pode vir antes dos argumentos (com ou sem aspas)
            if (arg.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;

            switch (arg.ToLowerInvariant())
            {
                case "--abrir": command = AppCommand.Open; found = true; break;
                case "--abrir-sem-foco": command = AppCommand.OpenNoFocus; found = true; break;
                case "--fechar": command = AppCommand.Close; found = true; break;
                case "--sair": command = AppCommand.Exit; found = true; break;
                case "--sem-aparar": noTrim = true; break;
                default: unknown = true; break;
            }
        }

        if (!found && unknown) command = AppCommand.None;
        return new CommandLineResult(command, noTrim);
    }

    /// <summary>Lê uma linha inteira (como vem na ativação redirecionada), respeitando aspas.</summary>
    public static CommandLineResult ParseLine(string? line) => Parse(Split(line));

    private static List<string> Split(string? line)
    {
        var tokens = new List<string>();
        if (string.IsNullOrWhiteSpace(line)) return tokens;
        var current = new StringBuilder();
        var inQuotes = false;
        foreach (var c in line)
        {
            if (c == '"') { inQuotes = !inQuotes; continue; }
            if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (current.Length > 0) { tokens.Add(current.ToString()); current.Clear(); }
                continue;
            }
            current.Append(c);
        }
        if (current.Length > 0) tokens.Add(current.ToString());
        return tokens;
    }
}
