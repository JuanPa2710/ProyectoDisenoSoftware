using System.Collections.Concurrent;
using System.Threading;

namespace SIVCR.Api.Services;

public sealed class CargaEnCursoException : InvalidOperationException
{
    public CargaEnCursoException()
        : base("Esta carga ya se está procesando o importando. Esperá a que termine.")
    {
    }
}

internal sealed class BloqueoCarga : IDisposable
{
    private static readonly ConcurrentDictionary<Guid, byte> Activas = new();

    private readonly Guid _cargaId;
    private int _liberado;

    private BloqueoCarga(Guid cargaId)
    {
        _cargaId = cargaId;
    }

    public static BloqueoCarga Adquirir(Guid cargaId)
    {
        if (!Activas.TryAdd(cargaId, 0))
        {
            throw new CargaEnCursoException();
        }

        return new BloqueoCarga(cargaId);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _liberado, 1) == 0)
        {
            Activas.TryRemove(_cargaId, out _);
        }
    }
}
