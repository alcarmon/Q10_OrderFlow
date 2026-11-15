namespace Inventory.Worker.Domain;

public class StockItem
{
    private StockItem() { }

    public StockItem(string sku, int disponible)
    {
        Sku = sku;
        Disponible = disponible;
    }

    public string Sku { get; private set; } = default!;
    public int Disponible { get; private set; }
}