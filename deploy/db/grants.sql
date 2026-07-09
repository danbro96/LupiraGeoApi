-- lupira-geo-api: provision the `lupira_geo` database on the shared medelynas-db.
-- One role, one logical database, isolated from the other Lupira apps (no cross-grants). The app owns the `geo_user`
-- schema (Marten) AND the `geo` schema (EF Core/PostGIS), created via `--apply-schema` — none of those tables are
-- created here.
--
-- PostGIS is an UNTRUSTED extension, so the app role cannot create it. A superuser must enable it in this database
-- ONCE (last statement below). pg_trgm is trusted and is created by the EF migration.
--
-- Apply (TrueNAS Shell), substituting a freshly generated password:
--   LUPIRA_GEO_DB_PW="$(openssl rand -hex 32)"; echo "$LUPIRA_GEO_DB_PW"   # save to your password manager
--   docker exec -i medelynas-db psql -U medelynas_admin -v app_password="'$LUPIRA_GEO_DB_PW'" postgres < grants.sql

CREATE ROLE lupira_geo_user WITH LOGIN PASSWORD :'app_password';
CREATE DATABASE lupira_geo OWNER lupira_geo_user;
REVOKE ALL ON DATABASE lupira_geo FROM PUBLIC;
GRANT CONNECT ON DATABASE lupira_geo TO lupira_geo_user;

-- Enable PostGIS in the new database as superuser (the app role cannot; postgis is untrusted). Requires the server
-- image to ship PostGIS (the shared medelynas-db image must include it alongside pgvector).
\connect lupira_geo
CREATE EXTENSION IF NOT EXISTS postgis;
