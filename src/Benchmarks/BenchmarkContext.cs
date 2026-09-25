public class BenchmarkContext(DbContextOptions<BenchmarkContext> options) :
    DbContext(options)
{
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Order> Orders => Set<Order>();

    // The query the execution benchmarks run. It has a Take count and a Contains list, so both value
    // checks run on every execution
    public IQueryable<Customer> CustomersIn(List<int> ids) =>
        Customers
            .Where(_ => ids.Contains(_.Id))
            .OrderBy(_ => _.Name)
            .Take(10);
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
