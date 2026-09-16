public static class AssemblySetup
{
    public static SqlInstance<TestDbContext> SqlInstance { get; private set; } = null!;

    // Not a ModuleInitializer. Microsoft.Testing.Platform can start the test exe twice, as a test
    // host controller and as the test host, and both processes would build the same template at
    // once. This hook only runs in the process that executes tests.
    [Before(HookType.Assembly)]
    public static void Setup() =>
        SqlInstance = new(
            constructInstance: builder => new(builder.UseQueryComplexity().Options),
            buildTemplate: async context =>
            {
                await context.Database.EnsureCreatedAsync();

                var company = new Company
                {
                    Name = "Acme"
                };
                var department = new Department
                {
                    Name = "Engineering",
                    Company = company
                };
                department.Employees.Add(
                    new()
                    {
                        Name = "Alice",
                        Salary = 100
                    });
                department.Employees.Add(
                    new()
                    {
                        Name = "Bob",
                        Salary = 90
                    });
                company.Departments.Add(department);
                context.Companies.Add(company);

                await context.SaveChangesAsync();
            });
}
