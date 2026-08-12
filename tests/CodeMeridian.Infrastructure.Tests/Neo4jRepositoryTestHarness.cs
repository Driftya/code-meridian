using NSubstitute;
using Neo4j.Driver;

namespace CodeMeridian.Infrastructure.Tests;

internal sealed class Neo4jRepositoryTestHarness
{
    public Neo4jRepositoryTestHarness()
    {
        Driver = Substitute.For<IDriver>();
        Session = Substitute.For<IAsyncSession>();
        Cursor = Substitute.For<IResultCursor>();

        Driver.AsyncSession(Arg.Any<Action<SessionConfigBuilder>>()).Returns(Session);
        Session.RunAsync(
                Arg.Any<string>(),
                Arg.Any<object>(),
                Arg.Any<Action<TransactionConfigBuilder>>())
            .Returns(Cursor);
        Cursor.FetchAsync().Returns(false);
    }

    public IDriver Driver { get; }

    public IAsyncSession Session { get; }

    public IResultCursor Cursor { get; }
}
