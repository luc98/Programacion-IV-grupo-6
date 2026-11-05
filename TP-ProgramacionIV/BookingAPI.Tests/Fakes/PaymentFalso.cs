using System.Collections.Concurrent;
using System.Diagnostics;
using Grpc.Core;
using Grupo6.Payment.Contracts.V1;

namespace BookingAPI.Tests.Fakes;

// Reemplaza la red: el cliente gRPC generado llama a este CallInvoker en lugar de mandar la
// llamada por HTTP/2. Cada llamada consume el próximo paso del guion; sin guion, aprueba.
public sealed class PaymentFalso : CallInvoker
{
    private readonly ConcurrentQueue<Func<CancellationToken, Task<ProcesarPagoResponse>>> _guion = new();
    private int _llamadas;

    public int Llamadas => _llamadas;

    public ConcurrentQueue<(ProcesarPagoRequest Pedido, Metadata Cabeceras)> Recibidas { get; } = new();

    public PaymentService.PaymentServiceClient Cliente => new(this);

    public PaymentFalso Luego(Func<CancellationToken, Task<ProcesarPagoResponse>> paso, int veces = 1)
    {
        for (var i = 0; i < veces; i++)
        {
            _guion.Enqueue(paso);
        }
        return this;
    }

    public static Func<CancellationToken, Task<ProcesarPagoResponse>> Aprueba() =>
        _ => Task.FromResult(new ProcesarPagoResponse { Resultado = ResultadoPago.Aprobado, TransaccionId = Guid.NewGuid().ToString() });

    public static Func<CancellationToken, Task<ProcesarPagoResponse>> RechazaPorFondos() =>
        _ => Task.FromResult(new ProcesarPagoResponse
        {
            Resultado = ResultadoPago.RechazadoFondosInsuficientes,
            TransaccionId = Guid.NewGuid().ToString(),
            MensajeError = "sin fondos"
        });

    public static Func<CancellationToken, Task<ProcesarPagoResponse>> Responde(ProcesarPagoResponse respuesta) =>
        _ => Task.FromResult(respuesta);

    public static Func<CancellationToken, Task<ProcesarPagoResponse>> Falla(StatusCode status) =>
        _ => Task.FromException<ProcesarPagoResponse>(new RpcException(new Status(status, "simulado")));

    // No responde nunca: solo termina cuando se cancela la llamada, igual que el cliente real
    // con ThrowOperationCanceledOnCancellation = true
    public static Func<CancellationToken, Task<ProcesarPagoResponse>> Cuelga() =>
        async ct =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            throw new UnreachableException();
        };

    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request)
    {
        Interlocked.Increment(ref _llamadas);
        Recibidas.Enqueue(((ProcesarPagoRequest)(object)request, options.Headers ?? new Metadata()));

        var paso = _guion.TryDequeue(out var siguiente) ? siguiente : Aprueba();
        var respuesta = (Task<TResponse>)(object)paso(options.CancellationToken);
        return new AsyncUnaryCall<TResponse>(
            respuesta, Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () => { });
    }

    public override TResponse BlockingUnaryCall<TRequest, TResponse>(
        Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request) =>
        throw new NotSupportedException();

    public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(
        Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request) =>
        throw new NotSupportedException();

    public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(
        Method<TRequest, TResponse> method, string? host, CallOptions options) =>
        throw new NotSupportedException();

    public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(
        Method<TRequest, TResponse> method, string? host, CallOptions options) =>
        throw new NotSupportedException();
}
