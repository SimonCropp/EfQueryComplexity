public class Company
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public List<Department> Departments { get; set; } = [];
}

public class Department
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int CompanyId { get; set; }
    public Company Company { get; set; } = null!;
    public List<Employee> Employees { get; set; } = [];
}

public class Employee
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int Salary { get; set; }
    public int DepartmentId { get; set; }
    public Department Department { get; set; } = null!;
    public List<EmployeeTask> Tasks { get; set; } = [];
}

public class EmployeeTask
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public int EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;
}
