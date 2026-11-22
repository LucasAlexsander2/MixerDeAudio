using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using MixerDeAudio.Interop;
using MixerDeAudio.Services;
using Velopack;
using Windows.ApplicationModel.Activation;

namespace MixerDeAudio;

/// <summary>
/// Ponto de entrada próprio (D6): uma cópia só do app por vez. A segunda cópia entrega a ativação (com a linha de
/// comando) à primeira e sai sem criar nada. O <c>Main</c> gerado pelo XAML fica desligado (<c>DISABLE_XAML_GENERATED_MAIN</c>).
/// </summary>
public static class Program
{
    private const string InstanceKey = "MixerDeAudio";

    /// <summary>Argumentos desta cópia (sem o caminho do .exe).</summary>
    public static string[] StartupArguments { get; private set; } = [];

    [STAThread]
    private static int Main(string[] args)
    {
        // Fase 11, D3: os hooks do Velopack vêm antes de tudo (antes do AppInstance e sem interface). Em publicado e bin
        // (cópia não instalada) isto não faz nada; só roda de verdade na cópia instalada, chamada pelo próprio Velopack.
        VelopackApp.Build()
            .OnFirstRun(_ => { InnoMigration.Run(); StartupRegistration.RepointToCurrent(); })
            .OnAfterUpdateFastCallback(_ => StartupRegistration.RepointToCurrent())
            .OnBeforeUninstallFastCallback(_ => StartupRegistration.RemoveIfCurrent())
            .Run();

        WinRT.ComWrappersSupport.InitializeComWrappers();
        StartupArguments = args;

        var instance = AppInstance.FindOrRegisterForKey(InstanceKey);
        if (!instance.IsCurrent)
        {
            RedirectToFirst(instance);
            return 0;
        }

        instance.Activated += OnActivated;
        Log.RotateIfNeeded();

        Application.Start(_ =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            new App();
        });
        return 0;
    }

    /// <summary>A primeira cópia recebe a ativação da segunda (chega numa thread de fundo).</summary>
    private static void OnActivated(object? sender, AppActivationArguments args)
    {
        var line = args.Kind == ExtendedActivationKind.Launch && args.Data is ILaunchActivatedEventArgs launch ? launch.Arguments : null;
        var command = CommandLine.ParseLine(line);
        App.UiQueue?.TryEnqueue(() => App.Instance?.Execute(command));
    }

    private static void RedirectToFirst(AppInstance first)
    {
        // Dá à primeira cópia o direito de se trazer para a frente.
        NativeMethods.AllowSetForegroundWindow((int)first.ProcessId);

        var activation = AppInstance.GetCurrent().GetActivatedEventArgs();
        using var done = new ManualResetEvent(false);
        Task.Run(async () =>
        {
            try
            {
                await first.RedirectActivationToAsync(activation);
            }
            catch (Exception ex)
            {
                Log.Write($"erro ao entregar a ativacao a primeira instancia: {ex.Message}");
            }
            finally
            {
                done.Set();
            }
        });

        // A thread principal é STA: esta espera continua tratando as mensagens do COM.
        done.WaitOne(TimeSpan.FromSeconds(10));
    }
}
