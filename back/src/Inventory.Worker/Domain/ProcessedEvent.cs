namespace Inventory.Worker.Domain;

public enum ReservationOutcome { Reserved, Rejected }

/// <summary>Registro de eventos ya procesados (bandeja de entrada). Guarda el resultado para poder republicarlo.</summary>
public class ProcessedEvent
{
    private ProcessedEvent() { } 

    public Guid EventId { get; private set; }
    public ReservationOutcome Resultado { get; private set; }
    public string? Motivo { get; private set; }
    public DateTime ProcesadoEn { get; private set; }

    public static ProcessedEvent Create(Guid eventId, ReservationOutcome resultado, string? motivo, DateTime procesadoEnUtc) =>
        new() { EventId = eventId, Resultado = resultado, Motivo = motivo, ProcesadoEn = procesadoEnUtc };
}