public class CounterTests
{
    [Test]
    public async Task Null() =>
        await Assert.That(Counter.Count(null)).IsEqualTo(0);

    // A string is enumerable, but it is never a list of values
    [Test]
    public async Task String() =>
        await Assert.That(Counter.Count("abc")).IsEqualTo(0);

    [Test]
    public async Task NotEnumerable() =>
        await Assert.That(Counter.Count(5)).IsEqualTo(0);

    [Test]
    public async Task Collection() =>
        await Assert.That(Counter.Count(new List<int> {1, 2, 3})).IsEqualTo(3);

    [Test]
    public async Task GenericCollection() =>
        await Assert.That(Counter.Count(new HashSet<int> {1, 2, 3})).IsEqualTo(3);

    [Test]
    public async Task ReadOnlyCollection() =>
        await Assert.That(Counter.Count(new ReadOnlyValues([1, 2, 3]))).IsEqualTo(3);

    [Test]
    public async Task Enumerable() =>
        await Assert.That(Counter.Count(Values(3))).IsEqualTo(3);

    static IEnumerable<int> Values(int count)
    {
        for (var index = 0; index < count; index++)
        {
            yield return index;
        }
    }

    // Only IReadOnlyCollection, so the count comes from that interface
    class ReadOnlyValues(int[] values) :
        IReadOnlyCollection<int>
    {
        public int Count => values.Length;

        public IEnumerator<int> GetEnumerator() =>
            ((IEnumerable<int>) values).GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() =>
            GetEnumerator();
    }
}
