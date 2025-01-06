using Marten;
using TestSetup;

namespace TestSetupTests;

public record SomethingHappened(string Message);

[TestFixture]
public class When_setting_up_test_eventstore
{
  private TestEventStore? _testEventStore;
  private IDocumentStore _store;

  [SetUp]
  public async Task InitializeAsync()
  {
    _testEventStore = await TestEventStore.InitializeAsync();
    _store = _testEventStore.Store;
  }

  [Test]
  public async Task should_write_events()
  {
    await using var session = _store.LightweightSession();
    var @event = new SomethingHappened("Test Succeeded");
    var streamId = Guid.NewGuid();
    session.Events.Append(streamId, @event);

    await session.SaveChangesAsync();

    var events = await session.Events.FetchStreamAsync(streamId);
    events.Count.ShouldBe(1);
  }

  [TearDown]
  public async Task DisposeAsync() => await _testEventStore?.DisposeAsync()!;
}
