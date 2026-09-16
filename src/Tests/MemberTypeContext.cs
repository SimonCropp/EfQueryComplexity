

// Member types the shared model does not have. Kept out of TestDbContext, since it is also the
// LocalDB schema.
public class MemberTypeContext(DbContextOptions<MemberTypeContext> options) :
    DbContext(options)
{
    public DbSet<Owner> Owners => Set<Owner>();
    public DbSet<Pet> Pets => Set<Pet>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<Pet>()
            .HasOne(_ => _.Owner)
            .WithMany()
            .HasForeignKey(_ => _.OwnerId);

        builder.Entity<Owner>()
            .Property(_ => _.Flags)
            .HasConversion(
                _ => ToBytes(_),
                _ => new(_));
    }

    static byte[] ToBytes(BitArray bits)
    {
        var bytes = new byte[(bits.Length + 7) / 8];
        bits.CopyTo(bytes, 0);
        return bytes;
    }
}

public class Owner
{
    public int Id { get; set; }
    public string Name { get; set; } = "";

    // Enumerable, but with no element type
    public BitArray Flags { get; set; } = new(0);
}

public class Pet
{
    public int Id { get; set; }
    public int OwnerId { get; set; }

    // A navigation mapped to a field rather than a property
    public Owner Owner = null!;
}
