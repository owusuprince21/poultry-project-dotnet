# Poultry Farm .NET Platform

This folder contains the new enterprise-standard .NET foundation for migrating the existing Django/Next.js poultry farm system.

## Structure

```text
src/
  PoultryFarm.Api/              ASP.NET Core API, Scalar docs, SignalR
  PoultryFarm.Application/      DTOs, validators, interfaces
  PoultryFarm.Domain/           Enterprise domain entities and rules
  PoultryFarm.Infrastructure/   EF Core, SQL Server, Identity
  PoultryFarm.Blazor/           Blazor UI with MudBlazor and Tailwind setup
tests/
  PoultryFarm.UnitTests/
  PoultryFarm.IntegrationTests/
```

## Local SQL Server

The API is configured for SQL Server:

```text
Server=localhost,1433;Database=PoultryFarm_Dev;User Id=sa;Password=YourStrong!Passw0rd;TrustServerCertificate=True;Encrypt=False
```

You can run SQL Server with Docker:

```bash
docker run -e ACCEPT_EULA=Y -e MSSQL_SA_PASSWORD='YourStrong!Passw0rd' -p 1433:1433 --name poultry-sql -d mcr.microsoft.com/mssql/server:2022-latest
```

## Common Commands

```bash
dotnet restore PoultryFarm.slnx
dotnet build PoultryFarm.slnx
dotnet test PoultryFarm.slnx
dotnet run --project src/PoultryFarm.Api
dotnet run --project src/PoultryFarm.Blazor
```

Scalar API docs are available at:

```text
http://localhost:<api-port>/scalar
```

SignalR activity hub:

```text
/hubs/activity
```
