public class BenchmarkContext(DbContextOptions<BenchmarkContext> options) :
    DbContext(options)
{
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Order> Orders => Set<Order>();
}

public class Customer
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public List<Order> Orders { get; set; } = [];
}

public class Order
{
    public int Id { get; set; }
    public int Quantity { get; set; }
    public int CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;
}
