# Poultry Farm .NET Platform

This folder contains the new enterprise-standard .NET foundation for migrating the existing Django/Next.js poultry farm system.

## Structure

```text
src/
  PoultryFarm.Api/              ASP.NET Core API, Scalar docs, SignalR
  PoultryFarm.Application/      DTOs, validators, interfaces
  PoultryFarm.Domain/           Enterprise domain entities and rules
  PoultryFarm.Infrastructure/   EF Core, PostgreSQL, Identity
  PoultryFarm.Blazor/           Farm + platform Blazor dashboard
  PoultryFarm.Marketplace/      Public marketplace Blazor app
tests/
  PoultryFarm.UnitTests/
  PoultryFarm.IntegrationTests/
```

## PostgreSQL

The API uses PostgreSQL via Npgsql. Configure the connection with user secrets (do not commit real passwords):

```bash
dotnet user-secrets set "ConnectionStrings:DefaultConnection" \
  "Host=YOUR_HOST;Port=5432;Database=poultry_farm;Username=poultry_farm_user;Password=YOUR_PASSWORD" \
  --project src/PoultryFarm.Api
```

Placeholder connection strings live in `appsettings.json` / `appsettings.Development.json`.

Apply schema:

```bash
dotnet ef database update \
  --project src/PoultryFarm.Infrastructure \
  --startup-project src/PoultryFarm.Api
```

If you see `permission denied for schema public` (common on PostgreSQL 15+), run this once as a superuser on the server:

```sql
GRANT USAGE, CREATE ON SCHEMA public TO poultry_farm_user;
GRANT ALL PRIVILEGES ON DATABASE poultry_farm TO poultry_farm_user;
ALTER SCHEMA public OWNER TO poultry_farm_user;
```

Then re-run `dotnet ef database update` (or just start the API; development seeding also calls `MigrateAsync()`).

List migrations:

```bash
dotnet ef migrations list \
  --project src/PoultryFarm.Infrastructure \
  --startup-project src/PoultryFarm.Api
```

## Common Commands

```bash
dotnet restore PoultryFarm.slnx
dotnet build PoultryFarm.slnx
dotnet test PoultryFarm.slnx
dotnet run --project src/PoultryFarm.Api
dotnet run --project src/PoultryFarm.Blazor
dotnet run --project src/PoultryFarm.Marketplace
```

Restart the API after marketplace schema or controller changes so migrations and routes load. Guest marketplace chat needs the API plus Marketplace (and farm Blazor for farm replies).

## Docker on the home server

Images: `poultry-farm-api`, `poultry-farm-dashboard`, `poultry-farm-marketplace`.

```bash
cp docker.env.example docker.env
# Set PUBLIC_HOST, the PostgreSQL password, and Jwt__SigningKey in docker.env
docker compose --env-file docker.env up -d --build
```

Published ports:

```text
API          http://<PUBLIC_HOST>:5100
Dashboard    http://<PUBLIC_HOST>:5083
Marketplace  http://<PUBLIC_HOST>:5084
```

The API applies database migrations on startup and stores listing uploads in the `api-uploads` volume. `docker.env` stays on the server and is not committed.

These URLs work for devices on the same Tailscale network. Opening them to the public internet also needs router port forwarding or Tailscale Funnel for ports 5100, 5083, and 5084.

## Email (Resend)

Farm registration approval emails are sent with the [Resend](https://resend.com) API. Configure secrets (do not commit real keys):

```bash
dotnet user-secrets set "Resend:ApiKey" "re_xxxxxxxx" --project src/PoultryFarm.Api
dotnet user-secrets set "Resend:FromEmail" "noreply@your-verified-domain.com" --project src/PoultryFarm.Api
dotnet user-secrets set "Resend:FromName" "Akokɔ Papa" --project src/PoultryFarm.Api
```

`Resend:FromEmail` must be a verified domain sender in Resend (or `onboarding@resend.dev` for early testing). After a super admin approves a farm registration, the applicant receives an invitation email with the `/setup-password?token=...` link.

The farm dashboard runs at:

```text
http://localhost:5083
```

The public marketplace runs at:

```text
http://localhost:5084
```

Scalar API docs are available at:

```text
http://localhost:<api-port>/scalar
```

SignalR activity hub:

```text
/hubs/activity
```
