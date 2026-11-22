using System;
using System.Runtime.InteropServices;

namespace MixerDeAudio.Pc;

public static partial class DisplayTopology
{
    private const uint QDC_ALL_PATHS = 0x1;
    private const uint QDC_DATABASE_CURRENT = 0x4;
    private const uint SDC_APPLY = 0x80;

    [LibraryImport("user32.dll")]
    private static partial int GetDisplayConfigBufferSizes(uint flags, out uint numPaths, out uint numModes);

    [LibraryImport("user32.dll")]
    private static unsafe partial int QueryDisplayConfig(uint flags, ref uint numPaths, byte* paths, ref uint numModes, byte* modes, uint* topology);

    [LibraryImport("user32.dll")]
    private static unsafe partial int SetDisplayConfig(uint numPaths, byte* paths, uint numModes, byte* modes, uint flags);

    /// <summary>Leitura pura do PC (vale também no modo de teste): modo atual e telas ligadas. Thread da tela.</summary>
    public static unsafe ProjectionInfo Read()
    {
        ProjectionMode? mode = null;
        var screens = 0;
        try
        {
            // Modo atual: o banco de dados de configuração devolve a topologia (interna, clone, estendida ou externa)
            if (GetDisplayConfigBufferSizes(QDC_DATABASE_CURRENT, out var np, out var nm) == 0 && np > 0)
            {
                var paths = new byte[np * PathInfoSize];
                var modes = new byte[Math.Max(nm, 1u) * 64];
                uint topology = 0;
                fixed (byte* p = paths)
                fixed (byte* m = modes)
                {
                    if (QueryDisplayConfig(QDC_DATABASE_CURRENT, ref np, p, ref nm, m, &topology) == 0)
                        mode = FromFlag(topology);
                }
            }
            // Telas ligadas: alvos disponíveis entre todos os caminhos possíveis
            if (GetDisplayConfigBufferSizes(QDC_ALL_PATHS, out var ap, out var am) == 0 && ap > 0)
            {
                var paths = new byte[ap * PathInfoSize];
                var modes = new byte[Math.Max(am, 1u) * 64];
                fixed (byte* p = paths)
                fixed (byte* m = modes)
                {
                    if (QueryDisplayConfig(QDC_ALL_PATHS, ref ap, p, ref am, m, null) == 0)
                        screens = CountScreens(ParsePaths(paths, (int)ap));
                }
            }
        }
        catch (Exception)
        {
            // sem leitura: o celular mostra "modo desconhecido"
        }
        return new ProjectionInfo(mode, screens);
    }

    /// <summary>Troca a projeção como o Win+P. Devolve falso se o Windows recusou.</summary>
    public static unsafe bool Apply(ProjectionMode mode) =>
        SetDisplayConfig(0, null, 0, null, SDC_APPLY | ToFlag(mode)) == 0;
}
