# ORM Comparison: Entity Framework vs Dapper vs ADO.NET

This project implements data access three ways, all behind the same `ILoginDataService` interface.
That means you can swap the active ORM for any query by changing a single Ninject binding — 
the business layer and API layer never know the difference.

---

## Quick Reference

| | Entity Framework 6 | Dapper | ADO.NET (ODBC) |
|---|---|---|---|
| **Abstraction level** | Highest | Medium | Lowest |
| **SQL control** | Auto-generated | Full control | Full control |
| **Performance** | Lower (change tracking overhead) | Near-raw SQL | Raw SQL |
| **Code verbosity** | Minimal | Low | High |
| **Best for** | Complex object graphs, CRUD, rapid development | High-throughput reads, complex queries | Fine-grained connection control, legacy DB drivers |
| **In this project** | `DbService.GetDataTable<T>()` | `db.Query<EmployeeMaster>()` | `Ado.GetDataTable()` / `GetDataSet()` |

---

## Entity Framework 6

### What it does

EF maps C# objects to database tables (or query results) through a `DbContext`. You write LINQ
or pass raw SQL; EF handles connection lifecycle, materialization, and (optionally) change tracking.

### How it's used here

`DbService` wraps the EF context with a generic query method:

```csharp
// DataService/DbService.cs
public class DbService
{
    Context _db = new Context(); // EF DbContext

    public List<T> GetDataTable<T>(string sqlQuery, T responseObj)
    {
        return _db.Database.SqlQuery<T>(sqlQuery).ToList();
    }
}
```

`Database.SqlQuery<T>()` executes a raw SQL string and materializes the results directly into any
POCO type `T` — no `DbSet<T>` mapping required. This gives EF's connection and materialization
infrastructure without requiring schema-mapped entities.

The commented-out call in `LoginDataService`:

```csharp
// DataService/LoginDataService.cs
// lstResponse = _db.GetDataTable<EmployeeMaster>(query.ToString(), new EmployeeMaster()).ToList();
```

### When to choose EF

- You want LINQ-based queries with compile-time safety
- You're building CRUD-heavy endpoints where EF's `Add`/`Remove`/`SaveChanges()` lifecycle matters
- You need lazy loading, navigation properties, or migrations
- Rapid prototyping where raw SQL isn't required

### When EF is not the right choice

- High-throughput read endpoints (change tracking adds memory overhead even when disabled)
- Complex multi-join queries that EF generates poorly
- Databases with no ORM provider (e.g., ODBC-only legacy systems)

---

## Dapper

### What it does

Dapper is a micro-ORM: a thin extension on top of `IDbConnection`. You write SQL; Dapper maps
the result rows to C# objects by matching column names to property names. No change tracking,
no migration engine, no configuration file — just SQL and a mapping.

### How it's used here

`DapperClass` in `Database/Context.cs` returns an open connection:

```csharp
// Database/Context.cs
public static class DapperClass
{
    private static readonly string connectionString =
        ConfigurationManager.ConnectionStrings[dataProvider].ConnectionString;

    public static IDbConnection Connection()
    {
        return new OdbcConnection(connectionString);
    }
}
```

The query in `LoginDataService` (commented out, ready to activate):

```csharp
// DataService/LoginDataService.cs
using (IDbConnection db = DapperClass.Connection())
{
    lstResponse = db.Query<EmployeeMaster>(query.ToString()).ToList();
}
```

The `using` block ensures the connection is disposed after the query. Dapper's `Query<T>()` maps
each row to `EmployeeMaster` by matching column names — zero configuration needed if names align.

### When to choose Dapper

- Read-heavy endpoints that need near-raw-SQL performance
- Complex queries with multiple joins, CTEs, or window functions that are hard to express in LINQ
- Stored procedures with multiple result sets
- You want full SQL control but don't want to write `DataReader` boilerplate

### When Dapper is not the right choice

- You need write operations with change tracking (`INSERT`/`UPDATE`/`DELETE` require manual SQL)
- You want schema migrations managed in code
- The object graph is deeply nested (EF navigation properties are more ergonomic)

---

## ADO.NET

### What it does

ADO.NET is the base data access layer in .NET — no ORM, no abstraction above the database
driver. You open a connection, create a command, execute it, and read the results manually.
Everything EF and Dapper do for you, you do yourself.

### How it's used here

The `Ado` static class in `Database/Context.cs` wraps three patterns:

```csharp
// Pattern 1: Simple OdbcConnection + OdbcDataAdapter
public static DataSet GetDataSet(string spName)
{
    using (OdbcConnection connection = new OdbcConnection(connectionString))
    {
        OdbcDataAdapter adapter = new OdbcDataAdapter(spName, connection);
        DataSet ds = new DataSet();
        connection.Open();
        adapter.Fill(ds);
        return ds;
    }
}

// Pattern 2: Explicit command object (more control over CommandType, parameters)
public static DataSet GetDataSet1(string spName)
{
    using (OdbcConnection connection = new OdbcConnection())
    using (OdbcCommand command = new OdbcCommand())
    using (OdbcDataAdapter adapter = new OdbcDataAdapter())
    {
        command.CommandText = spName;
        command.CommandType = CommandType.Text;
        command.Connection = connection;
        adapter.SelectCommand = command;
        connection.Open();
        adapter.Fill(ds);
        return ds;
    }
}

// Pattern 3: DbProviderFactory — database-agnostic (ODBC or SQL Server, same code)
public static DataSet GetDataSetByFactory(string spName)
{
    using (DbConnection connection = factory.CreateConnection())
    using (DbCommand command = factory.CreateCommand())
    using (DbDataAdapter adapter = factory.CreateDataAdapter())
    {
        // Works with any provider registered in Web.config <DbProviderFactories>
        ...
    }
}
```

The `DbProviderFactory` pattern (`GetDataSetByFactory`) is noteworthy: by reading the provider
name from `Web.config` appSettings, you can switch between ODBC and SQL Server connections
without changing application code — only configuration changes.

The commented-out ADO.NET path in `LoginDataService`:

```csharp
// DataTable dtList = Ado.GetDataTable(query.ToString());
// foreach (DataRow dr in dtList.Rows)
// {
//     EmployeeMaster objResponse = new EmployeeMaster();
//     objResponse.FName = Convert.ToString(dr["fname"]);
//     objResponse.LName = Convert.ToString(dr["lname"]);
//     lstResponse.Add(objResponse);
// }
```

### When to choose ADO.NET

- Legacy databases accessible only via ODBC (as in this project — SAP SQL Anywhere)
- You need precise control over connection pooling, transaction isolation, or command timeouts
- The team policy mandates stored procedures only — no inline SQL, no ORM query generation
- You're writing a high-performance bulk-insert path and need `SqlBulkCopy`
- Debugging ORM-generated SQL is causing more problems than writing it manually

### When ADO.NET is not the right choice

- Greenfield development with a mainstream database (EF or Dapper are faster to write)
- Object mapping needs: manual `DataRow["column"]` → property assignment is tedious and error-prone at scale

---

## Switching the Active ORM

All three strategies implement `ILoginDataService`. To activate Dapper instead of the current
static mock:

1. Uncomment the Dapper block in `DataService/LoginDataService.cs`
2. Comment out the static data block
3. Ensure `Web.config` has the correct `DataProvider` key and connection string

No changes needed in `LoginService`, `LoginController`, or anywhere else in the stack.
This is the concrete payoff of the interface-per-layer design.

---

## In a Real Codebase

These three aren't mutually exclusive. A mature codebase typically uses:

- **EF** for write operations (insert, update, delete) and for bounded domains with rich object graphs
- **Dapper** for read-optimized query endpoints (reporting, search, list views)
- **ADO.NET** for bulk operations, custom connection handling, or database types without an EF provider

The ability to discuss this trade-off — and back it up with working code for each — is the
point of having all three in this project.
