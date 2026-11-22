using System;
using System.Buffers;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using MixerDeAudio.Services;

namespace MixerDeAudio.Pc;

/// <summary>
/// Recebe um arquivo do celular direto no disco (Fase 9d, D26): em blocos de 64 KB para <c>nome.parcial</c>, com prazo de 30 s
/// <b>sem dados</b>, um envio por vez. No fim renomeia e grava o <c>Zone.Identifier</c> (ZoneId=3). Falha ou conexão caída
/// apaga o <c>.parcial</c>. O conteúdo e o nome nunca vão para o log (só o tamanho). Criado no primeiro uso.
/// </summary>
internal sealed partial class FileReceiver
{
    public static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(30);
    private const int BlockSize = 64 * 1024;

    /// <summary>Status HTTP a responder (0 = só fechar, a conexão caiu), nome gravado e bytes recebidos.</summary>
    public readonly record struct Result(int Status, string? Name, long Bytes);

    private readonly bool _testMode;
    private int _busy;

    public FileReceiver(bool testMode) => _testMode = testMode;

    public long MaxBytes => FileNameRules.Limit(_testMode);

    /// <summary>Pasta dos recebidos (D26, D28).</summary>
    public string Folder => FileNameRules.ReceivedFolder(_testMode ? "" : DownloadsFolder(), Path.GetTempPath(), _testMode);

    /// <summary>
    /// Lê <paramref name="length"/> bytes: primeiro <paramref name="prefetched"/> (o que já veio junto do cabeçalho) e o resto de
    /// <paramref name="source"/>. Devolve 409 (já há um envio), 413 (passa do limite) ou 507 (sem espaço) sem ler o corpo.
    /// </summary>
    public async Task<Result> ReceiveAsync(Stream source, ReadOnlyMemory<byte> prefetched, long length, string? rawName, CancellationToken serverCt)
    {
        if (length > MaxBytes) return new Result(413, null, 0);
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0) return new Result(409, null, 0);
        string? partial = null;
        try
        {
            var folder = Folder;
            Directory.CreateDirectory(folder);
            if (!DiskSpace.TryGet(Path.GetPathRoot(Path.GetFullPath(folder)), out var free, out _)) free = long.MaxValue; // sem resposta: não barra
            if (!FileNameRules.HasRoom(free, length)) return new Result(507, null, 0);

            var name = FileNameRules.Unique(FileNameRules.Clean(rawName), n => File.Exists(Path.Combine(folder, n)));
            partial = Path.Combine(folder, name + FileNameRules.PartialSuffix);
            var buffer = ArrayPool<byte>.Shared.Rent(BlockSize);
            try
            {
                using var idle = CancellationTokenSource.CreateLinkedTokenSource(serverCt);
                long received = 0;
                using (var file = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, BlockSize, FileOptions.Asynchronous))
                {
                    var have = (int)Math.Min(prefetched.Length, length);
                    if (have > 0)
                    {
                        await file.WriteAsync(prefetched.Slice(0, have), serverCt).ConfigureAwait(false);
                        received = have;
                    }
                    while (received < length)
                    {
                        idle.CancelAfter(IdleTimeout); // 30 s sem receber nada
                        var want = (int)Math.Min(BlockSize, length - received);
                        var n = await source.ReadAsync(buffer.AsMemory(0, want), idle.Token).ConfigureAwait(false);
                        if (n == 0) throw new IOException("conexao caiu"); // o celular desistiu
                        await file.WriteAsync(buffer.AsMemory(0, n), serverCt).ConfigureAwait(false);
                        received += n;
                    }
                }

                // Outro arquivo pode ter ganhado o nome durante o envio: confere de novo
                name = FileNameRules.Unique(name, n => File.Exists(Path.Combine(folder, n)));
                var final = Path.Combine(folder, name);
                File.Move(partial, final);
                partial = null;
                try { File.WriteAllText(final + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3\r\n"); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException) { Log.Write("ERRO celular: arquivo: Zone.Identifier nao gravado"); }
                return new Result(200, name, received);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or UnauthorizedAccessException or ObjectDisposedException)
        {
            // Só o tipo e o código vão para o log: a mensagem do Windows pode trazer o caminho e o nome do arquivo (D12)
            Log.Write($"celular: arquivo interrompido ({ex.GetType().Name}, 0x{ex.HResult:X8})");
            return new Result(ex is OperationCanceledException or ObjectDisposedException || ex.Message == "conexao caiu" ? 0 : 500, null, 0);
        }
        finally
        {
            if (partial is not null)
            {
                try { File.Delete(partial); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
            Volatile.Write(ref _busy, 0);
        }
    }

    // ---- Pasta Downloads (D26) ----

    private static readonly Guid DownloadsId = new("374DE290-123F-4565-9164-39C4925E467B");

    [LibraryImport("shell32.dll")]
    private static partial int SHGetKnownFolderPath(in Guid id, uint flags, IntPtr token, out IntPtr path);

    [LibraryImport("ole32.dll")]
    private static partial void CoTaskMemFree(IntPtr pointer);

    private static string DownloadsFolder()
    {
        if (SHGetKnownFolderPath(DownloadsId, 0, IntPtr.Zero, out var p) == 0 && p != IntPtr.Zero)
        {
            try { return Marshal.PtrToStringUni(p) ?? ""; }
            finally { CoTaskMemFree(p); }
        }
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    }
}
