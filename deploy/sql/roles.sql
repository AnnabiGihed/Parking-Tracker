-- Roles, database and grants of Parking Tracker (ADR-0010, ADR-0028).
--
-- pt-deploy-root runs this file before every migration, inside the postgres container as the superuser:
--   psql -U postgres -d postgres -f - < roles.sql
-- It is idempotent: it creates what is missing and sets the passwords again from the secret files, so a password
-- changed in /opt/apps/parking-tracker/secrets takes effect on the next deployment.
--
-- pt_owner owns the database and its schema and runs the migrations; pt_api reads and writes the tables pt_owner
-- creates and can create nothing. The Jobs and admin roles come with their hosts (S6, S8).

\set ON_ERROR_STOP on
\set owner_password `cat /run/secrets/pt_owner_password`
\set api_password `cat /run/secrets/pt_api_password`

SELECT 'CREATE ROLE pt_owner' WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'pt_owner') \gexec
SELECT 'CREATE ROLE pt_api' WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'pt_api') \gexec
SELECT format('ALTER ROLE pt_owner LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE PASSWORD %L', :'owner_password') \gexec
SELECT format('ALTER ROLE pt_api LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE PASSWORD %L', :'api_password') \gexec

SELECT 'CREATE DATABASE parkingdata OWNER pt_owner'
WHERE NOT EXISTS (SELECT 1 FROM pg_database WHERE datname = 'parkingdata') \gexec

REVOKE ALL ON DATABASE parkingdata FROM PUBLIC;
GRANT CONNECT ON DATABASE parkingdata TO pt_owner, pt_api;

\connect parkingdata

-- PostGIS is not a trusted extension, so the superuser creates it; the migration's own
-- CREATE EXTENSION IF NOT EXISTS postgis then finds it and does nothing.
CREATE EXTENSION IF NOT EXISTS postgis;

REVOKE CREATE ON SCHEMA public FROM PUBLIC;
GRANT USAGE, CREATE ON SCHEMA public TO pt_owner;
GRANT USAGE ON SCHEMA public TO pt_api;

-- Tables and sequences the migrations create later; the existing ones get the same rights below.
ALTER DEFAULT PRIVILEGES FOR ROLE pt_owner IN SCHEMA public
	GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO pt_api;
ALTER DEFAULT PRIVILEGES FOR ROLE pt_owner IN SCHEMA public
	GRANT USAGE, SELECT ON SEQUENCES TO pt_api;

DO $$
DECLARE
	owned record;
BEGIN
	FOR owned IN
		SELECT c.relname, c.relkind
		FROM pg_class c
		JOIN pg_namespace n ON n.oid = c.relnamespace
		WHERE n.nspname = 'public' AND pg_get_userbyid(c.relowner) = 'pt_owner' AND c.relkind IN ('r', 'p', 'S')
	LOOP
		IF owned.relkind = 'S' THEN
			EXECUTE format('GRANT USAGE, SELECT ON SEQUENCE public.%I TO pt_api', owned.relname);
		ELSE
			EXECUTE format('GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE public.%I TO pt_api', owned.relname);
		END IF;
	END LOOP;
END
$$;
