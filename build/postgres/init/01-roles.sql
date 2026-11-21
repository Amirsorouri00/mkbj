-- Runs once, as the superuser, when the Postgres data directory is first initialised.
-- Passwords come from the environment via psql's \getenv (psql >= 15) so that no password is ever
-- written into this file or into the image.
--
-- Two roles, because the application must not be able to change its own schema:
--   mkbj_migrator  owns the database and every object the migrations create
--   mkbj_app       connects and reads/writes rows, owns nothing, and cannot DDL
\getenv app_password APP_DB_PASSWORD
\getenv migrator_password MIGRATOR_DB_PASSWORD
\getenv db_name POSTGRES_DB

CREATE ROLE mkbj_migrator LOGIN PASSWORD :'migrator_password';
CREATE ROLE mkbj_app LOGIN PASSWORD :'app_password';

ALTER DATABASE :"db_name" OWNER TO mkbj_migrator;
GRANT CONNECT ON DATABASE :"db_name" TO mkbj_app;

-- Applies to objects the migrator creates from here on, which is every table the app will touch.
ALTER DEFAULT PRIVILEGES FOR ROLE mkbj_migrator GRANT USAGE ON SCHEMAS TO mkbj_app;
ALTER DEFAULT PRIVILEGES FOR ROLE mkbj_migrator GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO mkbj_app;
ALTER DEFAULT PRIVILEGES FOR ROLE mkbj_migrator GRANT USAGE, SELECT, UPDATE ON SEQUENCES TO mkbj_app;
