-- Runs once, as the superuser, when the Postgres data directory is first initialised.
-- Passwords come from the environment via psql's \getenv (psql >= 15) so that no password is ever
-- written into this file or into the image.
--
-- Two roles, because the application must not be able to change its own schema:
--   eways_migrator  owns the database and every object the migrations create
--   eways_app       connects and reads/writes rows, owns nothing, and cannot DDL
\getenv app_password APP_DB_PASSWORD
\getenv migrator_password MIGRATOR_DB_PASSWORD
\getenv db_name POSTGRES_DB

CREATE ROLE eways_migrator LOGIN PASSWORD :'migrator_password';
CREATE ROLE eways_app LOGIN PASSWORD :'app_password';

ALTER DATABASE :"db_name" OWNER TO eways_migrator;
GRANT CONNECT ON DATABASE :"db_name" TO eways_app;

-- Applies to objects the migrator creates from here on, which is every table the app will touch.
ALTER DEFAULT PRIVILEGES FOR ROLE eways_migrator GRANT USAGE ON SCHEMAS TO eways_app;
ALTER DEFAULT PRIVILEGES FOR ROLE eways_migrator GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO eways_app;
ALTER DEFAULT PRIVILEGES FOR ROLE eways_migrator GRANT USAGE, SELECT, UPDATE ON SEQUENCES TO eways_app;
