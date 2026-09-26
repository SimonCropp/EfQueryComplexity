// Keys the shared model does not have. Kept out of TestDbContext, since it is also the LocalDB
// schema.
public class KeyContext(DbContextOptions<KeyContext> options) :
    DbContext(options)
{
    public DbSet<Course> Courses => Set<Course>();
    public DbSet<Enrollment> Enrollments => Set<Enrollment>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        var course = builder.Entity<Course>();
        course.HasAlternateKey(_ => _.Code);
        course.HasIndex(_ => _.Title).IsUnique();

        builder.Entity<OnlineCourse>();

        builder.Entity<Enrollment>()
            .HasKey(_ => new
            {
                _.StudentId,
                _.CourseId
            });
    }
}

public class Course
{
    public int Id { get; set; }

    // An alternate key
    public string Code { get; set; } = "";

    // A unique index
    public string Title { get; set; } = "";

    public int Credits { get; set; }
}

// A derived type, which shares the key of its base type
public class OnlineCourse :
    Course
{
    public string Url { get; set; } = "";
}

// A composite key
public class Enrollment
{
    public int StudentId { get; set; }
    public int CourseId { get; set; }
}
