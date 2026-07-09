# LupiraGeoApi

Geo/gazetteer service for Lupira: a shared catalog of real-world **places** (with coordinates + an administrative
containment tree), **geocoding** (forward + reverse via a self-hosted Nominatim), and per-principal **saved places**.
Other services reference a place by id and resolve free-text locations through `POST /places/resolve`. Built to grow
into a Google-Maps-style capability — spatial storage (PostGIS) is first-class.

.NET 10 · Marten (`geo_user` schema) + EF Core/PostGIS (`geo` schema) in one Postgres database · Authentik OIDC ·
MCP (LAN-only). See [docs/architecture.md](docs/architecture.md).

## Surface

- `GET /places` — search: text (`q`), `category`/`kind`, containment (`withinAreaId`), proximity (`nearLat`+`nearLon`[+`radiusM`]) or viewport (`bbox`).
- `GET /places/{id}` · `POST /places` · `PATCH /places/{id}` · `POST /places/resolve`
- `GET /geocode/reverse` · `GET /geocode/forward`
- `GET /admin-areas` · `GET /admin-areas/{id}`
- `GET|POST|PATCH|DELETE /me/places` — saved places
- `GET /me` · `/livez` · `/readyz` · `/openapi/v1.json` · `/scalar/v1` · `/mcp` (LAN-only)

## Develop

```bash
# A local PostGIS (the geo schema needs postgis + pg_trgm):
docker run -d --name geo-db -e POSTGRES_PASSWORD=postgres -e POSTGRES_DB=lupira_geo -p 5432:5432 postgis/postgis:17-3.5

dotnet run --project src/LupiraGeoApi        # Development auto-migrates EF + accepts X-Dev-User
# → http://localhost:5260/scalar

dotnet test LupiraGeoApi.slnx                # unit tests (no I/O)
dotnet test tests/LupiraGeoApi.IntegrationTests   # integration (Testcontainers PostGIS)
```

Geocoding is optional: set `Nominatim__BaseUrl` to enable it (unset → resolver provisions user places, no external call).

## Migrations

```bash
dotnet ef migrations add <Name> --project src/LupiraGeoApi.Core --startup-project src/LupiraGeoApi --output-dir Data/Migrations
```

## Deploy

Image `danbro96/lupira-geo-api`. Provision the DB + PostGIS with [deploy/db/grants.sql](deploy/db/grants.sql) (postgis
created as superuser — it is untrusted), then:

```bash
dotnet LupiraGeoApi.dll --apply-schema        # Marten apply + EF migrate
dotnet LupiraGeoApi.dll --seed-gazetteer      # optional: seed AdminArea tree from GeoNames
```

See [deploy/compose.yaml](deploy/compose.yaml). Host at `geo-api.lupira.com` (REST at root, `/mcp` LAN-only).

---

Place data © OpenStreetMap contributors (via Nominatim), ODbL. Administrative data © GeoNames, CC BY 4.0.
