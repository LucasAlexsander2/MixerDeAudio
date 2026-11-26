using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Threading;
using MixerDeAudio.Pc;
using MixerDeAudio.Services;

namespace MixerDeAudio.Remote;

/// <summary>
/// Fase 16, D5: os IPs do PC por onde o celular pode chegar (interfaces Ethernet e Wi-Fi, redes Privadas ou de domínio,
/// sem adaptador virtual nem VPN). Relê no <c>NetworkAddressChanged</c> e, no máximo a cada 60 s, quando alguém consulta
/// (a categoria da rede pode mudar sem mudar o endereço). Se a leitura da categoria falhar, vale como Privada e o
/// log registra (uma vez por falha nova). A decisão está em <see cref="NetworkRules"/>.
/// </summary>
internal sealed class AllowedNetworks : IDisposable
{
    private static readonly TimeSpan MaxAge = TimeSpan.FromSeconds(60);

    private readonly object _gate = new();
    private IReadOnlyList<IPAddress> _addresses = Array.Empty<IPAddress>();
    private long _readAt; // 0 = nunca leu
    private int _reading;
    private string? _lastError;
    private bool _disposed;

    /// <summary>A lista mudou (thread qualquer).</summary>
    public event Action? Changed;

    public AllowedNetworks()
    {
        Refresh();
        NetworkChange.NetworkAddressChanged += OnAddressChanged;
    }

    /// <summary>Os IPs permitidos agora (a leitura é refeita se tiver mais de 60 s).</summary>
    public IReadOnlyList<IPAddress> Addresses
    {
        get
        {
            var at = Volatile.Read(ref _readAt);
            if (at == 0 || Environment.TickCount64 - at > MaxAge.TotalMilliseconds) Refresh();
            lock (_gate) return _addresses;
        }
    }

    /// <summary>Sem nenhuma interface permitida (rede Pública ou sem rede).</summary>
    public bool None => Addresses.Count == 0;

    private void OnAddressChanged(object? sender, EventArgs e) => Refresh();

    /// <summary>Lê as interfaces e as categorias de novo. Se já há uma leitura em andamento, usa o resultado anterior.</summary>
    public void Refresh()
    {
        if (_disposed || Interlocked.CompareExchange(ref _reading, 1, 0) != 0) return;
        try
        {
            var nics = new List<NicInfo>();
            IReadOnlyDictionary<Guid, NetworkCategory>? categories = NetworkCategories.Read(out var error);
            if (categories is null)
            {
                if (error != _lastError) Log.Write($"celular: categoria de rede indisponivel ({error}); tratando como Privada");
                _lastError = error;
            }
            else _lastError = null;

            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                try
                {
                    var props = nic.GetIPProperties();
                    var gateway = props.GatewayAddresses.Any(g => g.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any));
                    NetworkCategory? category = null;
                    if (categories is not null && Guid.TryParse(nic.Id, out var id) && categories.TryGetValue(id, out var c)) category = c;
                    var addresses = props.UnicastAddresses.Select(u => u.Address).ToList();
                    nics.Add(new NicInfo(nic.Description, nic.NetworkInterfaceType, nic.OperationalStatus == OperationalStatus.Up, gateway, category, addresses));
                }
                catch (Exception ex) when (ex is NetworkInformationException or InvalidOperationException) { }
            }

            var list = NetworkRules.AllowedAddresses(nics);
            bool changed;
            lock (_gate)
            {
                changed = !_addresses.SequenceEqual(list);
                _addresses = list;
            }
            Volatile.Write(ref _readAt, Environment.TickCount64);
            if (changed)
            {
                Log.Write(list.Count == 0
                    ? "celular: nenhuma rede permitida (publica ou sem rede)"
                    : $"celular: redes permitidas: {string.Join(", ", list)}");
                Changed?.Invoke();
            }
        }
        catch (Exception ex)
        {
            Log.Write($"ERRO celular: redes: {ex.Message}");
        }
        finally
        {
            Volatile.Write(ref _reading, 0);
        }
    }

    public void Dispose()
    {
        _disposed = true;
        NetworkChange.NetworkAddressChanged -= OnAddressChanged;
    }
}
