using Delegat.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Delegat.Tests.Infrastructure;

public static class TestDbFactory
{
    public static DataContext CreateDb(string? name = null)
    {
        var options = new DbContextOptionsBuilder<DataContext>()
            .UseInMemoryDatabase(name ?? Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new DataContext(options);
    }

    public static UnitOfWork CreateUnitOfWork(DataContext db) => new(db);
}
