public class TestDbContext(DbContextOptions<TestDbContext> options) :
    DbContext(options)
{
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<EmployeeTask> EmployeeTasks => Set<EmployeeTask>();
}
