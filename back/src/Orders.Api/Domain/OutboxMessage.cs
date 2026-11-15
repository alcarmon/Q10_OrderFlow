using System.Text.Json;

namespace Orders.Api.Domain;

public class OutboxMessage
{
    private OutboxMessage() { }

    public Guid EventId { get; private set; }
    public string Tipo { get; private set; } = default!;
    public string Payload { get; private set; } = default!;
    public DateTime CreadoEn { get; private set; }
    public DateTime? PublicadoEn { get; private set; }

    public static OutboxMessage FromEvent<T>(Guid eventId, T message, DateTime creadoEnUtc) where T : class =>
        new()
        {
            EventId = eventId,
            Tipo = typeof(T).Name,
            Payload = JsonSerializer.Serialize(message),
            CreadoEn = creadoEnUtc
        };

    public void MarkPublished(DateTime publicadoEnUtc) => PublicadoEn = publicadoEnUtc;
}