#:property TargetFramework=net10.0
#:property PublishAot=false
#:property NoWarn=CA1416;CS0618;SYSLIB1054;SYSLIB1096

// Janela de teste (Fase 9c, D21, D22 e D30): uma janela Win32 de verdade, com o titulo "MX-teste janela", que serve de alvo
// para os roteiros de "Janelas" do controle do PC pelo celular. Nunca aparece nem rouba o foco:
//   - WS_EX_TOOLWINDOW (fora da barra de tarefas e do Alt+Tab) e WS_EX_NOACTIVATE;
//   - posicao -10000,-10000 (fora de todas as telas), mostrada com SW_SHOWNOACTIVATE;
//   - sai sozinha em no maximo 10 minutos (padrao 600 s) se ninguem a fechar.
// Escreve em um arquivo de log o que recebe (uma linha por evento, so o nome do evento):
//   PRONTO <hwnd>              a janela existe e esta visivel
//   WM_SYSCOMMAND minimizar    pediram para minimizar (a janela NAO minimiza de verdade: nao aparece nada na tela)
//   WM_SIZE minimizado         (so se a janela chegasse a minimizar)
//   WM_ACTIVATE ativada        a janela foi ativada (nao deve acontecer: o roteiro confere que ninguem a trouxe para a frente)
//   WM_CLOSE                   pediram para fechar; a janela se destroi e o programa sai
//   SAIU <motivo>              fim do programa (fechada ou tempo)
//   janela-teste.exe --log <arquivo> [--segundos <1 a 600>]
using System.Runtime.InteropServices;

string? log = null;
var segundos = 600;
for (var i = 0; i < args.Length - 1; i++)
{
    if (args[i] == "--log") log = args[i + 1];
    if (args[i] == "--segundos" && int.TryParse(args[i + 1], out var s)) segundos = Math.Clamp(s, 1, 600);
}
if (log is null) { Console.Error.WriteLine("uso: janela-teste.exe --log <arquivo> [--segundos N]"); return 2; }
try { File.Delete(log); } catch { }
Registro.Arquivo = log;

var proc = new Janela.WndProc(Janela.Proc);
var hInstance = Janela.GetModuleHandle(null);
var cls = new Janela.WNDCLASSEX
{
    cbSize = (uint)Marshal.SizeOf<Janela.WNDCLASSEX>(),
    lpfnWndProc = Marshal.GetFunctionPointerForDelegate(proc),
    hInstance = hInstance,
    lpszClassName = "MXTesteJanelaClasse",
};
if (Janela.RegisterClassEx(ref cls) == 0) { Console.Error.WriteLine("RegisterClassEx falhou"); return 3; }
const uint WS_OVERLAPPEDWINDOW = 0x00CF0000;
const uint WS_EX_TOOLWINDOW = 0x00000080;
const uint WS_EX_NOACTIVATE = 0x08000000;
var hwnd = Janela.CreateWindowEx(WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE, cls.lpszClassName, "MX-teste janela", WS_OVERLAPPEDWINDOW,
    -10000, -10000, 300, 200, IntPtr.Zero, IntPtr.Zero, hInstance, IntPtr.Zero);
if (hwnd == IntPtr.Zero) { Console.Error.WriteLine("CreateWindowEx falhou"); return 3; }
Janela.ShowWindow(hwnd, 4); // SW_SHOWNOACTIVATE
Janela.SetTimer(hwnd, 1, (uint)(segundos * 1000), IntPtr.Zero);
Registro.Escrever($"PRONTO {hwnd.ToInt64()}");
Console.WriteLine($"PRONTO {hwnd.ToInt64()}");
Console.Out.Flush();
while (Janela.GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
{
    Janela.TranslateMessage(ref msg);
    Janela.DispatchMessage(ref msg);
}
return 0;

static class Registro
{
    public static string Arquivo = "";
    public static void Escrever(string linha)
    {
        try { File.AppendAllText(Arquivo, linha + Environment.NewLine); } catch { }
    }
}

static class Janela
{
    public delegate IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    const uint WM_DESTROY = 0x0002, WM_SIZE = 0x0005, WM_ACTIVATE = 0x0006, WM_CLOSE = 0x0010, WM_SYSCOMMAND = 0x0112, WM_TIMER = 0x0113;
    const long SC_MINIMIZE = 0xF020;

    public static IntPtr Proc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case WM_SYSCOMMAND when (wParam.ToInt64() & 0xFFF0) == SC_MINIMIZE:
                Registro.Escrever("WM_SYSCOMMAND minimizar");
                return IntPtr.Zero; // nao minimiza: uma janela minimizada apareceria como barrinha no canto da tela
            case WM_SIZE when wParam.ToInt64() == 1:
                Registro.Escrever("WM_SIZE minimizado");
                break;
            case WM_ACTIVATE when (wParam.ToInt64() & 0xFFFF) != 0:
                Registro.Escrever("WM_ACTIVATE ativada");
                break;
            case WM_CLOSE:
                Registro.Escrever("WM_CLOSE");
                DestroyWindow(hwnd);
                return IntPtr.Zero;
            case WM_TIMER:
                Registro.Escrever("SAIU tempo");
                DestroyWindow(hwnd);
                return IntPtr.Zero;
            case WM_DESTROY:
                Registro.Escrever("SAIU fechada");
                PostQuitMessage(0);
                return IntPtr.Zero;
        }
        return DefWindowProc(hwnd, msg, wParam, lParam);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct WNDCLASSEX
    {
        public uint cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int x;
        public int y;
    }

    [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", ExactSpelling = true, CharSet = CharSet.Unicode)] public static extern IntPtr GetModuleHandle(string? name);
    [DllImport("user32.dll", EntryPoint = "RegisterClassExW", ExactSpelling = true, CharSet = CharSet.Unicode)] public static extern ushort RegisterClassEx(ref WNDCLASSEX cls);
    [DllImport("user32.dll", EntryPoint = "CreateWindowExW", ExactSpelling = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr CreateWindowEx(uint exStyle, string cls, string title, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hwnd, int cmd);
    [DllImport("user32.dll")] public static extern IntPtr SetTimer(IntPtr hwnd, nuint id, uint ms, IntPtr proc);
    [DllImport("user32.dll", EntryPoint = "GetMessageW", ExactSpelling = true)] public static extern int GetMessage(out MSG msg, IntPtr hwnd, uint min, uint max);
    [DllImport("user32.dll")] public static extern bool TranslateMessage(ref MSG msg);
    [DllImport("user32.dll", EntryPoint = "DispatchMessageW", ExactSpelling = true)] public static extern IntPtr DispatchMessage(ref MSG msg);
    [DllImport("user32.dll", EntryPoint = "DefWindowProcW", ExactSpelling = true)] public static extern IntPtr DefWindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern void PostQuitMessage(int code);
}
